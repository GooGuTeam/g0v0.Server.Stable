// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Client;
using g0v0.Server.Stable.Packets.Server;

namespace g0v0.Server.Stable.Routes;

/// <summary>
/// Dispatches authenticated stable packets and returns framed response packets.
/// </summary>
/// <param name="facade">Services available to packet readers and handlers.</param>
/// <param name="logger">The logger used when a handler fails.</param>
public class PacketRouteHandler(IPacketFacade facade, ILogger<PacketRouteHandler>? logger = null) : IRouteHandler
{
    /// <inheritdoc />
    public async Task<IResult> HandleAsync(HttpContext context)
    {
        if (!string.Equals(context.Request.Headers.UserAgent.ToString(), "osu!", StringComparison.Ordinal))
        {
            return Results.StatusCode(StatusCodes.Status422UnprocessableEntity);
        }

        string token = context.Request.Headers["osu-token"].ToString();
        IPlayer? player = string.IsNullOrEmpty(token) ? null : facade.PlayerManager.GetPlayer(token);
        if (player == null)
        {
            return Results.Bytes(
                PacketConvert.SerializeAll([
                    new PacketNotification { Message = "Server has restarted." },
                    new PacketRestart { Milliseconds = 0 }
                ]),
                contentType: "application/octet-stream");
        }

        // Packet readers are synchronous; buffer the asynchronous HTTP body first.
        using MemoryStream body = new();
        await context.Request.Body.CopyToAsync(body, context.RequestAborted);
        body.Position = 0;
        foreach (IPacket incoming in PacketConvert.DeserializeAll(body, facade))
        {
            context.RequestAborted.ThrowIfCancellationRequested();
            if (!ReferenceEquals(facade.PlayerManager.GetPlayer(player.Token), player))
            {
                break;
            }

            if (incoming is not IClientPacket packet)
            {
                continue;
            }

            if (player.IsRestricted && !IsAllowedForRestricted(packet))
            {
                logger?.LogDebug("Ignored packet {PacketName} of restricted session {UserId}.", packet.GetType().Name, player.Id);
                continue;
            }

            IPacket[] packets;
            try
            {
                packets = await packet.Handle(player, facade);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A broken handler must not fail the whole poll: the client treats a non-200
                // response as a lost connection and stops sending packets.
                logger?.LogWarning(exception, "Packet {PacketId} of session {UserId} failed to handle.", packet.GetType().Name, player.Id);
                continue;
            }

            foreach (IPacket packetToSend in packets)
            {
                player.Enqueue(packetToSend);
            }
        }

        if (ReferenceEquals(facade.PlayerManager.GetPlayer(player.Token), player))
        {
            player.LastReceiveTime = DateTimeOffset.UtcNow;
        }

        return Results.Bytes(player.Dequeue(), contentType: "application/octet-stream");
    }

    /// <summary>
    /// Decides whether a restricted session may send a packet.
    /// </summary>
    /// <param name="packet">The incoming packet.</param>
    /// <returns><see langword="true"/> when the packet is allowed for a restricted account.</returns>
    /// <remarks>
    /// The flag is stored on the registration table, so the check is a dictionary lookup instead of
    /// an attribute reflection per packet.
    /// </remarks>
    private static bool IsAllowedForRestricted(IClientPacket packet)
    {
        Type type = packet.GetType();
        return OsuPacketAttribute.Packets.Values
            .Where(registration => registration.Type == type)
            .Any(registration => registration.AllowRestrict);
    }
}
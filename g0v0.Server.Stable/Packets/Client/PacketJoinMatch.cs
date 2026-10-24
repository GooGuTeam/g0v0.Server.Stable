// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C32 JOIN_MATCH. Payload members are declared in wire order.
/// </summary>
[OsuPacket(32)]
public class PacketJoinMatch : IClientPacket
{
    /// <summary>
    /// Gets or sets MatchId.
    /// </summary>
    public int MatchId { get; set; }

    /// <summary>
    /// Gets or sets Password.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <inheritdoc />
    public Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        // The handler is not implemented yet; the dispatcher logs and skips it, so an explicit
        // unsupported-operation error keeps the poll alive instead of failing the request.
        throw new NotSupportedException($"{GetType().Name} is not implemented by this server yet.");
    }
}
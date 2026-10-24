// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C2 LOGOUT: the client's explicit "I am leaving" notification.
/// </summary>
/// <remarks>
/// <para>
/// Payload (in wire order):
/// </para>
/// <code>
/// &lt;reserved&gt;: i32      always zero; read so the frame is consumed completely
/// </code>
/// <para>
/// The session is dropped from every chat channel and from the player registry, after which the
/// remaining clients receive <c>S12 USER_LOGOUT</c>. The account is only marked offline and stamped
/// with a last-visit time once its final session is gone, because stable allows several clients per
/// account.
/// </para>
/// <para>
/// The client often closes without sending this packet, so the same bookkeeping also runs from the
/// session-expiry sweep; this handler covers the explicit path only.
/// </para>
/// </remarks>
[OsuPacket(2, allowRestrict: true)]
public class PacketLogout : IClientPacket
{
    /// <summary>
    /// Gets or sets the reserved field.
    /// </summary>
    public int Reserved { get; set; }

    /// <inheritdoc />
    public async Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        ArgumentNullException.ThrowIfNull(player);
        await facade.ChatServer.DisconnectAsync(player).ConfigureAwait(false);
        await player.Offline().ConfigureAwait(false);

        // Another client of the same account keeps it online; only the last one may clear the flag.
        if (facade.PlayerManager.GetPlayerAllInstances(player.Id).Count != 0)
        {
            return [];
        }

        using IServiceScope scope = facade.ScopeFactory.CreateScope();
        IUserRepository users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        ILogger<PacketLogout> logger = scope.ServiceProvider.GetRequiredService<ILogger<PacketLogout>>();
        User? user = await users.GetByIdAsync(player.Id).ConfigureAwait(false);
        if (user is null)
        {
            logger.LogWarning("Logged out session {UserId} has no user row; the online flag was not cleared.", player.Id);
            return [];
        }

        user.IsOnline = false;
        user.LastVisit = DateTimeOffset.UtcNow;
        await users.UpdateAsync(user).ConfigureAwait(false);
        logger.LogInformation("User {UserId} went offline at {LastVisit:O}.", user.Id, user.LastVisit);
        return [];
    }
}
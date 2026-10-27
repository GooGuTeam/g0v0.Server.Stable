// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Services;

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
        await SessionLifecycle.DisconnectAsync(player, facade.PlayerManager, facade.ChatServer, facade.ScopeFactory, TimeProvider.System).ConfigureAwait(false);
        return [];
    }
}
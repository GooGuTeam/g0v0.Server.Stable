// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C87 MATCH_INVITE. Payload members are declared in wire order.
/// </summary>
[OsuPacket(87)]
public class PacketMatchInvite : IClientPacket
{
    /// <summary>
    /// Gets or sets UserId.
    /// </summary>
    public int UserId { get; set; }

    /// <inheritdoc />
    public async Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        await facade.RoomManager.InviteAsync(player, UserId).ConfigureAwait(false);
        return [];
    }
}
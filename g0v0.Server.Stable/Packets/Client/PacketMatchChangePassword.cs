// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C90 MATCH_CHANGE_PASSWORD. Payload members are declared in wire order.
/// </summary>
[OsuPacket(90)]
public class PacketMatchChangePassword : IClientPacket
{
    /// <summary>
    /// Gets or sets Match.
    /// </summary>
    public Match Match { get; set; } = new();

    /// <inheritdoc />
    public async Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        await facade.RoomManager.ExecuteAsync(player, room => room.ChangePasswordAsync(player, Match.Password)).ConfigureAwait(false);
        return [];
    }
}
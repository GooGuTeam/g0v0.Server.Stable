// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C31 CREATE_MATCH. Payload members are declared in wire order.
/// </summary>
[OsuPacket(31)]
public class PacketCreateMatch : IClientPacket
{
    /// <summary>
    /// Gets or sets Match.
    /// </summary>
    public Match Match { get; set; } = new();

    /// <inheritdoc />
    public async Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        await facade.RoomManager.CreateRoomAsync(player, Match).ConfigureAwait(false);
        return [];
    }
}
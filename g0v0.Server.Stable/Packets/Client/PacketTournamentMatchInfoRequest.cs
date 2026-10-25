// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C93 TOURNAMENT_MATCH_INFO_REQUEST. Payload members are declared in wire order.
/// </summary>
[OsuPacket(93)]
public class PacketTournamentMatchInfoRequest : IClientPacket
{
    /// <summary>
    /// Gets or sets MatchId.
    /// </summary>
    public int MatchId { get; set; }

    /// <inheritdoc />
    public async Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        await facade.RoomManager.SendRoomInfoAsync(player, MatchId).ConfigureAwait(false);
        return [];
    }
}
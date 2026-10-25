// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C47 MATCH_SCORE_UPDATE. Payload members are declared in wire order.
/// </summary>
[OsuPacket(47)]
public class PacketMatchScoreUpdate : IClientPacket
{
    /// <summary>
    /// Gets or sets the fully parsed score snapshot, including the optional ScoreV2 portions.
    /// </summary>
    public ScoreFrame Frame { get; set; } = new();

    /// <inheritdoc />
    public async Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        await facade.RoomManager.ExecuteAsync(player, room => room.ScoreUpdateAsync(player, Frame)).ConfigureAwait(false);
        return [];
    }
}
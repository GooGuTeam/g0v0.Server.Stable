// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C60 MATCH_SKIP_REQUEST. Empty payload.
/// </summary>
[OsuPacket(60)]
public class PacketMatchSkipRequest : IClientPacket
{
    /// <inheritdoc />
    public async Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        await facade.RoomManager.ExecuteAsync(player, room => room.SkipAsync(player)).ConfigureAwait(false);
        return [];
    }
}
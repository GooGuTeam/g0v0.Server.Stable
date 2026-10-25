// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C52 MATCH_LOAD_COMPLETE. Empty payload.
/// </summary>
[OsuPacket(52)]
public class PacketMatchLoadComplete : IClientPacket
{
    /// <inheritdoc />
    public async Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        await facade.RoomManager.ExecuteAsync(player, room => room.LoadCompleteAsync(player)).ConfigureAwait(false);
        return [];
    }
}
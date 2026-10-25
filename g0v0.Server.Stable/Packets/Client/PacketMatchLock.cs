// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C40 MATCH_LOCK. Payload members are declared in wire order.
/// </summary>
[OsuPacket(40)]
public class PacketMatchLock : IClientPacket
{
    /// <summary>
    /// Gets or sets SlotId.
    /// </summary>
    public int SlotId { get; set; }

    /// <inheritdoc />
    public async Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        await facade.RoomManager.ExecuteAsync(player, room => room.LockSlotAsync(player, SlotId)).ConfigureAwait(false);
        return [];
    }
}
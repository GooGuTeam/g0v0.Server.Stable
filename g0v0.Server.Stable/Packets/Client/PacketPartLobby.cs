// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C29 PART_LOBBY. Empty payload.
/// </summary>
[OsuPacket(29)]
public class PacketPartLobby : IClientPacket
{
    /// <inheritdoc />
    public Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        facade.RoomManager.LeaveLobby(player);
        return Task.FromResult<IPacket[]>([]);
    }
}
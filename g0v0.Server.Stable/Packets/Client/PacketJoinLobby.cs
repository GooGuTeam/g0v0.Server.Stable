// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C30 JOIN_LOBBY. Empty payload.
/// </summary>
[OsuPacket(30)]
public class PacketJoinLobby : IClientPacket
{
    /// <inheritdoc />
    public async Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        await facade.RoomManager.JoinLobbyAsync(player).ConfigureAwait(false);
        return [];
    }
}
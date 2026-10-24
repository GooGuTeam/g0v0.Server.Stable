// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C17 STOP_SPECTATING. Empty payload.
/// </summary>
[OsuPacket(17)]
public class PacketStopSpectating : IClientPacket
{
    /// <inheritdoc />
    public async Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        await player.StopWatchPlayer();
        return [];
    }
}
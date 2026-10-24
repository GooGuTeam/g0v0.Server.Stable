// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C18 SPECTATE_FRAMES. Payload members are declared in wire order.
/// </summary>
[OsuPacket(18)]
public class PacketSpectateFrames : IClientPacket
{
    /// <summary>
    /// Gets or sets FrameBundle.
    /// </summary>
    public ReplayFrameBundle FrameBundle { get; set; } = new();

    /// <inheritdoc />
    public async Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        await player.SendFrames(FrameBundle);
        return [];
    }
}
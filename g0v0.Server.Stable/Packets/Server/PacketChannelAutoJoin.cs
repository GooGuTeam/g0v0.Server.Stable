// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S67 CHANNEL_AUTO_JOIN. Payload members are declared in wire order.
/// </summary>
[OsuPacket(67)]
public class PacketChannelAutoJoin : IPacket
{
    /// <summary>
    /// Gets or sets Channel.
    /// </summary>
    public ChannelWire Channel { get; set; } = new();
}
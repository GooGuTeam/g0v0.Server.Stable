// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S65 CHANNEL_INFO. Payload members are declared in wire order.
/// </summary>
[OsuPacket(65)]
public class PacketChannelInfo : IPacket
{
    /// <summary>
    /// Gets or sets Channel.
    /// </summary>
    public ChannelWire Channel { get; set; } = new();
}
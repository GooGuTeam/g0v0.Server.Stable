// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S66 CHANNEL_KICK. Payload members are declared in wire order.
/// </summary>
[OsuPacket(66)]
public class PacketChannelKick : IPacket
{
    /// <summary>
    /// Gets or sets Name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}
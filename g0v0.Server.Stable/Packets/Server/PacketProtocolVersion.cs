// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S75 PROTOCOL_VERSION. Payload members are declared in wire order.
/// </summary>
[OsuPacket(75)]
public class PacketProtocolVersion : IPacket
{
    /// <summary>
    /// Gets or sets Version.
    /// </summary>
    public int Version { get; set; }
}
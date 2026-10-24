// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S86 RESTART. Payload members are declared in wire order.
/// </summary>
[OsuPacket(86)]
public class PacketRestart : IPacket
{
    /// <summary>
    /// Gets or sets Milliseconds.
    /// </summary>
    public int Milliseconds { get; set; }
}
// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S103 SWITCH_SERVER. Payload members are declared in wire order.
/// </summary>
[OsuPacket(103)]
public class PacketSwitchServer : IPacket
{
    /// <summary>
    /// Gets or sets Threshold.
    /// </summary>
    public int Threshold { get; set; }
}
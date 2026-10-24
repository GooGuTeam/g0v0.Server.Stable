// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S28 DISPOSE_MATCH. Payload members are declared in wire order.
/// </summary>
[OsuPacket(28)]
public class PacketDisposeMatch : IPacket
{
    /// <summary>
    /// Gets or sets Id.
    /// </summary>
    public int Id { get; set; }
}
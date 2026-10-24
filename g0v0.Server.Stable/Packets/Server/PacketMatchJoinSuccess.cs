// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S36 MATCH_JOIN_SUCCESS. Payload members are declared in wire order.
/// </summary>
[OsuPacket(36)]
public class PacketMatchJoinSuccess : IPacket
{
    /// <summary>
    /// Gets or sets Match.
    /// </summary>
    public Match Match { get; set; } = new();
}
// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S48 MATCH_SCORE_UPDATE. Payload members are declared in wire order.
/// </summary>
[OsuPacket(48)]
public class PacketMatchScoreUpdate : IPacket
{
    /// <summary>
    /// Gets or sets Frame.
    /// </summary>
    public ScoreFrame Frame { get; set; } = new();
}
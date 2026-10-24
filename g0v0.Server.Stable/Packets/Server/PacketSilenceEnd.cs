// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S92 SILENCE_END. Payload members are declared in wire order.
/// </summary>
[OsuPacket(92)]
public class PacketSilenceEnd : IPacket
{
    /// <summary>
    /// Gets or sets SecondsRemaining.
    /// </summary>
    public int SecondsRemaining { get; set; }
}
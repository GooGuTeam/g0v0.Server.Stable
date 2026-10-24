// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S14 SPECTATOR_LEFT. Payload members are declared in wire order.
/// </summary>
[OsuPacket(14)]
public class PacketSpectatorLeft : IPacket
{
    /// <summary>
    /// Gets or sets UserId.
    /// </summary>
    public int UserId { get; set; }
}
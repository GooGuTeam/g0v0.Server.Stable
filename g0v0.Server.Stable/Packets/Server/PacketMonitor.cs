// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S80 MONITOR. Empty payload.
/// </summary>
[OsuPacket(80)]
public class PacketMonitor : IPacket
{
}
// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S19 VERSION_UPDATE. Empty payload.
/// </summary>
[OsuPacket(19)]
public class PacketVersionUpdate : IPacket
{
}
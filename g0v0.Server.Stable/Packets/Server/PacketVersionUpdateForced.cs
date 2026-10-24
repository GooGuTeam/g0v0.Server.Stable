// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S102 VERSION_UPDATE_FORCED. Empty payload.
/// </summary>
[OsuPacket(102)]
public class PacketVersionUpdateForced : IPacket
{
}
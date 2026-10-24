// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S37 MATCH_JOIN_FAIL. Empty payload.
/// </summary>
[OsuPacket(37)]
public class PacketMatchJoinFail : IPacket
{
}
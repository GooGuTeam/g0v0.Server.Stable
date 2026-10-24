// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S89 CHANNEL_INFO_END. Empty payload.
/// </summary>
[OsuPacket(89)]
public class PacketChannelInfoEnd : IPacket
{
}
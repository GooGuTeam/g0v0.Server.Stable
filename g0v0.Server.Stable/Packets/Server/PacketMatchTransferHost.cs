// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S50 MATCH_TRANSFER_HOST. Empty payload.
/// </summary>
[OsuPacket(50)]
public class PacketMatchTransferHost : IPacket
{
}
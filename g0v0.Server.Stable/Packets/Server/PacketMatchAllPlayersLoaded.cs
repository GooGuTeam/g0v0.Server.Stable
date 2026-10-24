// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S53 MATCH_ALL_PLAYERS_LOADED. Empty payload.
/// </summary>
[OsuPacket(53)]
public class PacketMatchAllPlayersLoaded : IPacket
{
}
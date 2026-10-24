// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S107 SWITCH_TOURNAMENT_SERVER. Payload members are declared in wire order.
/// </summary>
[OsuPacket(107)]
public class PacketSwitchTournamentServer : IPacket
{
    /// <summary>
    /// Gets or sets Ip.
    /// </summary>
    public string Ip { get; set; } = string.Empty;
}
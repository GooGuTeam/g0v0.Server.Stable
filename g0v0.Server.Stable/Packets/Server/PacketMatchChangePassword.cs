// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S91 MATCH_CHANGE_PASSWORD. Payload members are declared in wire order.
/// </summary>
[OsuPacket(91)]
public class PacketMatchChangePassword : IPacket
{
    /// <summary>
    /// Gets or sets NewPassword.
    /// </summary>
    public string NewPassword { get; set; } = string.Empty;
}
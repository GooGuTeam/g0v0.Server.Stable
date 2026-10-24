// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S12 USER_LOGOUT. Payload members are declared in wire order.
/// </summary>
[OsuPacket(12)]
public class PacketUserLogout : IPacket
{
    /// <summary>
    /// Gets or sets UserId.
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Gets or sets Reserved.
    /// </summary>
    public byte Reserved { get; set; }
}
// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S71 PRIVILEGES. Payload members are declared in wire order.
/// </summary>
[OsuPacket(71)]
public class PacketPrivileges : IPacket
{
    /// <summary>
    /// Gets or sets Privileges.
    /// </summary>
    public ClientPrivileges Privileges { get; set; }
}
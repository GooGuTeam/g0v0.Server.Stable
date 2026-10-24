// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S96 USER_PRESENCE_BUNDLE. Payload members are declared in wire order.
/// </summary>
[OsuPacket(96)]
public class PacketUserPresenceBundle : IPacket
{
    /// <summary>
    /// Gets or sets the payload value. IdList16: ushort count followed by signed 32-bit user IDs.
    /// </summary>
    public int[] UserIds { get; set; } = [];
}
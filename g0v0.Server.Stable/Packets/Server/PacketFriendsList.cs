// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S72 FRIENDS_LIST. Payload members are declared in wire order.
/// </summary>
[OsuPacket(72)]
public class PacketFriendsList : IPacket
{
    /// <summary>
    /// Gets or sets the payload value. IdList16: ushort count followed by signed 32-bit user IDs.
    /// </summary>
    public int[] Friends { get; set; } = [];
}
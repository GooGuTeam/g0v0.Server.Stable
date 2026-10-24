// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S100 USER_DM_BLOCKED. Payload members are declared in wire order.
/// </summary>
[OsuPacket(100)]
public class PacketUserDmBlocked : IPacket
{
    /// <summary>
    /// Gets or sets Message.
    /// </summary>
    public Message Message { get; set; } = new();
}
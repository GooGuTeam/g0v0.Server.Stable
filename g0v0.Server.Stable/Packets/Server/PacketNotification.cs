// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S24 NOTIFICATION. Payload members are declared in wire order.
/// </summary>
[OsuPacket(24)]
public class PacketNotification : IPacket
{
    /// <summary>
    /// Gets or sets Message.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
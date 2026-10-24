// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S57 MATCH_PLAYER_FAILED. Payload members are declared in wire order.
/// </summary>
[OsuPacket(57)]
public class PacketMatchPlayerFailed : IPacket
{
    /// <summary>
    /// Gets or sets SlotId.
    /// </summary>
    public int SlotId { get; set; }
}
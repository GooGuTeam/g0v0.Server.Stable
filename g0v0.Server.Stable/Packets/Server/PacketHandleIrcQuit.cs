// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S10 HANDLE_IRC_QUIT. Historical packet with no defined payload; Data preserves uninterpreted bytes.
/// </summary>
[OsuPacket(10, rawData: true)]
public class PacketHandleIrcQuit : IPacket
{
    /// <summary>
    /// Gets or sets the payload value. Raw payload bytes with no inner length prefix.
    /// </summary>
    public byte[] Data { get; set; } = [];
}
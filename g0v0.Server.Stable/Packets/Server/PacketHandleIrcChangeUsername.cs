// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S9 HANDLE_IRC_CHANGE_USERNAME. Payload members are declared in wire order.
/// </summary>
[OsuPacket(9)]
public class PacketHandleIrcChangeUsername : IPacket
{
    /// <summary>
    /// Gets or sets the payload value. Old and new usernames joined by four greater-than characters.
    /// </summary>
    public string UsernameChange { get; set; } = string.Empty;
}
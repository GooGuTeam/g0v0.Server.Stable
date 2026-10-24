// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S76 MAIN_MENU_ICON. Payload members are declared in wire order.
/// </summary>
[OsuPacket(76)]
public class PacketMainMenuIcon : IPacket
{
    /// <summary>
    /// Gets or sets the payload value. Image URL and click URL joined by a single pipe character.
    /// </summary>
    public string Icon { get; set; } = string.Empty;
}
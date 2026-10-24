// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C63 CHANNEL_JOIN. Payload members are declared in wire order.
/// </summary>
[OsuPacket(63, allowRestrict: true)]
public class PacketChannelJoin : IClientPacket
{
    /// <summary>
    /// Gets or sets Name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc />
    public Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        return facade.ChatServer.JoinChannelAsync(player, Name);
    }
}
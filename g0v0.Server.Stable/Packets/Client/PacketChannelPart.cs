// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C78 CHANNEL_PART. Payload members are declared in wire order.
/// </summary>
[OsuPacket(78, allowRestrict: true)]
public class PacketChannelPart : IClientPacket
{
    /// <summary>
    /// Gets or sets Name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc />
    public Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        return facade.ChatServer.LeaveChannelAsync(player, Name);
    }
}
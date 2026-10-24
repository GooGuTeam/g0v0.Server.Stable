// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C25 SEND_PRIVATE_MESSAGE. Payload members are declared in wire order.
/// </summary>
[OsuPacket(25)]
public class PacketSendPrivateMessage : IClientPacket
{
    /// <summary>
    /// Gets or sets Message.
    /// </summary>
    public Message Message { get; set; } = new();

    /// <inheritdoc />
    public Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        return facade.ChatServer.SendPrivateMessageAsync(player, Message.Recipient, Message.Text);
    }
}
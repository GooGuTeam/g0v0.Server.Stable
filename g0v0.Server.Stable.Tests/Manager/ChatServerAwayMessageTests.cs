// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Client;
using g0v0.Server.Stable.Packets.Server;
using Moq;

namespace g0v0.Server.Stable.Tests.Manager;

public partial class ChatServerTests
{
    [Test]
    public async Task AwayRepliesUseTheRecipientsIdentityAndStillDeliverTheOriginalMessage()
    {
        await SetAwayMessageAsync("Back after lunch");
        _bob.Activity.Action = UserAction.Afk;

        IPacket[] response = await _chats.SendPrivateMessageAsync(_alice, "bob", "hello");

        using MemoryStream replyBytes = new(PacketConvert.SerializeAll(response));
        PacketSendMessage reply = PacketConvert.Deserialize<PacketSendMessage>(replyBytes, new PacketFacade())!;
        Assert.That(reply.Message.Sender, Is.EqualTo("bob"));
        Assert.That(reply.Message.SenderId, Is.EqualTo(2));
        Assert.That(reply.Message.Recipient, Is.EqualTo("alice"));
        Assert.That(reply.Message.Text, Is.EqualTo("Back after lunch"));
        using MemoryStream deliveredBytes = new(_bob.Dequeue());
        PacketSendMessage delivered = PacketConvert.Deserialize<PacketSendMessage>(deliveredBytes, new PacketFacade())!;
        Assert.That(delivered.Message.SenderId, Is.EqualTo(1));
        Assert.That(delivered.Message.Text, Is.EqualTo("hello"));
        _messages.Verify(repo => repo.CreateMessageAsync(3, 1, "hello", MessageType.Plain, null), Times.Once);
        _messages.Verify(repo => repo.CreateMessageAsync(It.IsAny<int>(), 2, It.IsAny<string>(), It.IsAny<MessageType>(), It.IsAny<string?>()), Times.Never);
    }

    [Test]
    public async Task AwayRepliesStopWhenTheRecipientReturnsAndCanBeReplacedOrCleared()
    {
        await SetAwayMessageAsync("first reply");
        Assert.That(_bob.Activity.Action, Is.EqualTo(UserAction.Idle));
        Assert.That(await _chats.SendPrivateMessageAsync(_alice, "bob", "while idle"), Is.Empty);

        _bob.Activity.Action = UserAction.Afk;
        await SetAwayMessageAsync("replacement reply");
        PacketSendMessage reply = (PacketSendMessage)(await _chats.SendPrivateMessageAsync(_alice, "bob", "while away")).Single();
        Assert.That(reply.Message.Text, Is.EqualTo("replacement reply"));

        _bob.Activity.Action = UserAction.Playing;
        Assert.That(await _chats.SendPrivateMessageAsync(_alice, "bob", "while playing"), Is.Empty);
        _bob.Activity.Action = UserAction.Afk;
        await SetAwayMessageAsync(string.Empty);
        Assert.That(await _chats.SendPrivateMessageAsync(_alice, "bob", "after clearing"), Is.Empty);
    }

    [Test]
    public async Task AwayRepliesRequireAnAcceptedNonemptyPrivateMessage()
    {
        await SetAwayMessageAsync("away");
        _bob.Activity.Action = UserAction.Afk;
        _bobUser.PmFriendsOnly = true;

        Assert.That(await _chats.SendPrivateMessageAsync(_alice, "bob", "   "), Is.Empty);
        AssertPrivateMessageBlocked(await _chats.SendPrivateMessageAsync(_alice, "bob", "blocked"));
        Assert.That(_alice.Dequeue(), Is.Empty);
    }

    private async Task SetAwayMessageAsync(string text)
    {
        PacketFacade facade = new();
        using MemoryStream bytes = new(PacketConvert.Serialize(new PacketSetAwayMessage
        {
            Message = new Message { Sender = "forged", SenderId = 999, Recipient = "ignored", Text = text }
        }));
        PacketSetAwayMessage packet = PacketConvert.Deserialize<PacketSetAwayMessage>(bytes, facade)!;
        await packet.Handle(_bob, facade);
    }
}
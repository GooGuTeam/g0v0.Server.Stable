// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Server;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PacketToggleBlockNonFriendDms = g0v0.Server.Stable.Packets.Client.PacketToggleBlockNonFriendDms;

namespace g0v0.Server.Stable.Tests.Manager;

public partial class ChatServerTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task PrivateMessagesRequireTheRecipientToFollowTheSender(bool senderFollows, bool recipientFollows)
    {
        _bobUser.PmFriendsOnly = true;
        _relationships.Setup(repo => repo.IsFollowingAsync(1, 2)).ReturnsAsync(senderFollows);
        _relationships.Setup(repo => repo.IsFollowingAsync(2, 1)).ReturnsAsync(recipientFollows);

        IPacket[] response = await _chats.SendPrivateMessageAsync(_alice, "bob", "hello");

        if (recipientFollows)
        {
            Assert.That(response, Is.Empty);
            Assert.That(_bob.Dequeue(), Is.Not.Empty);
            _messages.Verify(repo => repo.CreateMessageAsync(3, 1, "hello", MessageType.Plain, null), Times.Once);
        }
        else
        {
            AssertPrivateMessageBlocked(response);
        }
    }

    [TestCase(1, 2)]
    [TestCase(2, 1)]
    public async Task PrivateMessagesRespectBlocksInEitherDirectionEvenBetweenFriends(int blocker, int blocked)
    {
        _relationships.Setup(repo => repo.IsFollowingAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(value: true);
        _relationships.Setup(repo => repo.IsBlockedAsync(blocker, blocked)).ReturnsAsync(value: true);

        IPacket[] response = await _chats.SendPrivateMessageAsync(_alice, "bob", "hello");

        AssertPrivateMessageBlocked(response);
    }

    [Test]
    public async Task SendersWithFriendsOnlyEnabledMustFollowTheRecipient()
    {
        _aliceUser.PmFriendsOnly = true;
        _relationships.Setup(repo => repo.IsFollowingAsync(2, 1)).ReturnsAsync(value: true);

        IPacket[] rejected = await _chats.SendPrivateMessageAsync(_alice, "bob", "hello");

        Assert.That(rejected.Single(), Is.TypeOf<PacketNotification>());
        Assert.That(_bob.Dequeue(), Is.Empty);
        _messages.Verify(repo => repo.GetOrCreatePmChannelAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);

        _relationships.Setup(repo => repo.IsFollowingAsync(1, 2)).ReturnsAsync(value: true);
        Assert.That(await _chats.SendPrivateMessageAsync(_alice, "bob", "hello"), Is.Empty);
        Assert.That(_bob.Dequeue(), Is.Not.Empty);
    }

    [TestCase(1)]
    [TestCase(2)]
    public async Task PrivateMessagesFailClosedWhenAnAccountNoLongerExists(int missingUser)
    {
        _users.Setup(repo => repo.GetByIdAsync(missingUser)).ReturnsAsync((User?)null);

        AssertPrivateMessageBlocked(await _chats.SendPrivateMessageAsync(_alice, "bob", "hello"));
    }

    [Test]
    public async Task BotSendersHaveTheSamePrivacyExemptionAsRealtime()
    {
        _aliceUser.IsBot = true;
        _bobUser.PmFriendsOnly = true;
        _relationships.Setup(repo => repo.IsBlockedAsync(2, 1)).ReturnsAsync(value: true);

        Assert.That(await _chats.SendPrivateMessageAsync(_alice, "bob", "hello"), Is.Empty);
        Assert.That(_bob.Dequeue(), Is.Not.Empty);

        _aliceUser.PmFriendsOnly = true;
        Assert.That((await _chats.SendPrivateMessageAsync(_alice, "bob", "hello")).Single(), Is.TypeOf<PacketNotification>());
        Assert.That(_bob.Dequeue(), Is.Empty);
    }

    [Test]
    public async Task ToggleFriendsOnlyPersistsAndImmediatelyAffectsOtherSessions()
    {
        PacketFacade facade = new() { ScopeFactory = _services.GetRequiredService<IServiceScopeFactory>() };
        Player tournamentSession = new(2, "bob-tournament", isTourneyClient: true) { Name = "bob" };
        List<bool> savedPreferences = [];
        _users.Setup(repo => repo.UpdateAsync(_bobUser)).Callback<User>(user => savedPreferences.Add(user.PmFriendsOnly))
            .Returns(Task.CompletedTask);

        using MemoryStream enableBytes = new(PacketConvert.Serialize(new PacketToggleBlockNonFriendDms { Value = 1 }));
        PacketToggleBlockNonFriendDms enable = PacketConvert.Deserialize<PacketToggleBlockNonFriendDms>(enableBytes, facade)!;
        await enable.Handle(tournamentSession, facade);
        AssertPrivateMessageBlocked(await _chats.SendPrivateMessageAsync(_alice, "bob", "blocked"));

        await new PacketToggleBlockNonFriendDms { Value = 0 }.Handle(tournamentSession, facade);
        Assert.That(await _chats.SendPrivateMessageAsync(_alice, "bob", "allowed"), Is.Empty);
        Assert.That(_bob.Dequeue(), Is.Not.Empty);
        Assert.That(savedPreferences, Is.EqualTo((bool[])[true, false]));
    }

    private void AssertPrivateMessageBlocked(IPacket[] response)
    {
        Assert.That(response.Single(), Is.TypeOf<PacketUserDmBlocked>());
        using MemoryStream bytes = new(PacketConvert.Serialize(response.Single()));
        PacketUserDmBlocked packet = PacketConvert.Deserialize<PacketUserDmBlocked>(bytes, new PacketFacade())!;
        Assert.That(packet.Message.Recipient, Is.EqualTo("bob"));
        Assert.That(packet.Message.Sender, Is.Empty);
        Assert.That(packet.Message.Text, Is.Empty);
        Assert.That(packet.Message.SenderId, Is.Zero);
        Assert.That(_bob.Dequeue(), Is.Empty);
        _messages.Verify(repo => repo.GetOrCreatePmChannelAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        _messages.Verify(repo => repo.CreateMessageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<MessageType>(), It.IsAny<string?>()), Times.Never);
    }
}
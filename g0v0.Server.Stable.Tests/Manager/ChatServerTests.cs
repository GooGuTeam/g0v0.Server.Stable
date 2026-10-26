// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Stable.Manager;
using g0v0.Server.Stable.Objects.Chats;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using osu.Game.Online.Chat;

namespace g0v0.Server.Stable.Tests.Manager;

public partial class ChatServerTests
{
    private ServiceProvider _services = null!;
    private PlayerManager _players = null!;
    private ChatServer _chats = null!;
    private Player _alice = null!;
    private Player _bob = null!;
    private User _aliceUser = null!;
    private User _bobUser = null!;
    private Mock<IUserRepository> _users = null!;
    private Mock<IRelationshipRepository> _relationships = null!;
    private Mock<IChatRepository> _messages = null!;

    [SetUp]
    public void SetUp()
    {
        _aliceUser = new User { Id = 1, Username = "alice" };
        _bobUser = new User { Id = 2, Username = "bob" };
        _users = new Mock<IUserRepository>();
        _users.Setup(repo => repo.GetByIdAsync(1)).ReturnsAsync(_aliceUser);
        _users.Setup(repo => repo.GetByIdAsync(2)).ReturnsAsync(_bobUser);
        _relationships = new Mock<IRelationshipRepository>();
        _messages = Mock.Get(DefaultRepository());
        _messages.Setup(repo => repo.GetOrCreatePmChannelAsync(1, 2))
            .ReturnsAsync(new ChatChannel { ChannelId = 3, Name = "pm_1_2" });
        _services = new ServiceCollection().AddLogging()
            .AddSingleton(_users.Object)
            .AddSingleton(_relationships.Object)
            .AddSingleton(_messages.Object)
            .BuildServiceProvider();
        _players = new PlayerManager();
        _chats = CreateChatServer();
        _alice = new Player(1, "alice-token") { Name = "alice" };
        _bob = new Player(2, "bob-token") { Name = "bob" };
        _players.AddPlayer(_alice);
        _players.AddPlayer(_bob);
        _alice.Dequeue();
        _bob.Dequeue();
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
    }

    [Test]
    public async Task LoginDirectoryEndsWithChannelInfoEnd()
    {
        IPacket[] packets = await _chats.ConnectAsync(_alice);
        Assert.That(packets[^1], Is.TypeOf<PacketChannelInfoEnd>());
        Assert.That(
            packets.OfType<PacketChannelInfo>().Select(packet => packet.Channel.Name),
            Is.EqualTo((string[])["#osu", "#announce"]));
    }

    [Test]
    public async Task DirectoryIsEmptyWhenTheStorageHasNoPublicChannel()
    {
        await _services.DisposeAsync();
        Mock<IChatRepository> repository = new();
        repository.Setup(repo => repo.GetPublicChannelsAsync()).ReturnsAsync([]);
        _services = new ServiceCollection().AddLogging()
            .AddScoped(_ => repository.Object)
            .BuildServiceProvider();
        _chats = CreateChatServer();

        IPacket[] packets = await _chats.ConnectAsync(_alice);

        Assert.That(packets, Has.Length.EqualTo(1));
        Assert.That(packets[0], Is.TypeOf<PacketChannelInfoEnd>());
    }

    [Test]
    public async Task JoiningAccountsTheSessionAndNotifiesTheOtherMembers()
    {
        IPacket[] packets = await _chats.JoinChannelAsync(_alice, "#osu");

        Assert.That(packets, Has.Length.EqualTo(2));
        Assert.That(packets[0], Is.TypeOf<PacketChannelJoinSuccess>());
        Assert.That(((PacketChannelJoinSuccess)packets[0]).Name, Is.EqualTo("#osu"));
        PacketChannelInfo info = (PacketChannelInfo)packets[1];
        Assert.That(info.Channel.PlayerCount, Is.EqualTo(1));

        // bob was never in the channel but still learns the member count.
        Assert.That(PacketConvert.DeserializeAll(new MemoryStream(_bob.Dequeue()), new PacketFacade()), Has.Length.EqualTo(1));
        Assert.That(_chats.GetPlayerChannels(_alice).Select(channel => channel.Name), Is.EqualTo((string[])["#osu"]));
    }

    [Test]
    public async Task JoiningTheSameChannelTwiceIsIgnored()
    {
        await _chats.JoinChannelAsync(_alice, "#osu");
        _alice.Dequeue();
        _bob.Dequeue();

        IPacket[] packets = await _chats.JoinChannelAsync(_alice, "#osu");

        Assert.That(packets, Is.Empty);
        Assert.That(_alice.Dequeue(), Is.Empty);
        Assert.That(_bob.Dequeue(), Is.Empty);
    }

    [TestCase("#does-not-exist")]
    [TestCase("")]
    public async Task JoiningAnUnknownChannelIsIgnored(string name)
    {
        IPacket[] packets = await _chats.JoinChannelAsync(_alice, name);
        Assert.That(packets, Is.Empty);
    }

    [Test]
    public async Task PartingKicksTheSessionAndUpdatesTheMemberCount()
    {
        await _chats.JoinChannelAsync(_alice, "#osu");
        _alice.Dequeue();
        _bob.Dequeue();

        IPacket[] packets = await _chats.LeaveChannelAsync(_alice, "#osu");

        Assert.That(packets[0], Is.TypeOf<PacketChannelKick>());
        Assert.That(((PacketChannelKick)packets[0]).Name, Is.EqualTo("#osu"));
        Assert.That(((PacketChannelInfo)packets[1]).Channel.PlayerCount, Is.Zero);
        Assert.That(_chats.GetPlayerChannels(_alice), Is.Empty);
    }

    [Test]
    public async Task ChannelMessagesReachEveryOtherMemberButNotTheSender()
    {
        await _chats.JoinChannelAsync(_alice, "#osu");
        await _chats.JoinChannelAsync(_bob, "#osu");
        _alice.Dequeue();
        _bob.Dequeue();

        IPacket[] response = await _chats.SendChannelMessageAsync(_alice, "#osu", "hello");

        Assert.That(response, Is.Empty);
        Assert.That(_alice.Dequeue(), Is.Empty, "the stable client echoes its own message");
        PacketSendMessage delivered = (PacketSendMessage)PacketConvert
            .DeserializeAll(new MemoryStream(_bob.Dequeue()), new PacketFacade())
            .Single();
        Assert.That(delivered.Message.Sender, Is.EqualTo("alice"));
        Assert.That(delivered.Message.Text, Is.EqualTo("hello"));
        Assert.That(delivered.Message.Recipient, Is.EqualTo("#osu"));
        Assert.That(delivered.Message.SenderId, Is.EqualTo(1));
    }

    [Test]
    public async Task ChannelMessagesRequireMembershipAndWritePrivileges()
    {
        await _chats.JoinChannelAsync(_bob, "#announce");
        _bob.Dequeue();
        _alice.Dequeue();

        Assert.That(await _chats.SendChannelMessageAsync(_alice, "#osu", "not joined"), Is.Empty);
        Assert.That(await _chats.SendChannelMessageAsync(_bob, "#announce", "not staff"), Is.Empty);

        _bob.Privileges = ClientPrivileges.Moderator;
        await _chats.SendChannelMessageAsync(_bob, "#announce", "staff");
        Assert.That(_bob.Dequeue(), Is.Empty);
        Assert.That(PacketConvert.DeserializeAll(new MemoryStream(_alice.Dequeue()), new PacketFacade()), Is.Empty);
    }

    [Test]
    public async Task PrivateMessagesReachTheRecipientAndOfflineUsersGetANotice()
    {
        IPacket[] response = await _chats.SendPrivateMessageAsync(_alice, "bob", "psst");

        Assert.That(response, Is.Empty);
        PacketSendMessage delivered = (PacketSendMessage)PacketConvert
            .DeserializeAll(new MemoryStream(_bob.Dequeue()), new PacketFacade())
            .Single();
        Assert.That(delivered.Message.Sender, Is.EqualTo("alice"));
        Assert.That(delivered.Message.Recipient, Is.EqualTo("bob"));
        Assert.That(delivered.Message.Text, Is.EqualTo("psst"));

        IPacket[] missing = await _chats.SendPrivateMessageAsync(_alice, "carol", "anyone?");
        Assert.That(missing.Single(), Is.TypeOf<PacketNotification>());
        Assert.That(((PacketNotification)missing.Single()).Message, Is.EqualTo("carol is not online."));
    }

    [Test]
    public async Task OversizedMessagesAreTruncatedAndReported()
    {
        await _chats.JoinChannelAsync(_alice, "#osu");
        await _chats.JoinChannelAsync(_bob, "#osu");
        _alice.Dequeue();
        _bob.Dequeue();

        int limit = new ChatConfig().MessageLengthLimit;
        IPacket[] response = await _chats.SendChannelMessageAsync(_alice, "#osu", new string('a', limit + 500));

        Assert.That(response.Single(), Is.TypeOf<PacketNotification>());
        PacketSendMessage delivered = (PacketSendMessage)PacketConvert
            .DeserializeAll(new MemoryStream(_bob.Dequeue()), new PacketFacade())
            .Single();
        Assert.That(delivered.Message.Text, Has.Length.EqualTo(limit));
    }

    [Test]
    public async Task DisconnectingDropsEveryMembership()
    {
        await _chats.JoinChannelAsync(_alice, "#osu");
        await _chats.JoinChannelAsync(_alice, "#announce");
        _bob.Dequeue();

        await _chats.DisconnectAsync(_alice);

        Assert.That(_chats.GetPlayerChannels(_alice), Is.Empty);
        Assert.That(_chats.GetChannels().Select(channel => channel.Count), Is.EqualTo((int[])[0, 0]));
    }

    [Test]
    public async Task ChannelsAreSharedAndOwnedByTheServer()
    {
        await _chats.ConnectAsync(_alice);
        IReadOnlyList<StableChannel> channels = _chats.GetChannels();
        Assert.That(channels.Select(channel => channel.Name), Is.EqualTo((string[])["#osu", "#announce"]));
        Assert.That(channels[0].ChannelId, Is.EqualTo(1));
    }

    private static IChatRepository DefaultRepository()
    {
        Mock<IChatRepository> repository = new();
        repository.Setup(repo => repo.GetPublicChannelsAsync())
            .ReturnsAsync([
                new ChatChannel { ChannelId = 1, Name = "osu!", Description = "General discussion.", Type = ChannelType.Public },
                new ChatChannel { ChannelId = 2, Name = "announce", Description = "News.", Type = ChannelType.Public, Moderated = true }
            ]);
        repository.Setup(repo => repo.CreateMessageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<MessageType>(), It.IsAny<string?>()))
            .ReturnsAsync(new ChatMessage());
        return repository.Object;
    }

    private ChatServer CreateChatServer() => new(
        NullLogger<ChatServer>.Instance,
        _services.GetRequiredService<IServiceScopeFactory>(),
        _players);
}
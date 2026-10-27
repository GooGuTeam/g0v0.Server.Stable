// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Stable.Manager;
using g0v0.Server.Stable.Objects.Chats;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Server;
using g0v0.Server.Stable.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using osu.Game.Rulesets.Osu.Mods;

namespace g0v0.Server.Stable.Tests.Services;

public class BanchoBotTests
{
    private static readonly string[] HelpLines =
    [
        "Available commands:",
        "!help [command]",
        "!roll [positive maximum, default 100]",
        "!re",
        "!pr",
        "!mp",
    ];

    private string _configPath = null!;
    private ServiceProvider _services = null!;
    private PlayerManager _players = null!;
    private Player _alice = null!;
    private Player _bob = null!;
    private BanchoBot _bot = null!;
    private ChatServer _chats = null!;
    private Mock<IScoreRepository> _scores = null!;
    private Mock<IChatRepository> _messages = null!;
    private Mock<IRoomManager> _rooms = null!;

    [SetUp]
    public void SetUp()
    {
        _configPath = Path.Combine(Path.GetTempPath(), "stable-bot-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(_configPath, "config"));
        Mock<IUserRepository> users = new();
        users.Setup(repository => repository.GetUsernameByIdAsync(3)).ReturnsAsync("BanchoBot");
        _scores = new Mock<IScoreRepository>();
        _messages = new Mock<IChatRepository>();
        _messages.Setup(repository => repository.GetPublicChannelsAsync()).ReturnsAsync([
            new ChatChannel { ChannelId = 1, Name = "#osu" },
            new ChatChannel { ChannelId = 2, Name = "#announce", Moderated = true }
        ]);
        _messages.Setup(repository => repository.GetOrCreatePmChannelAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(new ChatChannel { ChannelId = 50, Name = "pm" });
        _rooms = new Mock<IRoomManager>();
        _services = new ServiceCollection().AddSingleton(users.Object).AddSingleton(_messages.Object)
            .AddSingleton(new ConfigManager(_configPath)).AddSingleton(_scores.Object).AddSingleton(_rooms.Object).AddSingleton<BanchoBot>().BuildServiceProvider();
        _players = new PlayerManager();
        _alice = new Player(1, _players) { Name = "alice", Privileges = ClientPrivileges.Player };
        _bob = new Player(2, _players) { Name = "bob", Privileges = ClientPrivileges.Player };
        _players.AddPlayer(_alice);
        _players.AddPlayer(_bob);
        _bot = _services.GetRequiredService<BanchoBot>();
        _chats = new ChatServer(NullLogger<ChatServer>.Instance, _services.GetRequiredService<IServiceScopeFactory>(), _players);
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
        Directory.Delete(_configPath, recursive: true);
    }

    [TestCase("!roll 1", "alice rolls 1 point(s)")]
    [TestCase("!RoLl 2147483647", "alice rolls ")]
    [TestCase("!roll -1", "Usage:")]
    [TestCase("!roll 0", "Usage:")]
    [TestCase("!help mp", "!mp make")]
    [TestCase("!mp clearhost", "!mp clearhost is not supported.")]
    public async Task ParsesCommandsWithoutOverflowAndExplainsInvalidArguments(string command, string expected)
        => Assert.That(await _bot.ProcessAsync(_alice, channel: null, command), Does.StartWith(expected));

    [TestCase("hello")]
    [TestCase("!")]
    [TestCase("!unknown")]
    public async Task IgnoresNonCommandsAndUnknownCommands(string message)
        => Assert.That(await _bot.ProcessAsync(_alice, channel: null, message), Is.Null);

    [Test]
    public async Task PreviousFindsTheLastPassedScoreAfterFailedAttemptsInTheCurrentMode()
    {
        _alice.Activity.Mode = 2;
        Score[] recent = [new() { Passed = false }, new() { Passed = false }, new() { Passed = true, Pp = 123.45, Accuracy = 0.99, TotalScore = 1000 }];
        _scores.Setup(repository => repository.GetRecentByUserIdAndModeAsync(1, 2, It.IsAny<int>()))
            .ReturnsAsync((int _, int _, int limit) => recent.Take(limit).ToArray());
        Assert.That(await _bot.ProcessAsync(_alice, channel: null, "!pr"), Does.Contain("123.45pp"));
        _scores.Verify(repository => repository.GetRecentByUserIdAndModeAsync(1, 2, 4), Times.Once);
        Assert.That(await _bot.ProcessAsync(_alice, channel: null, "!re"), Does.Contain("0.00pp"));
    }

    [Test]
    public async Task PrivateHelpSendsEachCommandOnItsOwnMessage()
    {
        IPacket[] replies = await _chats.SendPrivateMessageAsync(_alice, "BanchoBot", "!help");
        Assert.That(replies.OfType<PacketSendMessage>().Select(packet => packet.Message.Text), Is.EqualTo(HelpLines));
    }

    [Test]
    public async Task PrivateCommandsWorkWithoutAnOnlineBotSessionAndPersistBothDirections()
    {
        IPacket[] reply = await _chats.SendPrivateMessageAsync(_alice, "BanchoBot", "!roll 1");
        PacketSendMessage message = reply.OfType<PacketSendMessage>().Single();
        Assert.That(message.Message.SenderId, Is.EqualTo(3));
        Assert.That(message.Message.Recipient, Is.EqualTo("alice"));
        Assert.That(message.Message.Text, Is.EqualTo("alice rolls 1 point(s)"));
        Assert.That(_players.GetPlayer(3), Is.Null);
        _messages.Verify(repository => repository.CreateMessageAsync(50, 1, "!roll 1", MessageType.Plain), Times.Once);
        _messages.Verify(repository => repository.CreateMessageAsync(50, 3, "alice rolls 1 point(s)", MessageType.Plain), Times.Once);
    }

    [Test]
    public async Task OrdinaryCommandsReplyPrivatelyWhileMultiplayerCommandsRemainInTheRoomConversation()
    {
        await _chats.JoinChannelAsync(_alice, "#osu");
        await _chats.JoinChannelAsync(_bob, "#osu");
        _bob.Dequeue();
        IPacket[] privateReply = await _chats.SendChannelMessageAsync(_alice, "#osu", "!roll 1");
        Assert.That(privateReply.OfType<PacketSendMessage>().Single().Message.Recipient, Is.EqualTo("alice"));
        Assert.That(Messages(_bob), Is.Empty);

        StableChannel room = new("#multiplayer", "test", channelId: 55);
        await _chats.JoinMultiplayerChannelAsync(_alice, room);
        await _chats.JoinMultiplayerChannelAsync(_bob, room);
        _rooms.Setup(manager => manager.ExecuteBotCommandAsync(_alice, room, "settings", It.IsAny<string[]>())).ReturnsAsync("Room settings");
        _alice.Dequeue();
        _bob.Dequeue();
        await _chats.SendChannelMessageAsync(_alice, "#multiplayer", "!mp settings");
        Assert.That(Messages(_bob).Select(packet => packet.Message.Text), Is.EqualTo((string[])["!mp settings", "Room settings"]));
        Assert.That(Messages(_alice).Single().Message.Text, Is.EqualTo("Room settings"));
    }

    [Test]
    public async Task MembershipAndModerationChecksRunBeforeExecutingCommands()
    {
        Assert.That(await _chats.SendChannelMessageAsync(_alice, "#osu", "!mp make unauthorised"), Is.Empty);
        await _chats.JoinChannelAsync(_alice, "#announce");
        Assert.That(await _chats.SendChannelMessageAsync(_alice, "#announce", "!mp make moderated"), Is.Empty);
        _rooms.Verify(manager => manager.CreateTournamentRoomAsync(It.IsAny<IPlayer>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task LongRepliesAreSplitAndOneBrokenRecipientDoesNotBlockOtherMembers()
    {
        StableChannel room = new("#multiplayer", "test");
        Mock<IPlayer> broken = new();
        broken.Setup(player => player.IsOnline).Returns(value: true);
        broken.Setup(player => player.Enqueue(It.IsAny<IPacket>())).Throws(new InvalidOperationException("disconnected"));
        room.Add(broken.Object);
        room.Add(_alice);
        _alice.Dequeue();
        await _chats.SendBotMessageAsync(room, new string('x', 901) + "\nlast line");
        string[] chunks = Messages(_alice).Select(packet => packet.Message.Text).ToArray();
        Assert.That(chunks.Select(chunk => chunk.Length), Is.EqualTo((int[])[450, 450, 1, 9]));
        Assert.That(string.Concat(chunks).Replace("\n", string.Empty, StringComparison.Ordinal), Is.EqualTo(new string('x', 901) + "last line"));
    }

    [Test]
    public async Task RecentUsesTheConfiguredRelaxModeAndHandlesAnEmptyHistory()
    {
        await File.WriteAllTextAsync(Path.Combine(_configPath, "config", "game.json"), "{\"EnableRelax\":true}");
        _alice.Activity.Mods = [new OsuModRelax()];
        _scores.Setup(repository => repository.GetRecentByUserIdAndModeAsync(1, 4, 1)).ReturnsAsync([]);
        Assert.That(await _bot.ProcessAsync(_alice, channel: null, "!re"), Is.EqualTo("You have no scores."));
        _scores.Verify(repository => repository.GetRecentByUserIdAndModeAsync(1, 4, 1), Times.Once);
    }

    [Test]
    public async Task PersistenceFailureDoesNotFailTheCommandReply()
    {
        _messages.Setup(repository => repository.CreateMessageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), MessageType.Plain))
            .ThrowsAsync(new InvalidOperationException("storage unavailable"));
        IPacket[] reply = await _chats.SendPrivateMessageAsync(_alice, "BanchoBot", "!roll 1");
        Assert.That(reply.OfType<PacketSendMessage>().Single().Message.Text, Is.EqualTo("alice rolls 1 point(s)"));
    }

    [TestCase("#multiplayer")]
    [TestCase("#spectator")]
    public async Task UtilityRepliesStayInPrivateRoomAndSpectatorChannels(string channelName)
    {
        if (string.Equals(channelName, "#multiplayer", StringComparison.Ordinal))
        {
            StableChannel room = new(channelName, "test", autoJoin: false);
            await _chats.JoinMultiplayerChannelAsync(_alice, room);
            await _chats.JoinMultiplayerChannelAsync(_bob, room);
        }
        else
        {
            _players.UserWatchingPlayer(_bob, _alice);
            _players.JoinSpectatorChannel(_alice);
            _players.JoinSpectatorChannel(_bob);
        }

        _alice.Dequeue();
        _bob.Dequeue();
        Assert.That(await _chats.SendChannelMessageAsync(_alice, channelName, "!roll 1"), Is.Empty);
        PacketSendMessage message = Messages(_bob).Single();
        Assert.That(message.Message.SenderId, Is.EqualTo(3));
        Assert.That(message.Message.Recipient, Is.EqualTo(channelName));
        Assert.That(message.Message.Text, Is.EqualTo("alice rolls 1 point(s)"));
        Assert.That(Messages(_alice).Single().Message.Text, Is.EqualTo(message.Message.Text));
    }

    private static PacketSendMessage[] Messages(Player player)
    {
        using MemoryStream stream = new(player.Dequeue());
        return PacketConvert.DeserializeAll(stream, new PacketFacade()).OfType<PacketSendMessage>().ToArray();
    }
}
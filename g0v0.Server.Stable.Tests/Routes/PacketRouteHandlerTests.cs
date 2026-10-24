// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Manager;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Client;
using g0v0.Server.Stable.Packets.Server;
using g0v0.Server.Stable.Routes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace g0v0.Server.Stable.Tests.Routes;

[NonParallelizable]
public class PacketRouteHandlerTests
{
    private ServiceProvider _services = null!;
    private PlayerManager _players = null!;
    private PacketFacade _facade = null!;
    private Player _player = null!;

    [SetUp]
    public void SetUp()
    {
        Mock<IChatRepository> chatRepository = TestChatRepository.Create();
        _services = new ServiceCollection().AddLogging()
            .AddScoped(_ => chatRepository.Object)
            .BuildServiceProvider();
        _players = new PlayerManager();
        _player = new Player(1, "session-token");
        _players.AddPlayer(_player);
        _facade = new PacketFacade
        {
            PlayerManager = _players,
            ChatServer = new ChatServer(NullLogger<ChatServer>.Instance, _services.GetRequiredService<IServiceScopeFactory>(), _players)
        };
        OsuPacketAttribute.Register(60000, typeof(TestPacket));
        OsuPacketAttribute.Register(60001, typeof(AllowedPacket));
    }

    [TearDown]
    public void TearDown()
    {
        OsuPacketAttribute.Unregister(60000);
        OsuPacketAttribute.Unregister(60001);
        _services.Dispose();
    }

    [TestCase(null)]
    [TestCase("unknown")]
    public async Task MissingSessionReturnsTheBpyRestartPackets(string? token)
    {
        DefaultHttpContext context = createContext([], token);
        await execute(context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(context.Response.ContentType, Is.EqualTo("application/octet-stream"));
        Assert.That(bytes(context), Is.EqualTo(PacketConvert.SerializeAll([
            new PacketNotification { Message = "Server has restarted." },
            new PacketRestart { Milliseconds = 0 }
        ])));
    }

    [TestCase("")]
    [TestCase("browser")]
    public async Task RejectsUserAgentsOtherThanOsu(string userAgent)
    {
        DefaultHttpContext context = createContext([]);
        context.Request.Headers.UserAgent = userAgent;
        await execute(context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(422));
    }

    [Test]
    public async Task ReadsMultiplePacketsAndReturnsResponsesInWireOrder()
    {
        DefaultHttpContext context = createContext(PacketConvert.SerializeAll([
            new TestPacket { Value = 12 }, new TestPacket { Value = 34 }
        ]));
        await execute(context);
        Assert.That(_facade.PlayerManager, Is.SameAs(_players));
        Assert.That(_player.LastReceiveTime, Is.GreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.That(bytes(context), Is.EqualTo(PacketConvert.SerializeAll([
            new PacketNotification { Message = "session-token:12" },
            new PacketNotification { Message = "session-token:34" }
        ])));
    }

    [Test]
    public async Task RestrictedSessionsOnlyDispatchAllowedPackets()
    {
        _player.IsRestricted = true;
        DefaultHttpContext context = createContext(PacketConvert.SerializeAll([
            new TestPacket { Value = 1 }, new AllowedPacket { Value = 2 }
        ]));
        await execute(context);
        Assert.That(bytes(context), Is.EqualTo(PacketConvert.Serialize(new PacketNotification { Message = "session-token:2" })));
    }

    [Test]
    public async Task SkipsUnknownAndServerPacketsAndStopsAtATruncatedFrame()
    {
        using MemoryStream body = new();
        body.Write([255, 255, 0, 0, 0, 0, 0]);
        PacketConvert.Serialize(body, new PacketNotification { Message = "ignored" });
        PacketConvert.Serialize(body, new TestPacket { Value = 5 });
        body.Write([96, 234, 0, 255, 255, 255, 255]);
        PacketConvert.Serialize(body, new TestPacket { Value = 99 });
        DefaultHttpContext context = createContext(body.ToArray());
        await execute(context);
        Assert.That(bytes(context), Is.EqualTo(PacketConvert.Serialize(new PacketNotification { Message = "session-token:5" })));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(-1)]
    [TestCase(3)]
    public async Task ReceiveUpdatesDoesNotPreventPersonalStatisticsInTheSameRequest(int filter)
    {
        _player.PresenceFilter = PresenceFilter.Friends;
        _player.Statistics.RankedScore = 123456;
        DefaultHttpContext context = createContext(PacketConvert.SerializeAll([
            new PacketReceiveUpdates { Value = (PresenceFilter)filter },
            new PacketUserStatsRequest { UserIds = [_player.Id] }
        ]));
        await execute(context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(_player.PresenceFilter, Is.EqualTo(filter is >= 0 and <= 2 ? (PresenceFilter)filter : PresenceFilter.Friends));
        using MemoryStream stream = new(bytes(context));
        PacketUserStats statistics = (PacketUserStats)PacketConvert.DeserializeAll(stream, _facade).Single();
        Assert.That(statistics.UserId, Is.EqualTo(_player.Id));
        Assert.That(statistics.RankedScore, Is.EqualTo(123456));
    }

    [Test]
    public async Task EmptyPollReturnsAnEmptyBody()
    {
        DefaultHttpContext context = createContext([]);
        await execute(context);
        Assert.That(bytes(context), Is.Empty);
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
    }

    [Test]
    public async Task EmptyPollDeliversAnnouncementsQueuedByAnotherPlayer()
    {
        Player other = new(2) { Name = "other" };
        _players.AddPlayer(other);
        DefaultHttpContext context = createContext([]);
        await execute(context);
        using MemoryStream stream = new(bytes(context));
        IPacket[] packets = PacketConvert.DeserializeAll(stream, _facade);
        Assert.That(packets.OfType<PacketUserPresence>().Single().UserId, Is.EqualTo(2));
        DefaultHttpContext next = createContext([]);
        await execute(next);
        Assert.That(bytes(next), Is.Empty);
    }

    [Test]
    public async Task LogoutStopsDispatchingLaterPacketsInTheSameBody()
    {
        DefaultHttpContext context = createContext(PacketConvert.SerializeAll([
            new PacketLogout(), new TestPacket { Value = 99 }
        ]));
        await execute(context);
        Assert.That(_players.GetPlayer(_player.Token), Is.Null);
        Assert.That(bytes(context), Is.Empty);
    }

    [Test]
    public async Task SpectatingAndReplayFramesTravelThroughThePacketEndpoint()
    {
        Player host = new(2);
        _players.AddPlayer(host);
        _player.Dequeue();
        host.Dequeue();
        DefaultHttpContext start = createContext(PacketConvert.Serialize(new PacketStartSpectating { TargetId = 2 }));
        await execute(start);
        Assert.That(_players.GetSpectatingPlayer(_player), Is.SameAs(host));
        host.Dequeue();
        ReplayFrameBundle bundle = new() { Sequence = 42 };
        DefaultHttpContext frames = createContext(
            PacketConvert.Serialize(new g0v0.Server.Stable.Packets.Client.PacketSpectateFrames { FrameBundle = bundle }),
            host.Token);
        await execute(frames);
        DefaultHttpContext poll = createContext([]);
        await execute(poll);
        using MemoryStream stream = new(bytes(poll));
        g0v0.Server.Stable.Packets.Server.PacketSpectateFrames packet = (g0v0.Server.Stable.Packets.Server.PacketSpectateFrames)PacketConvert.DeserializeAll(stream, _facade).Single();
        Assert.That(packet.Data, Is.EqualTo(PacketConvert.SerializePayload(bundle)));
    }

    [Test]
    public async Task ChannelJoinThroughThePollerReturnsJoinSuccessAndTheChannelInfo()
    {
        DefaultHttpContext context = createContext(PacketConvert.Serialize(new PacketChannelJoin { Name = "#osu" }));
        await execute(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        IPacket[] packets = PacketConvert.DeserializeAll(new MemoryStream(bytes(context)), _facade);
        Assert.That(packets[0], Is.TypeOf<PacketChannelJoinSuccess>());
        Assert.That(((PacketChannelJoinSuccess)packets[0]).Name, Is.EqualTo("#osu"));
        Assert.That(packets[1], Is.TypeOf<PacketChannelInfo>());
    }

    [Test]
    public async Task AFailingHandlerDoesNotFailThePoll()
    {
        OsuPacketAttribute.Register(60002, typeof(ThrowingPacket));
        try
        {
            DefaultHttpContext context = createContext(PacketConvert.SerializeAll([
                new ThrowingPacket(), new TestPacket { Value = 7 }
            ]));
            await execute(context);

            Assert.That(context.Response.StatusCode, Is.EqualTo(200));
            Assert.That(bytes(context), Is.EqualTo(PacketConvert.Serialize(new PacketNotification { Message = "session-token:7" })));
        }
        finally
        {
            OsuPacketAttribute.Unregister(60002);
        }
    }

    private static byte[] bytes(DefaultHttpContext context) => ((MemoryStream)context.Response.Body).ToArray();

    private DefaultHttpContext createContext(byte[] body, string? token = "session-token")
    {
        DefaultHttpContext context = new() { RequestServices = _services };
        context.Request.Headers.UserAgent = "osu!";
        if (token != null)
        {
            context.Request.Headers["osu-token"] = token;
        }

        context.Request.Body = new MemoryStream(body);
        context.Response.Body = new MemoryStream();
        return context;
    }

    private async Task execute(DefaultHttpContext context)
    {
        IResult result = await new PacketRouteHandler(_facade).HandleAsync(context);
        await result.ExecuteAsync(context);
    }

    [OsuPacket(60000)]
    public class TestPacket : IClientPacket
    {
        public int Value { get; set; }

        public Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
        {
            return Task.FromResult<IPacket[]>([new PacketNotification { Message = $"{facade.PlayerManager.GetPlayer(1)!.Token}:{Value}" }]);
        }
    }

    [OsuPacket(60001, allowRestrict: true)]
    public class AllowedPacket : TestPacket;

    [OsuPacket(60002)]
    public class ThrowingPacket : IClientPacket
    {
        public Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
        {
            throw new NotImplementedException();
        }
    }
}
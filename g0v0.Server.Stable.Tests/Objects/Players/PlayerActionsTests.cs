// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Rulesets;
using g0v0.Server.Stable.Manager;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Objects.States;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Client;
using g0v0.Server.Stable.Packets.Server;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using osu.Game.Beatmaps.Legacy;
using osu.Game.IO.Legacy;
using ClientFrames = g0v0.Server.Stable.Packets.Client.PacketSpectateFrames;
using ServerFrames = g0v0.Server.Stable.Packets.Server.PacketSpectateFrames;

namespace g0v0.Server.Stable.Tests.Objects.Players;

public class PlayerActionsTests
{
    private PlayerManager _manager = null!;
    private PacketFacade _facade = null!;

    [SetUp]
    public void SetUp()
    {
        _manager = new PlayerManager();
        _facade = new PacketFacade
        {
            PlayerManager = _manager,
            RulesetManager = new RulesetManager(Path.Combine(Path.GetTempPath(), "stable-action-rulesets"))
        };
    }

    [Test]
    public async Task OnlineAnnouncesBothSidesAndOfflineOnlyRemovesThisSession()
    {
        Player first = new(1, _manager) { Name = "first" };
        Player second = new(2, _manager) { Name = "second" };
        await first.Online();
        await second.Online();
        Assert.That(Read(first).Select(packet => packet.GetType()), Is.EqualTo([typeof(PacketUserPresence), typeof(PacketUserStats)]));
        Assert.That(Read(second).OfType<PacketUserPresence>().Single().UserId, Is.EqualTo(1));
        await first.Offline();
        Assert.That(first.IsOnline, Is.False);
        Assert.That(Read(second).OfType<PacketUserLogout>().Single().UserId, Is.EqualTo(1));
        await first.Offline();
        Assert.That(Read(second), Is.Empty);
    }

    [Test]
    public async Task ActivityBroadcastCarriesOriginalModsAndSnapshotsTheState()
    {
        Player host = Add(1);
        Player other = Add(2);
        Drain();
        Activity activity = new() { InfoText = "original", MapId = 123, Mode = 1 };
        await host.ChangePlayerActivityAsync(activity, LegacyMods.Hidden);
        activity.InfoText = "modified later";
        PacketUserStats packet = Read(other).OfType<PacketUserStats>().Single();
        Assert.That(packet.InfoText, Is.EqualTo("original"));
        Assert.That(packet.Mods, Is.EqualTo(LegacyMods.Hidden));
        Assert.That(packet.MapId, Is.EqualTo(123));
        Assert.That(Read(host).OfType<PacketUserStats>(), Has.Exactly(1).Items);
    }

    [Test]
    public async Task RestrictedActivityDoesNotLeakToOtherClients()
    {
        Player hidden = Add(1);
        Player other = Add(2);
        hidden.IsRestricted = true;
        Drain();
        await hidden.ChangePlayerActivityAsync(new Activity { InfoText = "private" });
        Assert.That(Read(other), Is.Empty);
        Assert.That(Read(hidden).OfType<PacketUserStats>().Single().InfoText, Is.EqualTo("private"));
    }

    [Test]
    public async Task SpectatorJoinsAreIdempotentAndNotifyTheHostAndFellows()
    {
        Player host = Add(1);
        Player first = Add(2);
        Player second = Add(3);
        Drain();
        await first.WatchPlayer(host);
        Assert.That(Read(host).OfType<PacketSpectatorJoined>().Single().UserId, Is.EqualTo(first.Id));
        await second.WatchPlayer(host);
        Assert.That(Read(host).OfType<PacketSpectatorJoined>().Single().UserId, Is.EqualTo(second.Id));
        Assert.That(Read(first).OfType<PacketFellowSpectatorJoined>().Single().UserId, Is.EqualTo(second.Id));
        Assert.That(Read(second).OfType<PacketFellowSpectatorJoined>().Single().UserId, Is.EqualTo(first.Id));
        await second.WatchPlayer(host);
        Assert.That(Read(host), Is.Empty);
        await second.StopWatchPlayer(host);
        Assert.That(Read(host).OfType<PacketSpectatorLeft>().Single().UserId, Is.EqualTo(second.Id));
        Assert.That(Read(first).OfType<PacketFellowSpectatorLeft>().Single().UserId, Is.EqualTo(second.Id));
    }

    [Test]
    public async Task SwitchingSpectatorTargetsDetachesThePreviousHost()
    {
        Player first = Add(1);
        Player second = Add(2);
        Player watcher = Add(3);
        await watcher.WatchPlayer(first);
        Drain();
        await watcher.WatchPlayer(second);
        Assert.That(Read(first).OfType<PacketSpectatorLeft>().Single().UserId, Is.EqualTo(watcher.Id));
        Assert.That(Read(second).OfType<PacketSpectatorJoined>().Single().UserId, Is.EqualTo(watcher.Id));
        await watcher.StopWatchPlayer(first);
        Assert.That(_manager.GetSpectatingPlayer(watcher), Is.SameAs(second));
    }

    [Test]
    public async Task FramesOnlyReachCurrentSpectatorsAndKeepTheExactPayload()
    {
        Player host = Add(1);
        Player watcher = Add(2);
        Player unrelated = Add(3);
        await watcher.WatchPlayer(host);
        Drain();
        ReplayFrameBundle bundle = new() { Extra = 123, Sequence = 42 };
        await new ClientFrames { FrameBundle = bundle }.Handle(host, _facade);
        ServerFrames packet = Read(watcher).OfType<ServerFrames>().Single();
        Assert.That(packet.Data, Is.EqualTo(PacketConvert.SerializePayload(bundle)));
        Assert.That(Read(unrelated), Is.Empty);
        Assert.That(Read(host), Is.Empty);
        await watcher.StopWatchPlayer();
        Drain();
        await host.SendFrames(bundle);
        Assert.That(Read(watcher), Is.Empty);
    }

    [Test]
    public async Task CantSpectateOnlyNotifiesTheCurrentHostAndFellows()
    {
        Player host = Add(1);
        Player first = Add(2);
        Player second = Add(3);
        Player unrelated = Add(4);
        await first.WatchPlayer(host);
        await second.WatchPlayer(host);
        Drain();
        await new PacketCantSpectate().Handle(first, _facade);
        Assert.That(Read(host).OfType<PacketSpectatorCantSpectate>().Single().UserId, Is.EqualTo(first.Id));
        Assert.That(Read(second).OfType<PacketSpectatorCantSpectate>().Single().UserId, Is.EqualTo(first.Id));
        Assert.That(Read(first), Is.Empty);
        Assert.That(Read(unrelated), Is.Empty);
    }

    [Test]
    public async Task HostOfflineClearsAllSpectatorRelationships()
    {
        Player host = Add(1);
        Player first = Add(2);
        Player second = Add(3);
        await first.WatchPlayer(host);
        await second.WatchPlayer(host);
        Drain();
        await host.Offline();
        Assert.That(_manager.GetSpectatingPlayer(first), Is.Null);
        Assert.That(_manager.GetSpectatingPlayer(second), Is.Null);
        Assert.That(_manager.GetWatchingPlayers(host), Is.Empty);
        Assert.That(Read(first).OfType<PacketUserLogout>().Single().UserId, Is.EqualTo(host.Id));
    }

    [Test]
    public async Task ReplacementReconnectsTheOldSessionAndLateLogoutCannotRemoveTheNewOne()
    {
        Player previous = Add(1);
        Player other = Add(2);
        Drain();
        Player next = new(1, _manager) { Name = "replacement" };
        await next.Online();
        Assert.That(Read(previous).OfType<PacketRestart>().Single().Milliseconds, Is.Zero);
        await previous.Offline();
        Assert.That(next.IsOnline, Is.True);
        Assert.That(Read(other).OfType<PacketUserLogout>(), Is.Empty);
    }

    [Test]
    public async Task RemovingOneTournamentSessionDoesNotAnnounceTheAccountOffline()
    {
        Player ordinary = Add(1);
        Player other = Add(2);
        Player tournament = new(1, _manager, isTourneyClient: true);
        await tournament.Online();
        Drain();
        await tournament.Offline();
        Assert.That(ordinary.IsOnline, Is.True);
        Assert.That(Read(other).OfType<PacketUserLogout>(), Is.Empty);
    }

    [Test]
    public void ConcurrentQueueingDrainsEveryPacketExactlyOnce()
    {
        Player player = new(1);
        Parallel.For(0, 1000, id => player.Enqueue(new PacketUserLogout { UserId = id }));
        PacketUserLogout[] packets = Read(player).OfType<PacketUserLogout>().ToArray();
        Assert.That(packets, Has.Length.EqualTo(1000));
        Assert.That(packets.Select(packet => packet.UserId).Distinct().Count(), Is.EqualTo(1000));
        Assert.That(Read(player), Is.Empty);
    }

    [Test]
    public async Task ActionHandlerLoadsStatisticsBeforeBroadcastingAndConvertsAccuracyToAFraction()
    {
        Player source = Add(1);
        Player other = Add(2);
        Drain();
        Mock<IUserStatisticsRepository> repository = new();
        repository.Setup(value => value.GetByUserIdAndModeAsync(1, 1)).ReturnsAsync(new UserStatistics
        {
            UserId = 1,
            Mode = 1,
            RankedScore = 1234,
            HitAccuracy = 98.25,
            Pp = 200,
            PlayCount = 42
        });
        repository.Setup(value => value.GetGlobalRankAsync(1, 1)).ReturnsAsync(7);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(repository.Object).BuildServiceProvider();
        _facade.ScopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        await new PacketChangeAction { Activity = new Activity { Mode = 1, MapId = 123 } }.Handle(source, _facade);
        PacketUserStats packet = Read(other).OfType<PacketUserStats>().Single();
        Assert.That(packet.Accuracy, Is.EqualTo(0.9825f).Within(0.00001));
        Assert.That(packet.RankedScore, Is.EqualTo(1234));
        Assert.That(packet.Pp, Is.EqualTo(200));
        Assert.That(packet.Rank, Is.EqualTo(7));
        Assert.That(packet.Mode, Is.EqualTo(1));
    }

    [Test]
    public async Task StatusRequestsHandleMissingStatisticsForNewAccounts()
    {
        Player source = Add(1);
        Mock<IUserStatisticsRepository> repository = new();
        using ServiceProvider provider = new ServiceCollection().AddSingleton(repository.Object).BuildServiceProvider();
        _facade.ScopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        IPacket[] packets = await new PacketRequestStatusUpdate().Handle(source, _facade);
        Assert.That(((PacketUserStats)packets.Single()).RankedScore, Is.Zero);
    }

    [Test]
    public async Task ChangeActionReaderPreservesTheOriginalWireModFlags()
    {
        Player source = Add(1);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(Mock.Of<IUserStatisticsRepository>()).BuildServiceProvider();
        _facade.ScopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        using MemoryStream payload = new();
        LegacyMods mods = LegacyMods.Nightcore | LegacyMods.DoubleTime;
        using (SerializationWriter writer = new(payload, leaveOpen: true))
        {
            writer.Write((byte)UserAction.Playing);
            writer.Write("playing");
            writer.Write(string.Empty);
            writer.Write((uint)mods);
            writer.Write((byte)0);
            writer.Write(123);
        }

        payload.Position = 0;
        using SerializationReader reader = new(payload);
        PacketChangeAction packet = PacketChangeAction.Read(reader, _facade);
        await packet.Handle(source, _facade);
        Assert.That(Read(source).OfType<PacketUserStats>().Single().Mods, Is.EqualTo(mods));
    }

    [Test]
    public async Task PresenceAndStatsRequestsFilterHiddenOfflineAndDuplicateSessions()
    {
        Player source = Add(1);
        Player visible = Add(2);
        Player hidden = new(3, _manager) { IsRestricted = true };
        await hidden.Online();
        Player tournament = new(2, _manager, isTourneyClient: true);
        await tournament.Online();
        IPacket[] presences = await new PacketUserPresenceRequestAll().Handle(source, _facade);
        Assert.That(presences.OfType<PacketUserPresence>().Select(packet => packet.UserId), Is.EquivalentTo([1, 2]));
        IPacket[] stats = await new PacketUserStatsRequest { UserIds = [2, 2, 3, 999] }.Handle(source, _facade);
        Assert.That(stats.OfType<PacketUserStats>().Select(packet => packet.UserId), Is.EqualTo([2]));
        IPacket[] selected = await new PacketUserPresenceRequest { UserIds = [2, 3, 999] }.Handle(source, _facade);
        Assert.That(selected.OfType<PacketUserPresence>().Single().Name, Is.EqualTo(visible.Name));
    }

    private static IPacket[] Read(Player player)
    {
        using MemoryStream stream = new(player.Dequeue());
        return PacketConvert.DeserializeAll(stream, new PacketFacade());
    }

    private Player Add(int id)
    {
        Player player = new(id, _manager) { Name = $"player-{id}" };
        _manager.AddPlayer(player);
        return player;
    }

    private void Drain()
    {
        foreach (IPlayer player in _manager.GetAllPlayers())
        {
            player.Dequeue();
        }
    }
}
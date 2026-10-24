// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Manager;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Tests.Manager;

public class PlayerManagerTests
{
    private PlayerManager manager = null!;

    [SetUp]
    public void SetUp()
    {
        manager = new PlayerManager();
    }

    [Test]
    public void FindsPlayersByIdAndCaseSensitiveToken()
    {
        Player player = new(1, "Token");
        manager.AddPlayer(player);
        Assert.Multiple(() =>
        {
            Assert.That(manager.GetPlayer(1), Is.SameAs(player));
            Assert.That(manager.GetPlayer("Token"), Is.SameAs(player));
            Assert.That(manager.GetPlayer("token"), Is.Null);
            Assert.That(manager.GetPlayer(2), Is.Null);
        });
    }

    [Test]
    public void NormalReconnectReplacesIndexesAndClearsBothSpectatorDirections()
    {
        Player previous = new(1, "old");
        Player observer = new(2);
        Player host = new(3);
        Player replacement = new(1, "new");
        manager.AddPlayer(previous);
        manager.AddPlayer(observer);
        manager.AddPlayer(host);
        manager.UserWatchingPlayer(observer, previous);
        manager.UserWatchingPlayer(previous, host);

        manager.AddPlayer(replacement);
        Assert.Multiple(() =>
        {
            Assert.That(manager.GetPlayer(1), Is.SameAs(replacement));
            Assert.That(manager.GetPlayer("old"), Is.Null);
            Assert.That(manager.GetPlayer("new"), Is.SameAs(replacement));
            Assert.That(manager.GetSpectatingPlayer(observer), Is.Null);
            Assert.That(manager.GetSpectatingPlayer(previous), Is.Null);
            Assert.That(manager.GetWatchingPlayers(host), Is.Empty);
            Assert.That(manager.GetWatchingPlayers(previous), Is.Empty);
            Assert.That(manager.RemovePlayer(previous), Is.False);
            Assert.That(manager.GetPlayer(1), Is.SameAs(replacement));
        });
    }

    [Test]
    public void TournamentSessionsCoexistAndIdLookupPrefersOrdinaryClient()
    {
        Player first = new(1, "tourney-1", isTourneyClient: true);
        Player second = new(1, "tourney-2", isTourneyClient: true);
        Player ordinary = new(1, "ordinary");
        manager.AddPlayer(first);
        manager.AddPlayer(second);
        manager.AddPlayer(ordinary);

        Assert.Multiple(() =>
        {
            Assert.That(manager.GetPlayer(1), Is.SameAs(ordinary));
            Assert.That(manager.GetPlayerAllInstances(1), Is.EquivalentTo(new IPlayer[] { first, second, ordinary }));
            Assert.That(manager.GetPlayer(first.Token), Is.SameAs(first));
            Assert.That(manager.GetPlayer(second.Token), Is.SameAs(second));
        });

        manager.RemovePlayer(ordinary);
        Assert.That(manager.GetPlayer(1), Is.AnyOf(first, second));
        manager.RemovePlayer(first);
        Assert.That(manager.GetPlayer(1), Is.SameAs(second));
        manager.RemovePlayer(second);
        Assert.That(manager.GetPlayerAllInstances(1), Is.Empty);
        Assert.That(manager.GetPlayer(1), Is.Null);
    }

    [Test]
    public void DuplicateTokenFailsBeforeReplacingAnySession()
    {
        Player first = new(1, "first");
        Player second = new(2, "second");
        manager.AddPlayer(first);
        manager.AddPlayer(second);
        Assert.Throws<InvalidOperationException>(() => manager.AddPlayer(new Player(1, second.Token)));
        Assert.Multiple(() =>
        {
            Assert.That(manager.GetPlayer(1), Is.SameAs(first));
            Assert.That(manager.GetPlayer("second"), Is.SameAs(second));
            Assert.That(manager.GetAllPlayers(), Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void ReaddingSameInstancePreservesSpectatorState()
    {
        Player watcher = new(1);
        Player host = new(2);
        manager.AddPlayer(watcher);
        manager.AddPlayer(host);
        manager.UserWatchingPlayer(watcher, host);
        manager.AddPlayer(watcher);
        Assert.That(manager.GetSpectatingPlayer(watcher), Is.SameAs(host));
        Assert.That(manager.GetAllPlayers(), Has.Count.EqualTo(2));
    }

    [Test]
    public void SwitchingTargetAndRepeatedStartsKeepBothDirectionsConsistent()
    {
        Player watcher = new(1);
        Player first = new(2);
        Player second = new(3);
        manager.AddPlayer(watcher);
        manager.AddPlayer(first);
        manager.AddPlayer(second);
        manager.UserWatchingPlayer(watcher, first);
        manager.UserWatchingPlayer(watcher, first);
        Assert.That(manager.GetWatchingPlayers(first), Has.Count.EqualTo(1));

        manager.UserWatchingPlayer(watcher, second);
        Assert.Multiple(() =>
        {
            Assert.That(manager.GetWatchingPlayers(first), Is.Empty);
            Assert.That(manager.GetWatchingPlayers(second), Has.Count.EqualTo(1));
            Assert.That(manager.GetWatchingPlayers(second)[0], Is.SameAs(watcher));
            Assert.That(manager.GetSpectatingPlayer(watcher), Is.SameAs(second));
        });

        Assert.That(manager.UserStoppedWatchingPlayer(watcher), Is.True);
        Assert.That(manager.UserStoppedWatchingPlayer(watcher), Is.False);
        Assert.That(manager.GetWatchingPlayers(second), Is.Empty);
        Assert.That(manager.GetSpectatingPlayer(watcher), Is.Null);
    }

    [Test]
    public void RemovingWatcherPreservesOtherSpectatorsAndRemovingHostClearsThem()
    {
        Player host = new(1);
        Player first = new(2);
        Player second = new(3);
        manager.AddPlayer(host);
        manager.AddPlayer(first);
        manager.AddPlayer(second);
        manager.UserWatchingPlayer(first, host);
        manager.UserWatchingPlayer(second, host);
        manager.RemovePlayer(first);
        Assert.That(manager.GetSpectatingPlayer(first), Is.Null);
        Assert.That(manager.GetWatchingPlayers(host), Has.Count.EqualTo(1));
        Assert.That(manager.GetWatchingPlayers(host)[0], Is.SameAs(second));

        manager.RemovePlayer(host);
        Assert.That(manager.GetSpectatingPlayer(second), Is.Null);
        Assert.That(manager.GetWatchingPlayers(host), Is.Empty);
    }

    [Test]
    public void RejectsSelfSpectatingAndUnregisteredOrReplacedSessions()
    {
        Player source = new(1);
        Player target = new(2);
        manager.AddPlayer(source);
        Assert.Throws<InvalidOperationException>(() => manager.UserWatchingPlayer(source, source));
        Assert.Throws<InvalidOperationException>(() => manager.UserWatchingPlayer(source, target));
        manager.AddPlayer(target);
        manager.AddPlayer(new Player(source.Id));
        Assert.Throws<InvalidOperationException>(() => manager.UserWatchingPlayer(source, target));
        Assert.That(manager.GetWatchingPlayers(target), Is.Empty);
    }

    [Test]
    public void QueriesReturnSnapshotsUnaffectedByLaterChanges()
    {
        Player first = new(1);
        Player host = new(2);
        manager.AddPlayer(first);
        manager.AddPlayer(host);
        manager.UserWatchingPlayer(first, host);
        IReadOnlyList<IPlayer> players = manager.GetAllPlayers();
        IReadOnlyList<IPlayer> sessions = manager.GetPlayerAllInstances(first.Id);
        IReadOnlyList<IPlayer> watchers = manager.GetWatchingPlayers(host);
        manager.RemovePlayer(first);
        Assert.Multiple(() =>
        {
            Assert.That(players, Has.Count.EqualTo(2));
            Assert.That(sessions, Has.Count.EqualTo(1));
            Assert.That(watchers, Has.Count.EqualTo(1));
            Assert.That(manager.GetPlayerAllInstances(first.Id), Is.Empty);
            Assert.That(manager.GetWatchingPlayers(host), Is.Empty);
        });
    }

    [Test]
    public void ConcurrentSessionChangesKeepBothIndexesConsistent()
    {
        Parallel.For(0, 64, i => manager.AddPlayer(new Player((i % 8) + 1, $"session-{i}", isTourneyClient: true)));
        IReadOnlyList<IPlayer> players = manager.GetAllPlayers();
        Assert.That(players, Has.Count.EqualTo(64));
        foreach (IPlayer player in players)
        {
            Assert.That(manager.GetPlayer(player.Token), Is.SameAs(player));
            Assert.That(manager.GetPlayerAllInstances(player.Id), Has.Count.EqualTo(8));
        }

        Parallel.ForEach(players, player => manager.RemovePlayer(player));
        Assert.That(manager.GetAllPlayers(), Is.Empty);
        Assert.That(Enumerable.Range(1, 8).Select(manager.GetPlayer), Is.All.Null);
    }
}
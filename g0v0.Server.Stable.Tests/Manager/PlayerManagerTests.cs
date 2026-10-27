// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Manager;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Tests.Manager;

public class PlayerManagerTests
{
    private PlayerManager _manager = null!;

    [SetUp]
    public void SetUp()
    {
        _manager = new PlayerManager();
    }

    [Test]
    public void FindsPlayersByIdAndCaseSensitiveToken()
    {
        Player player = new(1, "Token");
        _manager.AddPlayer(player);
        Assert.Multiple(() =>
        {
            Assert.That(_manager.GetPlayer(1), Is.SameAs(player));
            Assert.That(_manager.GetPlayer("Token"), Is.SameAs(player));
            Assert.That(_manager.GetPlayer("token"), Is.Null);
            Assert.That(_manager.GetPlayer(2), Is.Null);
        });
    }

    [Test]
    public void NormalReconnectReplacesIndexesAndClearsBothSpectatorDirections()
    {
        Player previous = new(1, "old");
        Player observer = new(2);
        Player host = new(3);
        Player replacement = new(1, "new");
        _manager.AddPlayer(previous);
        _manager.AddPlayer(observer);
        _manager.AddPlayer(host);
        _manager.UserWatchingPlayer(observer, previous);
        _manager.UserWatchingPlayer(previous, host);

        _manager.AddPlayer(replacement);
        Assert.Multiple(() =>
        {
            Assert.That(_manager.GetPlayer(1), Is.SameAs(replacement));
            Assert.That(_manager.GetPlayer("old"), Is.Null);
            Assert.That(_manager.GetPlayer("new"), Is.SameAs(replacement));
            Assert.That(_manager.GetSpectatingPlayer(observer), Is.Null);
            Assert.That(_manager.GetSpectatingPlayer(previous), Is.Null);
            Assert.That(_manager.GetWatchingPlayers(host), Is.Empty);
            Assert.That(_manager.GetWatchingPlayers(previous), Is.Empty);
            Assert.That(_manager.RemovePlayer(previous), Is.False);
            Assert.That(_manager.GetPlayer(1), Is.SameAs(replacement));
        });
    }

    [Test]
    public void TournamentSessionsCoexistAndIdLookupPrefersOrdinaryClient()
    {
        Player first = new(1, "tourney-1", isTourneyClient: true);
        Player second = new(1, "tourney-2", isTourneyClient: true);
        Player ordinary = new(1, "ordinary");
        _manager.AddPlayer(first);
        _manager.AddPlayer(second);
        _manager.AddPlayer(ordinary);

        Assert.Multiple(() =>
        {
            Assert.That(_manager.GetPlayer(1), Is.SameAs(ordinary));
            Assert.That(_manager.GetPlayerAllInstances(1), Is.EquivalentTo(new IPlayer[] { first, second, ordinary }));
            Assert.That(_manager.GetPlayer(first.Token), Is.SameAs(first));
            Assert.That(_manager.GetPlayer(second.Token), Is.SameAs(second));
        });

        _manager.RemovePlayer(ordinary);
        Assert.That(_manager.GetPlayer(1), Is.AnyOf(first, second));
        _manager.RemovePlayer(first);
        Assert.That(_manager.GetPlayer(1), Is.SameAs(second));
        _manager.RemovePlayer(second);
        Assert.That(_manager.GetPlayerAllInstances(1), Is.Empty);
        Assert.That(_manager.GetPlayer(1), Is.Null);
    }

    [Test]
    public void DuplicateTokenFailsBeforeReplacingAnySession()
    {
        Player first = new(1, "first");
        Player second = new(2, "second");
        _manager.AddPlayer(first);
        _manager.AddPlayer(second);
        Assert.Throws<InvalidOperationException>(() => _manager.AddPlayer(new Player(1, second.Token)));
        Assert.Multiple(() =>
        {
            Assert.That(_manager.GetPlayer(1), Is.SameAs(first));
            Assert.That(_manager.GetPlayer("second"), Is.SameAs(second));
            Assert.That(_manager.GetAllPlayers(), Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void ReaddingSameInstancePreservesSpectatorState()
    {
        Player watcher = new(1);
        Player host = new(2);
        _manager.AddPlayer(watcher);
        _manager.AddPlayer(host);
        _manager.UserWatchingPlayer(watcher, host);
        _manager.AddPlayer(watcher);
        Assert.That(_manager.GetSpectatingPlayer(watcher), Is.SameAs(host));
        Assert.That(_manager.GetAllPlayers(), Has.Count.EqualTo(2));
    }

    [Test]
    public void SwitchingTargetAndRepeatedStartsKeepBothDirectionsConsistent()
    {
        Player watcher = new(1);
        Player first = new(2);
        Player second = new(3);
        _manager.AddPlayer(watcher);
        _manager.AddPlayer(first);
        _manager.AddPlayer(second);
        _manager.UserWatchingPlayer(watcher, first);
        _manager.UserWatchingPlayer(watcher, first);
        Assert.That(_manager.GetWatchingPlayers(first), Has.Count.EqualTo(1));

        _manager.UserWatchingPlayer(watcher, second);
        Assert.Multiple(() =>
        {
            Assert.That(_manager.GetWatchingPlayers(first), Is.Empty);
            Assert.That(_manager.GetWatchingPlayers(second), Has.Count.EqualTo(1));
            Assert.That(_manager.GetWatchingPlayers(second)[0], Is.SameAs(watcher));
            Assert.That(_manager.GetSpectatingPlayer(watcher), Is.SameAs(second));
        });

        Assert.That(_manager.UserStoppedWatchingPlayer(watcher), Is.True);
        Assert.That(_manager.UserStoppedWatchingPlayer(watcher), Is.False);
        Assert.That(_manager.GetWatchingPlayers(second), Is.Empty);
        Assert.That(_manager.GetSpectatingPlayer(watcher), Is.Null);
    }

    [Test]
    public void RemovingWatcherPreservesOtherSpectatorsAndRemovingHostClearsThem()
    {
        Player host = new(1);
        Player first = new(2);
        Player second = new(3);
        _manager.AddPlayer(host);
        _manager.AddPlayer(first);
        _manager.AddPlayer(second);
        _manager.UserWatchingPlayer(first, host);
        _manager.UserWatchingPlayer(second, host);
        _manager.RemovePlayer(first);
        Assert.That(_manager.GetSpectatingPlayer(first), Is.Null);
        Assert.That(_manager.GetWatchingPlayers(host), Has.Count.EqualTo(1));
        Assert.That(_manager.GetWatchingPlayers(host)[0], Is.SameAs(second));

        _manager.RemovePlayer(host);
        Assert.That(_manager.GetSpectatingPlayer(second), Is.Null);
        Assert.That(_manager.GetWatchingPlayers(host), Is.Empty);
    }

    [Test]
    public void RejectsSelfSpectatingAndUnregisteredOrReplacedSessions()
    {
        Player source = new(1);
        Player target = new(2);
        _manager.AddPlayer(source);
        Assert.Throws<InvalidOperationException>(() => _manager.UserWatchingPlayer(source, source));
        Assert.Throws<InvalidOperationException>(() => _manager.UserWatchingPlayer(source, target));
        _manager.AddPlayer(target);
        _manager.AddPlayer(new Player(source.Id));
        Assert.Throws<InvalidOperationException>(() => _manager.UserWatchingPlayer(source, target));
        Assert.That(_manager.GetWatchingPlayers(target), Is.Empty);
    }

    [Test]
    public void QueriesReturnSnapshotsUnaffectedByLaterChanges()
    {
        Player first = new(1);
        Player host = new(2);
        _manager.AddPlayer(first);
        _manager.AddPlayer(host);
        _manager.UserWatchingPlayer(first, host);
        IReadOnlyList<IPlayer> players = _manager.GetAllPlayers();
        IReadOnlyList<IPlayer> sessions = _manager.GetPlayerAllInstances(first.Id);
        IReadOnlyList<IPlayer> watchers = _manager.GetWatchingPlayers(host);
        _manager.RemovePlayer(first);
        Assert.Multiple(() =>
        {
            Assert.That(players, Has.Count.EqualTo(2));
            Assert.That(sessions, Has.Count.EqualTo(1));
            Assert.That(watchers, Has.Count.EqualTo(1));
            Assert.That(_manager.GetPlayerAllInstances(first.Id), Is.Empty);
            Assert.That(_manager.GetWatchingPlayers(host), Is.Empty);
        });
    }

    [Test]
    public void ConcurrentSessionChangesKeepBothIndexesConsistent()
    {
        Parallel.For(0, 64, i => _manager.AddPlayer(new Player((i % 8) + 1, $"session-{i}", isTourneyClient: true)));
        IReadOnlyList<IPlayer> players = _manager.GetAllPlayers();
        Assert.That(players, Has.Count.EqualTo(64));
        foreach (IPlayer player in players)
        {
            Assert.That(_manager.GetPlayer(player.Token), Is.SameAs(player));
            Assert.That(_manager.GetPlayerAllInstances(player.Id), Has.Count.EqualTo(8));
        }

        Parallel.ForEach(players, player => _manager.RemovePlayer(player));
        Assert.That(_manager.GetAllPlayers(), Is.Empty);
        Assert.That(Enumerable.Range(1, 8).Select(_manager.GetPlayer), Is.All.Null);
    }
}
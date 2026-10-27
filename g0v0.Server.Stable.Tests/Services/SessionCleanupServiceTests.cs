// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Stable.Manager;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace g0v0.Server.Stable.Tests.Services;

public class SessionCleanupServiceTests
{
    private string _configPath = null!;
    private PlayerManager _players = null!;
    private ServiceProvider _services = null!;
    private SessionCleanupService _cleanup = null!;
    private Mock<IChatServer> _chats = null!;
    private Mock<IUserRepository> _users = null!;
    private ManualTimeProvider _clock = null!;

    [SetUp]
    public void SetUp()
    {
        _configPath = Path.Combine(Path.GetTempPath(), "stable-session-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(_configPath, "config"));
        _players = new PlayerManager();
        _chats = new Mock<IChatServer>();
        _users = new Mock<IUserRepository>();
        _users.Setup(repository => repository.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => new User { Id = id, Username = $"user{id}", IsOnline = true });
        _services = new ServiceCollection().AddSingleton(_users.Object).BuildServiceProvider();
        _clock = new ManualTimeProvider();
        _cleanup = new SessionCleanupService(_players, _chats.Object, _services.GetRequiredService<IServiceScopeFactory>(), new ConfigManager(_configPath), _clock, NullLogger<SessionCleanupService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _cleanup.Dispose();
        _services.Dispose();
        Directory.Delete(_configPath, recursive: true);
    }

    [Test]
    public async Task ExpiresOnlyIdleSessionsAndClearsTheirSpectatorGraphAndOnlineState()
    {
        Player expired = Add(1, 91);
        Player active = Add(2, 1);
        _players.UserWatchingPlayer(active, expired);
        Assert.That(await _cleanup.SweepAsync(), Is.EqualTo(1));
        Assert.That(_players.GetPlayer(expired.Token), Is.Null);
        Assert.That(_players.GetPlayer(active.Token), Is.SameAs(active));
        Assert.That(_players.GetSpectatingPlayer(active), Is.Null);
        _chats.Verify(chats => chats.DisconnectAsync(expired), Times.Once);
        _users.Verify(users => users.UpdateAsync(It.Is<User>(user => user.Id == 1 && !user.IsOnline && user.LastVisit == _clock.UtcNow)), Times.Once);
    }

    [Test]
    public async Task KeepsAccountOnlineUntilItsLastTournamentSessionExpires()
    {
        Player first = Add(1, 91, tournament: true);
        Player second = Add(1, 0, tournament: true);
        Assert.That(await _cleanup.SweepAsync(), Is.EqualTo(1));
        _users.Verify(repository => repository.UpdateAsync(It.IsAny<User>()), Times.Never);
        Assert.That(_players.GetPlayerAllInstances(1), Is.EqualTo((Player[])[second]));
        _clock.UtcNow = _clock.UtcNow.AddSeconds(91);
        Assert.That(await _cleanup.SweepAsync(), Is.EqualTo(1));
        _users.Verify(repository => repository.UpdateAsync(It.Is<User>(user => user.Id == first.Id && !user.IsOnline)), Times.Once);
    }

    [Test]
    public void RefreshAndReplacementInvalidateAnOldExpirySnapshot()
    {
        Player old = Add(1, 91);
        Assert.That(_players.RefreshSession(old, _clock.UtcNow), Is.True);
        Assert.That(_players.RemoveInactivePlayer(old, _clock.UtcNow.AddSeconds(-90)), Is.False);
        Player replacement = Add(1, 91);
        Assert.That(_players.RemoveInactivePlayer(old, _clock.UtcNow), Is.False);
        Assert.That(_players.RefreshSession(old, _clock.UtcNow), Is.False);
        Assert.That(_players.GetPlayer(replacement.Token), Is.SameAs(replacement));
    }

    [Test]
    public async Task ReconnectDuringUserLookupKeepsTheAccountOnline()
    {
        Add(1, 91);
        User account = new() { Id = 1, Username = "user1", IsOnline = true };
        _users.Setup(repository => repository.GetByIdAsync(1)).ReturnsAsync(() =>
        {
            Add(1, 0);
            return account;
        });
        Assert.That(await _cleanup.SweepAsync(), Is.EqualTo(1));
        Assert.That(account.IsOnline, Is.True);
        _users.Verify(repository => repository.UpdateAsync(It.IsAny<User>()), Times.Never);
    }

    [Test]
    public async Task CleanupFailureForOneAccountDoesNotPreventOtherExpirations()
    {
        Add(1, 91);
        Add(2, 91);
        _users.Setup(repository => repository.GetByIdAsync(1)).ThrowsAsync(new InvalidOperationException("database unavailable"));
        Assert.That(await _cleanup.SweepAsync(), Is.EqualTo(2));
        Assert.That(_players.GetAllPlayers(), Is.Empty);
        _users.Verify(repository => repository.UpdateAsync(It.Is<User>(user => user.Id == 2 && !user.IsOnline)), Times.Once);
    }

    private Player Add(int id, int ageSeconds, bool tournament = false)
    {
        Player player = new(id, _players, tournament) { Name = $"user{id}", LastReceiveTime = _clock.UtcNow.AddSeconds(-ageSeconds) };
        _players.AddPlayer(player);
        return player;
    }
}
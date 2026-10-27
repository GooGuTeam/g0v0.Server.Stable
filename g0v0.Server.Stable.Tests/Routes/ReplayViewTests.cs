// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Stable.Routes;
using Microsoft.AspNetCore.Http;
using Moq;

namespace g0v0.Server.Stable.Tests.Routes;

public partial class LegacyWebRouteHandlerTests
{
    [TestCase(1, 0, 0)]
    [TestCase(2, 0, 1)]
    [TestCase(2, 4, 1)]
    [TestCase(2, 5, 1)]
    public async Task SuccessfulReplayViewsCountOnlyOtherUsersInTheStoredMode(int owner, int mode, int increments)
    {
        byte[] frames = [1, 2, 3];
        ConfigureReplay(CreateReplay(frames));
        _scores.Setup(repository => repository.GetByIdAsync(42)).ReturnsAsync(new Score { Id = 42, UserId = owner, Mode = mode, BeatmapId = 2, Type = "stable", HasReplay = true });
        DefaultHttpContext context = Authenticated("&c=42&m=0");
        await ExecuteAsync(new ReplayRouteHandler(_authenticator, CreateReplayService()), context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(((MemoryStream)context.Response.Body).ToArray(), Is.EqualTo(frames));
        _statistics.Verify(repository => repository.IncrementReplayViewsAsync(owner, mode, It.IsAny<CancellationToken>()), Times.Exactly(increments));
        _replayViewLocks.Verify(
            cache => cache.TrySetAsync("view:score_replay:42:1", "1", TimeSpan.FromDays(1)),
            Times.Exactly(increments));
        _statistics.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ReplayViewIsCountedOnlyOncePerViewerWithinOneDay()
    {
        ConfigureReplay(CreateReplay([1, 2, 3]));
        _scores.Setup(repository => repository.GetByIdAsync(42)).ReturnsAsync(new Score { Id = 42, UserId = 2, Mode = 0, BeatmapId = 2, Type = "stable", HasReplay = true });
        _replayViewLocks.Reset();
        _replayViewLocks
            .SetupSequence(cache => cache.TrySetAsync("view:score_replay:42:1", "1", TimeSpan.FromDays(1)))
            .ReturnsAsync(value: true)
            .ReturnsAsync(value: false);

        await ExecuteAsync(new ReplayRouteHandler(_authenticator, CreateReplayService()), Authenticated("&c=42&m=0"));
        await ExecuteAsync(new ReplayRouteHandler(_authenticator, CreateReplayService()), Authenticated("&c=42&m=0"));

        _replayViewLocks.Verify(
            cache => cache.TrySetAsync("view:score_replay:42:1", "1", TimeSpan.FromDays(1)),
            Times.Exactly(2));
        _statistics.Verify(repository => repository.IncrementReplayViewsAsync(2, 0, It.IsAny<CancellationToken>()), Times.Once);
        _statistics.VerifyNoOtherCalls();
    }

    [Test]
    public async Task FileRemovedBetweenExistenceCheckAndReadDoesNotCountAView()
    {
        ConfigureReplay(CreateReplay([1, 2, 3]));
        _storage.Setup(storage => storage.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new FileNotFoundException());
        DefaultHttpContext context = Authenticated("&c=42&m=0");
        await ExecuteAsync(new ReplayRouteHandler(_authenticator, CreateReplayService()), context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(404));
        _statistics.VerifyNoOtherCalls();
    }
}
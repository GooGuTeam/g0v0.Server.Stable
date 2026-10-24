// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Fetching;
using g0v0.Server.Common.Rulesets;
using g0v0.Server.Common.Scoring;
using g0v0.Server.Common.Utils;
using g0v0.Server.Stable.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

#pragma warning disable CA5351 // Test fixtures use the MD5 checksums required by the osu! protocol.

namespace g0v0.Server.Stable.Tests.Services;

public class ScoreProcessingQueueTests
{
    private string _configPath = null!;
    private ConfigManager _configs = null!;

    [SetUp]
    public void SetUp()
    {
        _configPath = Path.Combine(Path.GetTempPath(), "stable-queue-config-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(_configPath, "config"));
        File.WriteAllText(Path.Combine(_configPath, "config", "general.json"), "{}");
        File.WriteAllText(Path.Combine(_configPath, "config", "game.json"), "{\"EnableRelax\": true, \"EnableAutopilot\": true}");
        _configs = new ConfigManager(_configPath);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_configPath, recursive: true);

    [Test]
    public async Task IndependentClientInstancesUseTheirOwnTypeAndScopedCallback()
    {
        Mock<IScoreRepository> scores = new();
        Score stable = new() { Id = 1, Type = "stable" };
        Score lazer = new() { Id = 2, Type = "lazer" };
        scores.Setup(s => s.GetUnprocessedScoresAsync(0, 100, "stable")).ReturnsAsync([stable]);
        scores.Setup(s => s.GetUnprocessedScoresAsync(0, 100, "lazer")).ReturnsAsync([lazer]);
        scores.Setup(s => s.GetUnprocessedScoresAsync(1, 100, "stable")).ReturnsAsync([]);
        scores.Setup(s => s.GetUnprocessedScoresAsync(2, 100, "lazer")).ReturnsAsync([]);
        await using ServiceProvider services = new ServiceCollection().AddSingleton(scores.Object).BuildServiceProvider();
        TaskCompletionSource stableDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource lazerDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ScoreProcessingQueue stableQueue = new(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ScoreProcessingQueue>.Instance,
            "stable",
            (_, score, _) =>
            {
                Assert.That(score, Is.SameAs(stable));
                stableDone.TrySetResult();
                return Task.CompletedTask;
            });
        using ScoreProcessingQueue lazerQueue = new(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ScoreProcessingQueue>.Instance,
            "lazer",
            (_, score, _) =>
            {
                Assert.That(score, Is.SameAs(lazer));
                lazerDone.TrySetResult();
                return Task.CompletedTask;
            });
        await stableQueue.StartAsync(CancellationToken.None);
        await lazerQueue.StartAsync(CancellationToken.None);
        try
        {
            await Task.WhenAll(stableDone.Task, lazerDone.Task).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await stableQueue.StopAsync(CancellationToken.None);
            await lazerQueue.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task RecoversPersistedPendingScoreAtStartupAndRetriesAfterCalculationFailure()
    {
        string content = ScoreProcessingServiceTests.BeatmapText(0);
        string checksum = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
        RulesetManager rulesets = new("missing");
        Score score = StableScoreSubmission.Parse($"{checksum}:peppy:{new string('a', 32)}:4:0:0:0:0:0:1000000:4:True:D:0:True:0:241024120000:20261024")
            .ToScore(rulesets, 1, 2, "20261024", DateTimeOffset.UtcNow, 2000);
        score.Id = 1;
        score.HasReplay = true;
        Mock<IFetcher> fetcher = new();
        TaskCompletionSource firstAttempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        fetcher.Setup(f => f.FetchBeatmapRawAsync(2, It.IsAny<CancellationToken>())).Returns(() =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                firstAttempt.TrySetResult();
                throw new HttpRequestException("Temporary mirror outage");
            }

            return Task.FromResult(content);
        });
        Mock<IScoreRepository> scores = new();
        scores.Setup(s => s.GetUnprocessedScoresAsync(0, 100, "stable")).ReturnsAsync(() => completed.Task.IsCompleted ? [] : new[] { score });
        scores.Setup(s => s.GetUnprocessedScoresAsync(1, 100, "stable")).ReturnsAsync([]);
        scores.Setup(s => s.CompleteProcessingAsync(It.IsAny<Score>(), It.IsAny<int>())).Callback(() => completed.TrySetResult()).Returns(Task.CompletedTask);
        Mock<IBeatmapRepository> beatmaps = new();
        beatmaps.Setup(b => b.GetByIdAsync(2)).ReturnsAsync(new Beatmap { Id = 2, Status = osu.Game.Beatmaps.BeatmapOnlineStatus.Ranked });
        await using ServiceProvider services = new ServiceCollection()
            .AddSingleton(scores.Object).AddSingleton(fetcher.Object).AddSingleton(beatmaps.Object)
            .AddSingleton(rulesets).AddSingleton(_configs).AddSingleton<ScoreAttributeUtils>().AddScoped<ScoreProcessingService>().BuildServiceProvider();
        using ScoreProcessingQueue queue = new(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ScoreProcessingQueue>.Instance,
            "stable",
            (services, score, cancellationToken) => services.GetRequiredService<ScoreProcessingService>().ProcessAsync(score, cancellationToken),
            score => !score.Passed || score.HasReplay);
        await queue.StartAsync(CancellationToken.None);
        try
        {
            await firstAttempt.Task.WaitAsync(TimeSpan.FromSeconds(5));
            scores.Verify(s => s.CompleteProcessingAsync(It.IsAny<Score>(), It.IsAny<int>()), Times.Never);
            queue.NotifyPendingScore();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            scores.Verify(s => s.CompleteProcessingAsync(score, It.IsAny<int>()), Times.Once);
            Assert.That(attempts, Is.EqualTo(2));
        }
        finally
        {
            await queue.StopAsync(CancellationToken.None);
        }
    }
}
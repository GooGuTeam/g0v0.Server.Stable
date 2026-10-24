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
using Moq;
using osu.Game.Scoring;
using Score = g0v0.Server.Common.Database.Models.Score;

#pragma warning disable CA5351 // Test fixtures use the MD5 checksums required by the osu! protocol.

namespace g0v0.Server.Stable.Tests.Services;

public class ScoreProcessingServiceTests
{
    private string _configPath = null!;
    private ConfigManager _configs = null!;

    [SetUp]
    public void SetUp()
    {
        // Relax and autopilot only award performance while their feature is enabled.
        _configPath = Path.Combine(Path.GetTempPath(), "stable-processing-config-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(_configPath, "config"));
        File.WriteAllText(Path.Combine(_configPath, "config", "general.json"), "{}");
        File.WriteAllText(Path.Combine(_configPath, "config", "game.json"), "{\"EnableRelax\": true, \"EnableAutopilot\": true}");
        _configs = new ConfigManager(_configPath);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_configPath, recursive: true);

    [TestCase(0, true)]
    [TestCase(1, true)]
    [TestCase(2, true)]
    [TestCase(3, true)]
    [TestCase(4, true)]
    [TestCase(5, true)]
    [TestCase(6, true)]
    [TestCase(7, true)]
    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(3, false)]
    public async Task CalculatesFromOsuBeforeCompletingAndReplacesClientGrade(int mode, bool passed)
    {
        int baseMode = ScoreModeUtils.GetBaseRulesetId(mode);
        int legacyMods = mode == 5 ? 8192 : mode >= 4 ? 128 : 0;
        string content = BeatmapText(baseMode);
        string checksum = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
        RulesetManager rulesets = new("missing");
        string data = $"{checksum}:peppy:{new string('a', 32)}:4:0:0:0:0:0:1000000:4:True:D:{legacyMods}:{passed}:" + $"{baseMode}:241024120000:20261024";
        Score score = StableScoreSubmission.Parse(data).ToScore(rulesets, 1, 2, "20261024", DateTimeOffset.UtcNow, 2000);
        score.Mode = mode;
        Mock<IFetcher> fetcher = new();
        fetcher.Setup(f => f.FetchBeatmapRawAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(content);
        Mock<IBeatmapRepository> beatmaps = new();
        beatmaps.Setup(b => b.GetByIdAsync(2)).ReturnsAsync(new Beatmap { Id = 2, Status = osu.Game.Beatmaps.BeatmapOnlineStatus.Ranked });
        Mock<IScoreRepository> scores = new();
        scores.Setup(s => s.CompleteProcessingAsync(It.IsAny<Score>(), It.IsAny<int>())).Returns(Task.CompletedTask);
        ScoreProcessingService processor = new(fetcher.Object, beatmaps.Object, scores.Object, rulesets, new ScoreAttributeUtils(rulesets), _configs);
        await processor.ProcessAsync(score, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(score.Rank, Is.EqualTo(passed ? ScoreRank.X : ScoreRank.F));
            Assert.That(score.Accuracy, Is.EqualTo(mode == 3 ? 300.0 / 305 : 1).Within(0.000001));
            Assert.That(score.ClassicTotalScore, Is.EqualTo(1000000));
            Assert.That(score.ClassicTotalScoreWithoutMods, Is.EqualTo(mode >= 4 ? 0 : 1000000));
            Assert.That(score.MaximumStatistics.Values.Sum(), Is.GreaterThanOrEqualTo(4));
            Assert.That(score.Pp, passed ? Is.GreaterThan(0) : Is.Zero);
        });
        scores.Verify(s => s.CompleteProcessingAsync(score, It.IsAny<int>()), Times.Once);
    }

    [TestCase("stable", false)]
    [TestCase("stable", true)]
    [TestCase("lazer", false)]
    [TestCase("lazer", true)]
    public async Task UsesClassicTotalScoreToIdentifyLegacyScoresRegardlessOfType(string type, bool legacy)
    {
        string content = BeatmapText(0);
        string checksum = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
        RulesetManager rulesets = new("missing");
        Score score = StableScoreSubmission.Parse($"{checksum}:peppy:{new string('a', 32)}:4:0:0:0:0:0:12345:4:True:D:0:True:0:241024120000:20261024")
            .ToScore(rulesets, 1, 2, "20261024", DateTimeOffset.UtcNow, 2000);
        score.Type = type;
        score.ClassicTotalScore = legacy ? 12345 : 0;
        score.TotalScore = 500000;
        score.TotalScoreWithoutMods = 500000;
        Mock<IFetcher> fetcher = new();
        fetcher.Setup(f => f.FetchBeatmapRawAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(content);
        Mock<IBeatmapRepository> beatmaps = new();
        beatmaps.Setup(b => b.GetByIdAsync(2)).ReturnsAsync(new Beatmap { Id = 2 });
        Mock<IScoreRepository> scores = new();
        scores.Setup(s => s.CompleteProcessingAsync(It.IsAny<Score>(), It.IsAny<int>())).Returns(Task.CompletedTask);
        ScoreProcessingService service = new(fetcher.Object, beatmaps.Object, scores.Object, rulesets, new ScoreAttributeUtils(rulesets), _configs);
        await service.ProcessAsync(score, CancellationToken.None);
        Assert.That(score.TotalScoreWithoutMods, Is.EqualTo(legacy ? 1000000 : 500000));
        Assert.That(score.ClassicTotalScore, Is.EqualTo(legacy ? 12345 : 0));
        if (legacy)
        {
            Assert.That(score.TotalScore, Is.GreaterThan(score.ClassicTotalScore));
        }

        Assert.That(score.MaximumStatistics, Is.Not.Empty);
        scores.Verify(s => s.CompleteProcessingAsync(score, It.IsAny<int>()), Times.Once);
    }

    [Test]
    public void ChangedBeatmapLeavesScorePending()
    {
        Mock<IFetcher> fetcher = new();
        fetcher.Setup(f => f.FetchBeatmapRawAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(BeatmapText(0));
        Mock<IScoreRepository> scores = new();
        RulesetManager rulesets = new("missing");
        ScoreProcessingService processor = new(fetcher.Object, Mock.Of<IBeatmapRepository>(), scores.Object, rulesets, new ScoreAttributeUtils(rulesets), _configs);
        Score score = new() { BeatmapId = 2, BeatmapChecksum = new string('0', 32), Type = "stable" };
        Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(score, CancellationToken.None));
        scores.Verify(s => s.CompleteProcessingAsync(It.IsAny<Score>(), It.IsAny<int>()), Times.Never);
        Assert.That(score.Processed, Is.False);
    }

    internal static string BeatmapText(int mode) => $"""
        osu file format v14

        [General]
        AudioFilename: test.mp3
        Mode: {mode}

        [Metadata]
        Title:Test
        Artist:Test
        Creator:Test
        Version:Test

        [Difficulty]
        HPDrainRate:5
        CircleSize:4
        OverallDifficulty:5
        ApproachRate:5
        SliderMultiplier:1.4
        SliderTickRate:1

        [TimingPoints]
        0,500,4,2,1,100,1,0

        [HitObjects]
        64,192,1000,1,0,0:0:0:0:
        192,192,1500,1,0,0:0:0:0:
        320,192,2000,1,0,0:0:0:0:
        448,192,2500,1,0,0:0:0:0:
        """;
}
// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Net.Http.Headers;
using System.Text;
using g0v0.Server.Common.Achievements;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Fetching;
using g0v0.Server.Common.Rulesets;
using g0v0.Server.Common.Scoring;
using g0v0.Server.Common.Storage;
using g0v0.Server.Stable.Manager;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Routes;
using g0v0.Server.Stable.Services;
using g0v0.Server.Stable.Tests.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace g0v0.Server.Stable.Tests.Routes;

public partial class SubmitScoreRouteHandlerTests
{
    private const string Password = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ILoginRepository> _logins = new();
    private readonly Mock<IScoreRepository> _scores = new();
    private readonly Mock<IBeatmapRepository> _beatmaps = new();
    private readonly Mock<IStorageService> _storage = new();
    private ServiceProvider _services = null!;
    private ScoreProcessingQueue _queue = null!;
    private StableScoreSubmissionTracker _submissions = null!;
    private SubmitScoreRouteHandler _handler = null!;
    private string _configPath = null!;
    private ConfigManager _configs = null!;
    private GameConfig _game = null!;
    private Score? _saved;
    private byte[]? _uploaded;
    private Mock<IUserStatisticsRepository> _statistics = null!;
    private Mock<IScoreProcessingService> _processor = null!;
    private Mock<IAchievementService> _achievements = null!;

    [SetUp]
    public void SetUp()
    {
        _users.Reset();
        _logins.Reset();
        _scores.Reset();
        _beatmaps.Reset();
        _storage.Reset();
        _configPath = Path.Combine(Path.GetTempPath(), "stable-score-config-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(_configPath, "config"));
        File.WriteAllText(Path.Combine(_configPath, "config", "general.json"), "{}");
        File.WriteAllText(Path.Combine(_configPath, "config", "game.json"), "{}");
        _configs = new ConfigManager(_configPath);
        _game = _configs.Get<GameConfig>();
        _saved = null;
        _uploaded = null;
        _services = new ServiceCollection().AddLogging().BuildServiceProvider();
        _submissions = new StableScoreSubmissionTracker();
        _queue = new ScoreProcessingQueue(
            _services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ScoreProcessingQueue>.Instance,
            "stable",
            (services, score, cancellationToken) => services.GetRequiredService<ScoreProcessingService>().ProcessAsync(score, cancellationToken),
            score => !score.Passed || score.HasReplay);
        PlayerManager players = new();
        players.AddPlayer(new Player(1, "token"));
        _users.Setup(u => u.GetByUsernameAsync("peppy")).ReturnsAsync(new User { Id = 1, Username = "peppy" });
        _logins.Setup(l => l.GetPasswordHashAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(BCrypt.Net.BCrypt.HashPassword(Password, 4));
        _beatmaps.Setup(b => b.GetOrFetchByChecksumAsync(new string('a', 32), It.IsAny<IFetcher>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Beatmap { Id = 2, BeatmapSetId = 3 });
        _scores.Setup(s => s.CreateAsync(It.IsAny<Score>())).Callback<Score>(s =>
        {
            s.Id = 42;
            _saved = s;
        }).Returns(Task.CompletedTask);
        _scores.Setup(s => s.MarkScoreHasReplay(It.IsAny<long>())).Returns(Task.CompletedTask);
        _scores.Setup(s => s.GetByBeatmapIdAsync(It.IsAny<int>())).ReturnsAsync([]);
        _scores.Setup(s => s.GetBestBeatmapScoreAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Score?)null);
        _storage.Setup(s => s.WriteFileAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, byte[], string, string, CancellationToken>((_, bytes, _, _, _) => _uploaded = bytes).Returns(Task.CompletedTask);
        _statistics = new Mock<IUserStatisticsRepository>();
        _statistics.Setup(s => s.GetByUserIdAndModeAsync(1, It.IsAny<int>()))
            .ReturnsAsync(new UserStatistics { UserId = 1, Mode = 0 });
        _statistics.Setup(s => s.GetGlobalRankAsync(1, It.IsAny<int>())).ReturnsAsync(7);
        _processor = new Mock<IScoreProcessingService>();
        _achievements = new Mock<IAchievementService>();
        _achievements.Setup(service => service.UnlockAsync(It.IsAny<Score>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _handler = new SubmitScoreRouteHandler(
            new StableWebAuthenticator(_users.Object, _logins.Object),
            _scores.Object,
            _beatmaps.Object,
            Mock.Of<IFetcher>(),
            players,
            new RulesetManager("missing"),
            _storage.Object,
            _queue,
            _submissions,
            _statistics.Object,
            _processor.Object,
            _achievements.Object,
            _configs,
            NullLogger<SubmitScoreRouteHandler>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _submissions.Dispose();
        _queue.Dispose();
        _services.Dispose();
        Directory.Delete(_configPath, recursive: true);
    }

    [Test]
    public async Task ReadsBothMultipartScorePartsPersistsPendingAndUploadsReplay()
    {
        DefaultHttpContext context = await Context();
        await (await _handler.HandleAsync(context)).ExecuteAsync(context);
        string response = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        Assert.Multiple(() =>
        {
            Assert.That(_saved, Is.Not.Null);
            Assert.That(_saved!.Type, Is.EqualTo("stable"));
            Assert.That(_saved.ClassicTotalScore, Is.EqualTo(26810));
            Assert.That(_saved.Processed, Is.False);
            Assert.That(_saved.MaximumStatistics, Is.Empty);
            Assert.That(_uploaded, Is.Not.Null);
            Assert.That(response, Does.Contain("onlineScoreId:42"));
        });
        _storage.Verify(s => s.WriteFileAsync("replays/42_2_1_stable_replay.osr", It.IsAny<byte[]>(), "application/x-osu-replay", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _scores.Verify(s => s.MarkScoreHasReplay(42), Times.Once);
    }

    [TestCase(true, 4)]
    [TestCase(false, 0)]
    public async Task PersistsRelaxInConfiguredMode(bool enabled, int expectedMode)
    {
        _game.EnableRelax = enabled;
        DefaultHttpContext context = await Context();
        await _handler.HandleAsync(context);
        Assert.That(_saved!.Mode, Is.EqualTo(expectedMode));
    }

    [Test]
    public async Task DuplicateUploadReusesScoreAndReplay()
    {
        await RememberSubmission();
        _scores.Setup(s => s.GetByIdAsync(42)).ReturnsAsync(new Score { Id = 42, BeatmapId = 2, UserId = 1, Passed = true, HasReplay = true });
        DefaultHttpContext context = await Context();
        await _handler.HandleAsync(context);
        _scores.Verify(s => s.CreateAsync(It.IsAny<Score>()), Times.Never);
        _storage.Verify(s => s.WriteFileAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCase("incorrect-password", true, false)]
    [TestCase(Password, false, false)]
    [TestCase(Password, true, true)]
    public async Task InvalidAuthenticationReplayOrChecksumDoesNotPersist(string password, bool replay, bool tamper)
    {
        DefaultHttpContext context = await Context(password, replay, tamper);
        await _handler.HandleAsync(context);
        _scores.Verify(s => s.CreateAsync(It.IsAny<Score>()), Times.Never);
    }

    [Test]
    public async Task InterruptedReplayUploadCanResumeOnRetry()
    {
        await RememberSubmission();
        _scores.Setup(s => s.GetByIdAsync(42)).ReturnsAsync(new Score { Id = 42, BeatmapId = 2, UserId = 1, Passed = true });
        DefaultHttpContext context = await Context();
        await _handler.HandleAsync(context);
        _scores.Verify(s => s.CreateAsync(It.IsAny<Score>()), Times.Never);
        _scores.Verify(s => s.MarkScoreHasReplay(42), Times.Once);
        Assert.That(_uploaded, Is.Not.Null);
    }

    [Test]
    public async Task ReportsTheBeatmapLeaderboardPositionInsteadOfTheGradeLetter()
    {
        // The client stores the beatmap chart rank in an integer field, so the response has to carry
        // the one-based leaderboard position; a grade letter parses as zero and the results screen
        // shows no rank.
        _scores.Setup(s => s.GetBestBeatmapScoreAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Score { Id = 41, Rank = osu.Game.Scoring.ScoreRank.A, Passed = true });
        _scores.Setup(s => s.GetBeatmapLeaderboardRankAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        DefaultHttpContext context = await Context();
        await (await _handler.HandleAsync(context)).ExecuteAsync(context);
        string response = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        Assert.That(response, Does.Contain("rankBefore:3|rankAfter:3"));
    }

    private async Task RememberSubmission()
    {
        string checksum = StableScoreSubmission.Parse(StableScoreSubmissionTests.Fixture()["plaintext"]).Checksum;
        using StableScoreSubmissionTracker.Lease lease = await _submissions.AcquireAsync(1, checksum, CancellationToken.None);
        lease.ScoreId = 42;
    }

    private async Task<DefaultHttpContext> Context(string password = Password, bool replay = true, bool tamper = false)
    {
        Dictionary<string, string> fixture = StableScoreSubmissionTests.Fixture();
        using MultipartFormDataContent multipart = new();
        foreach ((string name, string value) in new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["score"] = fixture["encrypted"],
            ["iv"] = fixture["iv"],
            ["s"] = fixture["encryptedClientHash"],
            ["osuver"] = fixture["version"],
            ["sbk"] = tamper ? "modified" : fixture["storyboardHash"],
            ["bmk"] = new('a', 32),
            ["pass"] = password,
            ["st"] = "2000",
            ["ft"] = "0"
        })
        {
            multipart.Add(new StringContent(value), name);
        }

        if (replay)
        {
            ByteArrayContent file = new(new byte[20]);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            multipart.Add(file, "score", "replay");
        }

        byte[] body = await multipart.ReadAsByteArrayAsync();
        DefaultHttpContext context = new() { RequestServices = _services };
        context.Request.ContentType = multipart.Headers.ContentType!.ToString();
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
        context.Response.Body = new MemoryStream();
        return context;
    }
}
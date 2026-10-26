// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using g0v0.Server.Common.Beatmaps;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Fetching;
using g0v0.Server.Common.GeoIP;
using g0v0.Server.Common.Scoring;
using g0v0.Server.Common.Screenshots;
using g0v0.Server.Common.Storage;
using g0v0.Server.Common.Utils;
using g0v0.Server.Stable.Routes;
using g0v0.Server.Stable.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using osu.Game.Beatmaps;
using osu.Game.IO.Legacy;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using Beatmap = g0v0.Server.Common.Database.Models.Beatmap;

namespace g0v0.Server.Stable.Tests.Routes;

public partial class LegacyWebRouteHandlerTests
{
    private const string Password = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Checksum = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly string PasswordHash = UserPasswordHasher.HashDigest(Password, workFactor: 4);
    private Mock<IUserRepository> _users = null!;
    private Mock<ILoginRepository> _logins = null!;
    private Mock<IBeatmapFeedbackRepository> _feedback = null!;
    private Mock<IBeatmapRepository> _beatmaps = null!;
    private Mock<IBeatmapSetRepository> _sets = null!;
    private Mock<IFetcher> _fetcher = null!;
    private Mock<IScoreRepository> _scores = null!;
    private Mock<IStorageService> _storage = null!;
    private Mock<IScreenshotRepository> _screenshots = null!;
    private Mock<IGeoIPService> _geoIp = null!;
    private StableWebAuthenticator _authenticator = null!;
    private BeatmapFeedbackService _feedbackService = null!;
    private ScreenshotService _screenshotService = null!;
    private ServiceProvider _services = null!;
    private string _configPath = null!;
    private ConfigManager _configs = null!;

    [SetUp]
    public void SetUp()
    {
        _users = new Mock<IUserRepository>();
        _logins = new Mock<ILoginRepository>();
        _feedback = new Mock<IBeatmapFeedbackRepository>();
        _beatmaps = new Mock<IBeatmapRepository>();
        _sets = new Mock<IBeatmapSetRepository>();
        _fetcher = new Mock<IFetcher>();
        _scores = new Mock<IScoreRepository>();
        _storage = new Mock<IStorageService>();
        _screenshots = new Mock<IScreenshotRepository>();
        _geoIp = new Mock<IGeoIPService>();
        _users.Setup(repository => repository.GetByUsernameAsync("player one")).ReturnsAsync(new User { Id = 1, Username = "player one" });
        _logins.Setup(repository => repository.GetPasswordHashAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(PasswordHash);
        _authenticator = new StableWebAuthenticator(_users.Object, _logins.Object);
        _feedbackService = new BeatmapFeedbackService(_feedback.Object, _beatmaps.Object, _sets.Object, _fetcher.Object, TimeProvider.System);
        _screenshotService = new ScreenshotService(_screenshots.Object, _storage.Object, TimeProvider.System);
        _services = new ServiceCollection().AddLogging().BuildServiceProvider();
        _configPath = Path.Combine(Path.GetTempPath(), "stable-web-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(_configPath, "config"));
        _configs = new ConfigManager(_configPath);
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
        Directory.Delete(_configPath, recursive: true);
    }

    [Test]
    public async Task SeasonalBackgroundsAreAnAnonymousArrayAndReflectReloads()
    {
        SeasonalRouteHandler handler = new(_configs);
        DefaultHttpContext empty = Context();
        Assert.That(await ExecuteAsync(handler, empty), Is.EqualTo("[]"));
        await File.WriteAllTextAsync(Path.Combine(_configPath, "config", "game.json"), "{\"SeasonalBackgrounds\":[\"https://example.test/bg.jpg\"]}");
        Assert.That(await ExecuteAsync(handler, Context()), Is.EqualTo("[\"https://example.test/bg.jpg\"]"));
        await File.WriteAllTextAsync(Path.Combine(_configPath, "config", "game.json"), "{\"SeasonalBackgrounds\":[]}");
        _configs.Reload<GameConfig>();
        Assert.That(await ExecuteAsync(handler, Context()), Is.EqualTo("[]"));
    }

    [TestCase("favourites")]
    [TestCase("add")]
    [TestCase("rating")]
    [TestCase("replay")]
    [TestCase("screenshot")]
    public async Task InvalidCredentialsCannotReadOrChangeSharedData(string route)
    {
        DefaultHttpContext context = string.Equals(route, "screenshot", StringComparison.Ordinal) ? await ScreenshotContextAsync([1, 2, 3], "wrong") : Context();
        IRouteHandler handler = route switch
        {
            "favourites" => new FavouritesRouteHandler(_authenticator, _feedback.Object),
            "add" => new AddFavouriteRouteHandler(_authenticator, _feedbackService),
            "rating" => new RatingRouteHandler(_authenticator, _feedbackService),
            "replay" => new ReplayRouteHandler(_authenticator, new ReplayService(_scores.Object, _storage.Object)),
            _ => new ScreenshotRouteHandler(_authenticator, _screenshotService),
        };
        string body = await ExecuteAsync(handler, context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(401));
        if (string.Equals(route, "rating", StringComparison.Ordinal))
        {
            Assert.That(body, Is.EqualTo("auth fail"));
        }

        _feedback.VerifyNoOtherCalls();
        _scores.VerifyNoOtherCalls();
        _storage.VerifyNoOtherCalls();
    }

    [TestCase(false, "42\n123")]
    [TestCase(true, "")]
    public async Task FavouritesUseNewlineDelimitedSetIdsWithoutATrailingNewline(bool empty, string expected)
    {
        _feedback.Setup(repository => repository.GetFavouritesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(empty ? [] : new List<int> { 42, 123 });
        string body = await ExecuteAsync(new FavouritesRouteHandler(_authenticator, _feedback.Object), Authenticated());
        Assert.That(body, Is.EqualTo(expected));
    }

    [TestCase(true, "Added favourite!")]
    [TestCase(false, "You've already favourited this beatmap!")]
    public async Task AddFavouritePersistsTheAuthenticatedUserAndReportsDuplicates(bool added, string expected)
    {
        _sets.Setup(repository => repository.GetOrFetchByIdAsync(42, _fetcher.Object, It.IsAny<CancellationToken>())).ReturnsAsync(new BeatmapSet { Id = 42 });
        _feedback.Setup(repository => repository.AddFavouriteAsync(It.Is<FavouriteBeatmapSet>(item => item.UserId == 1 && item.BeatmapSetId == 42), It.IsAny<CancellationToken>())).ReturnsAsync(added);
        string body = await ExecuteAsync(new AddFavouriteRouteHandler(_authenticator, _feedbackService), Authenticated("&a=42"));
        Assert.That(body, Is.EqualTo(expected));
        _sets.Verify(repository => repository.GetOrFetchByIdAsync(42, _fetcher.Object, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task InvalidFavouriteIdNeverFetchesOrWrites()
    {
        DefaultHttpContext context = Authenticated("&a=-1");
        await ExecuteAsync(new AddFavouriteRouteHandler(_authenticator, _feedbackService), context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
        _sets.VerifyNoOtherCalls();
        _feedback.VerifyNoOtherCalls();
    }

    [Test]
    public async Task UnknownRatingChecksumReportsNoExist()
    {
        string body = await ExecuteAsync(new RatingRouteHandler(_authenticator, _feedbackService), Authenticated("&c=" + Checksum, passwordField: "p"));
        Assert.That(body, Is.EqualTo("no exist"));
        _feedback.VerifyNoOtherCalls();
    }

    [TestCase(BeatmapOnlineStatus.Pending, "not ranked")]
    [TestCase(BeatmapOnlineStatus.Ranked, "ok")]
    public async Task RatingChecksRespectBeatmapEligibility(BeatmapOnlineStatus status, string expected)
    {
        SetRatingBeatmap(status);
        string body = await ExecuteAsync(new RatingRouteHandler(_authenticator, _feedbackService), Authenticated("&c=" + Checksum, passwordField: "p"));
        Assert.That(body, Is.EqualTo(expected));
        _feedback.Verify(repository => repository.AddRatingAsync(It.IsAny<BeatmapRating>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    [SetCulture("fr-FR")]
    public async Task RatingSubmissionUsesTheSharedSetIdAndAnInvariantAverage()
    {
        SetRatingBeatmap(BeatmapOnlineStatus.Ranked);
        _feedback.Setup(repository => repository.GetAverageRatingAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(7.5);
        string body = await ExecuteAsync(new RatingRouteHandler(_authenticator, _feedbackService), Authenticated("&c=" + Checksum + "&v=8", passwordField: "p"));
        Assert.That(body, Is.EqualTo("alreadyvoted\n7.5"));
        _feedback.Verify(repository => repository.AddRatingAsync(It.Is<BeatmapRating>(vote => vote.BeatmapSetId == 42 && vote.UserId == 1 && vote.Rating == 8), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CheckingAnExistingRatingReturnsTheAverageWithoutWriting()
    {
        SetRatingBeatmap(BeatmapOnlineStatus.Ranked);
        _feedback.Setup(repository => repository.HasRatingAsync(1, 42, It.IsAny<CancellationToken>())).ReturnsAsync(value: true);
        _feedback.Setup(repository => repository.GetAverageRatingAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(value: 8);
        Assert.That(await ExecuteAsync(new RatingRouteHandler(_authenticator, _feedbackService), Authenticated("&c=" + Checksum, passwordField: "p")), Is.EqualTo("alreadyvoted\n8"));
        _feedback.Verify(repository => repository.AddRatingAsync(It.IsAny<BeatmapRating>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task UnrankedBeatmapsCannotBypassTheCheckBySubmittingAVoteDirectly()
    {
        SetRatingBeatmap(BeatmapOnlineStatus.Pending);
        Assert.That(await ExecuteAsync(new RatingRouteHandler(_authenticator, _feedbackService), Authenticated("&c=" + Checksum + "&v=8", passwordField: "p")), Is.EqualTo("not ranked"));
        _feedback.VerifyNoOtherCalls();
    }

    [Test]
    public async Task InvalidRatingsDoNotReachTheDatabase()
    {
        DefaultHttpContext context = Authenticated("&c=" + Checksum + "&v=11", passwordField: "p");
        await ExecuteAsync(new RatingRouteHandler(_authenticator, _feedbackService), context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
        _beatmaps.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ReplayReturnsOnlyCompressedFramesFromTheSharedOsrFile()
    {
        byte[] frames = [0x5d, 0, 0, 0x80, 0, 1, 2, 3, 4, 5, 6, 7, 8];
        ConfigureReplay(CreateReplay(frames));
        DefaultHttpContext context = Authenticated("&c=42&m=0");
        await ExecuteAsync(new ReplayRouteHandler(_authenticator, new ReplayService(_scores.Object, _storage.Object)), context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(((MemoryStream)context.Response.Body).ToArray(), Is.EqualTo(frames));
        Assert.That(context.Response.ContentType, Is.EqualTo("application/octet-stream"));
        _storage.Verify(storage => storage.ReadFileAsync("replays/42_2_1_lazer_replay.osr", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase("score")]
    [TestCase("flag")]
    [TestCase("file")]
    [TestCase("truncated")]
    [TestCase("mode")]
    public async Task MissingOrInvalidReplaysReturnAnEmpty404(string failure)
    {
        ConfigureReplay(string.Equals(failure, "truncated", StringComparison.Ordinal) ? [0, 1, 2] : CreateReplay([1, 2, 3]));
        if (string.Equals(failure, "score", StringComparison.Ordinal))
        {
            _scores.Setup(repository => repository.GetByIdAsync(42)).ReturnsAsync((Score?)null);
        }
        else if (string.Equals(failure, "flag", StringComparison.Ordinal))
        {
            _scores.Setup(repository => repository.GetByIdAsync(42)).ReturnsAsync(new Score { Id = 42, HasReplay = false });
        }
        else if (string.Equals(failure, "file", StringComparison.Ordinal))
        {
            _storage.Setup(storage => storage.IsExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(value: false);
        }

        DefaultHttpContext context = Authenticated("&c=42&m=" + (string.Equals(failure, "mode", StringComparison.Ordinal) ? "1" : "0"));
        string body = await ExecuteAsync(new ReplayRouteHandler(_authenticator, new ReplayService(_scores.Object, _storage.Object)), context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(404));
        Assert.That(body, Is.Empty);
    }

    [Test]
    public async Task PngUploadReturnsTheHashOfTheJpegStoredUnderTheLazerKey()
    {
        using Image<Rgb24> image = new(2, 2);
        using MemoryStream png = new();
        await image.SaveAsPngAsync(png);
        byte[]? uploaded = null;
        string? key = null;
        Screenshot? record = null;
        _storage.Setup(storage => storage.WriteFileAsync(It.IsAny<string>(), It.IsAny<byte[]>(), "image/jpeg", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, byte[], string, string, CancellationToken>((path, bytes, _, _, _) =>
            {
                key = path;
                uploaded = bytes;
            })
            .Returns(Task.CompletedTask);
        _storage.Setup(storage => storage.GetFileUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("https://storage.test/signed");
        _screenshots.Setup(repository => repository.RecordUploadAsync(It.IsAny<Screenshot>(), It.IsAny<CancellationToken>()))
            .Callback<Screenshot, CancellationToken>((screenshot, _) => record = screenshot)
            .ReturnsAsync((Screenshot screenshot, CancellationToken _) => screenshot);
        DefaultHttpContext context = await ScreenshotContextAsync(png.ToArray());
        string hash = await ExecuteAsync(new ScreenshotRouteHandler(_authenticator, _screenshotService), context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(hash, Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(uploaded!))));
        Assert.That(key, Is.EqualTo($"screenshots/1_{hash}.jpg"));
        Assert.That(Image.DetectFormat(uploaded!), Is.TypeOf<JpegFormat>());
        Assert.That(record!.Sha256Hash, Is.EqualTo(hash));
        Assert.That(record.UserId, Is.EqualTo(1));
    }

    [Test]
    public async Task InvalidImageBytesAreRejectedBeforeWritingStorage()
    {
        DefaultHttpContext context = await ScreenshotContextAsync([0xff, 0xd8, 0xff, 0xe0, 0xff, 0xd9]);
        string body = await ExecuteAsync(new ScreenshotRouteHandler(_authenticator, _screenshotService), context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
        Assert.That(body, Is.EqualTo("Invalid file type"));
        _storage.VerifyNoOtherCalls();
        _screenshots.VerifyNoOtherCalls();
    }

    [Test]
    public async Task OversizedScreenshotIsRejectedBeforeReadingTheUpload()
    {
        DefaultHttpContext context = Context();
        context.Request.ContentLength = ScreenshotService.MaximumUploadSize + (128 * 1024);
        string body = await ExecuteAsync(new ScreenshotRouteHandler(_authenticator, _screenshotService), context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
        Assert.That(body, Is.EqualTo("Screenshot file too large."));
        _storage.VerifyNoOtherCalls();
    }

    [Test]
    public async Task DifficultyRedirectPreservesThePostMethodAndBody()
    {
        DefaultHttpContext context = Context();
        context.Request.Method = "POST";
        context.Request.Body = new MemoryStream("{\"beatmap_id\":42}"u8.ToArray());
        await ExecuteAsync(new DifficultyRatingRouteHandler(), context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(307));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("https://osu.ppy.sh/difficulty-rating"));
        Assert.That(context.Request.Body.Position, Is.Zero);
    }

    [TestCase("CN", "42n", "https://dl.sayobot.cn/beatmaps/download/novideo/42")]
    [TestCase("CN", "42", "https://dl.sayobot.cn/beatmaps/download/full/42")]
    [TestCase("US", "42n", "https://catboy.best/d/42n")]
    [TestCase("US", "42", "https://catboy.best/d/42")]
    public async Task ArchiveRedirectUsesTheRegionalMirrorAndVideoSelection(string country, string id, string expected)
    {
        _geoIp.Setup(service => service.Lookup(It.IsAny<IPAddress>())).Returns(new GeoIPLookupResult("127.0.0.1") { CountryIso = country });
        DefaultHttpContext context = Context();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.RouteValues["map_set_id"] = id;
        await ExecuteAsync(new BeatmapDownloadRouteHandler(new BeatmapDownloadService(_configs, _geoIp.Object)), context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(302));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo(expected));
    }

    [Test]
    public async Task InvalidArchiveIdsCannotBecomeRedirectPaths()
    {
        DefaultHttpContext context = Context();
        context.Request.RouteValues["map_set_id"] = "42?next=https://example.test";
        await ExecuteAsync(new BeatmapDownloadRouteHandler(new BeatmapDownloadService(_configs, _geoIp.Object)), context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
        Assert.That(context.Response.Headers.Location.ToString(), Is.Empty);
    }

    [Test]
    public async Task BeatmapFileResolvesTheFullUnicodeFilenameAndReturnsRawUtf8()
    {
        const string filename = "作曲者 - Song (Mapper) [Insane].osu";
        const string raw = "osu file format v14\n[Metadata]\nTitle:新曲\n";
        _fetcher.Setup(fetcher => fetcher.FetchBeatmapByFilenameAsync(filename, It.IsAny<CancellationToken>())).ReturnsAsync(new Beatmap { Id = 123 });
        _fetcher.Setup(fetcher => fetcher.FetchBeatmapRawAsync(123, It.IsAny<CancellationToken>())).ReturnsAsync(raw);
        DefaultHttpContext context = Context();
        context.Request.RouteValues["map_filename"] = filename;
        string body = await ExecuteAsync(new BeatmapFileRouteHandler(_fetcher.Object), context);
        Assert.That(body, Is.EqualTo(raw));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/plain; charset=utf-8"));
    }

    [Test]
    public async Task MissingBeatmapFileReturns404WithoutGuessingByDifficultyName()
    {
        DefaultHttpContext context = Context();
        context.Request.RouteValues["map_filename"] = "Unknown - Song (Mapper) [Insane].osu";
        await ExecuteAsync(new BeatmapFileRouteHandler(_fetcher.Object), context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(404));
        _fetcher.Verify(fetcher => fetcher.FetchBeatmapRawAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task BeatmapFileFetchFailureReturns502()
    {
        _fetcher.Setup(fetcher => fetcher.FetchBeatmapByFilenameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new FetcherException("upstream unavailable"));
        DefaultHttpContext context = Context();
        context.Request.RouteValues["map_filename"] = "Map.osu";
        await ExecuteAsync(new BeatmapFileRouteHandler(_fetcher.Object), context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(502));
    }

    private static byte[] CreateReplay(byte[] frames)
    {
        using MemoryStream stream = new();
        using (SerializationWriter writer = new(stream, leaveOpen: true))
        {
            writer.Write((byte)0);
            writer.Write(20261025);
            writer.Write(Checksum);
            writer.Write("player one");
            writer.Write("replay checksum");
            for (int i = 0; i < 6; i++)
            {
                writer.Write((ushort)123);
            }

            writer.Write(123456);
            writer.Write((ushort)789);
            writer.Write(value: false);
            writer.Write(0);
            writer.Write("0|1,1000|0.5");
            writer.Write(DateTime.UtcNow);
            writer.Write(frames);
            writer.Write(42L);
            writer.Write("lazer trailer");
        }

        return stream.ToArray();
    }

    private static async Task<string> ExecuteAsync(IRouteHandler handler, DefaultHttpContext context)
    {
        await (await handler.HandleAsync(context)).ExecuteAsync(context);
        return Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
    }

    private void SetRatingBeatmap(BeatmapOnlineStatus status)
        => _beatmaps.Setup(repository => repository.GetByChecksumAsync(Checksum)).ReturnsAsync(new Beatmap { Id = 2, BeatmapSetId = 42, Status = status });

    private void ConfigureReplay(byte[] replay)
    {
        _scores.Setup(repository => repository.GetByIdAsync(42)).ReturnsAsync(new Score { Id = 42, BeatmapId = 2, UserId = 1, HasReplay = true });
        _storage.Setup(storage => storage.IsExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(value: true);
        _storage.Setup(storage => storage.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(replay);
    }

    private DefaultHttpContext Context()
    {
        DefaultHttpContext context = new() { RequestServices = _services };
        context.Response.Body = new MemoryStream();
        return context;
    }

    private DefaultHttpContext Authenticated(string extra = "", string passwordField = "h")
    {
        DefaultHttpContext context = Context();
        context.Request.QueryString = new QueryString("?u=player+one&" + passwordField + "=" + Password + extra);
        return context;
    }

    private async Task<DefaultHttpContext> ScreenshotContextAsync(byte[] bytes, string password = Password)
    {
        using MultipartFormDataContent multipart = new();
        multipart.Add(new StringContent("player%20one"), "u");
        multipart.Add(new StringContent(password), "p");
        multipart.Add(new StringContent("1"), "v");
        multipart.Add(new ByteArrayContent(bytes), "ss", "screenshot.png");
        byte[] body = await multipart.ReadAsByteArrayAsync();
        DefaultHttpContext context = Context();
        context.Request.Method = "POST";
        context.Request.ContentType = multipart.Headers.ContentType!.ToString();
        context.Request.ContentLength = body.Length;
        context.Request.Body = new MemoryStream(body);
        return context;
    }
}
// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using g0v0.Server.Common.Beatmaps;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Fetching;
using g0v0.Server.Stable.Routes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using osu.Game.Beatmaps;
using Beatmap = g0v0.Server.Common.Database.Models.Beatmap;

namespace g0v0.Server.Stable.Tests.Routes;

public partial class LegacyWebRouteHandlerTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task DirectRequiresValidCredentialsBeforeCallingTheSharedService(bool lookup)
    {
        Mock<IBeatmapSearchService> search = new(MockBehavior.Strict);
        IRouteHandler handler = lookup
            ? new SearchSetRouteHandler(_authenticator, search.Object, NullLogger<SearchSetRouteHandler>.Instance)
            : DirectSearch(search);
        DefaultHttpContext context = Context();
        context.Request.QueryString = new QueryString("?u=player+one&h=wrong&s=42");
        await ExecuteAsync(handler, context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(401));
        search.VerifyNoOtherCalls();
    }

    [TestCase("&r=0&q=Newest", "ranked", "updated_desc", "")]
    [TestCase("&r=1&q=song", "ranked", "relevance_desc", "song status=ranked")]
    [TestCase("&r=2&q=Top+Rated", "pending", "rating_desc", "")]
    [TestCase("&r=3&q=Most%2BPlayed", "qualified", "plays_desc", "")]
    [TestCase("&r=4", "any", "ranked_desc", "")]
    [TestCase("&r=5", "graveyard", "ranked_desc", "")]
    [TestCase("&r=6", "ranked", "ranked_desc", "status=approved")]
    [TestCase("&r=8", "loved", "ranked_desc", "")]
    public async Task DirectMapsLegacyCategoriesAndPresetSorts(string parameters, string status, string sort, string keywords)
    {
        Mock<IBeatmapSearchService> search = new(MockBehavior.Strict);
        search.Setup(service => service.SearchAsync(
                It.Is<BeatmapSearchQuery>(query => query.Query == keywords && query.Status == status
                    && query.Sort == sort && query.Mode == 3 && query.IncludeExplicitContent),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BeatmapSearchResult());
        DefaultHttpContext context = Authenticated(parameters + "&m=3");
        Assert.That(await ExecuteAsync(DirectSearch(search), context), Is.EqualTo("0"));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/plain; charset=utf-8"));
        search.VerifyAll();
    }

    [TestCase("&m=4")]
    [TestCase("&r=9")]
    [TestCase("&p=-1")]
    [TestCase("&p=100")]
    [TestCase("&p=not-a-number")]
    public async Task DirectRejectsInvalidFiltersAndPages(string parameters)
    {
        Mock<IBeatmapSearchService> search = new(MockBehavior.Strict);
        DefaultHttpContext context = Authenticated(parameters);
        await ExecuteAsync(DirectSearch(search), context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
        search.VerifyNoOtherCalls();
    }

    [TestCase(0, 1, 100, "101", 2)]
    [TestCase(1, 101, 100, "101", 4)]
    [TestCase(2, 201, 1, "1", 5)]
    [TestCase(3, 0, 0, "0", 5)]
    public async Task DirectCombinesCursorPagesIntoStablePages(int page, int firstId, int count, string header, int calls)
    {
        Mock<IBeatmapSearchService> search = new(MockBehavior.Strict);
        search.Setup(service => service.SearchAsync(It.IsAny<BeatmapSearchQuery>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BeatmapSearchQuery _, string? cursor, CancellationToken _) =>
            {
                int offset = cursor is null ? 0 : int.Parse(cursor, CultureInfo.InvariantCulture);
                return new BeatmapSearchResult
                {
                    Total = 201,
                    BeatmapSets = Enumerable.Range(offset + 1, Math.Min(50, 201 - offset)).Select(DirectSet).ToArray(),
                    Cursor = offset + 50 < 201 ? (offset + 50).ToString(CultureInfo.InvariantCulture) : null
                };
            });
        string[] lines = (await ExecuteAsync(DirectSearch(search), Authenticated("&p=" + page))).Split('\n');
        Assert.That(lines[0], Is.EqualTo(header));
        Assert.That(lines, Has.Length.EqualTo(count + 1));
        if (count > 0)
        {
            Assert.That(lines[1], Does.StartWith(firstId + ".osz|"));
            Assert.That(lines[^1], Does.StartWith((firstId + count - 1) + ".osz|"));
        }

        search.Verify(service => service.SearchAsync(It.IsAny<BeatmapSearchQuery>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Exactly(calls));
    }

    [Test]
    public async Task DirectFullTerminalPageDoesNotAdvertiseAnotherPage()
    {
        Mock<IBeatmapSearchService> search = new();
        search.Setup(service => service.SearchAsync(It.IsAny<BeatmapSearchQuery>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BeatmapSearchResult { Total = 100, BeatmapSets = Enumerable.Range(1, 100).Select(DirectSet).ToArray() });
        string[] lines = (await ExecuteAsync(DirectSearch(search), Authenticated())).Split('\n');
        Assert.That(lines[0], Is.EqualTo("100"));
        Assert.That(lines, Has.Length.EqualTo(101));
    }

    [Test]
    public async Task DirectFormatsAndSanitizesRecordsWithInvariantNumbersAndOrderedDifficulties()
    {
        BeatmapSet set = DirectSet(42);
        set.Artist = "Artist|作曲者\n";
        set.Title = "Title\r\n";
        set.Creator = "Mapper";
        set.Status = BeatmapOnlineStatus.Loved;
        set.LastUpdatedDate = new DateTimeOffset(2026, 10, 26, 18, 30, 0, TimeSpan.FromHours(8));
        set.HasVideo = true;
        set.HasStoryboard = true;
        set.Beatmaps =
        [
            new Beatmap { Id = 2, Version = "Hard", DifficultyRating = 5.25, Mode = 3, CircleSize = 4 },
            new Beatmap { Id = 1, Version = "Easy,@|\n", DifficultyRating = 1.5, CircleSize = 3.5f, OverallDifficulty = 4, ApproachRate = 5, HpDrainRate = 6 }
        ];
        Mock<IBeatmapSearchService> search = new();
        search.Setup(service => service.SearchAsync(It.IsAny<BeatmapSearchQuery>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BeatmapSearchResult { Total = 1, BeatmapSets = [set] });
        CultureInfo previous = CultureInfo.CurrentCulture;
        string body;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            body = await ExecuteAsync(DirectSearch(search), Authenticated());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        string[] lines = body.Split('\n');
        Assert.That(lines, Has.Length.EqualTo(2));
        Assert.That(lines[1], Is.EqualTo("42.osz|ArtistI作曲者 |Title  |Mapper|4|10.0|2026-10-26 10:30:00|42|0|1|1|0|0|"
            + "[1.50⭐] Easy，＠I  {cs: 3.5 / od: 4 / ar: 5 / hp: 6}@0,[5.25⭐] Hard {cs: 4 / od: 0 / ar: 0 / hp: 0}@3"));
        Assert.That(lines[1].Split('|'), Has.Length.EqualTo(14));
    }

    [Test]
    public async Task DirectPlayedCategoryUsesLocalHistoryAndDeduplicatesSetsAcrossPages()
    {
        _scores.Setup(repository => repository.GetByUserIdAsync(1)).ReturnsAsync([new Score { UserId = 1, BeatmapId = 20 }]);
        Mock<IBeatmapSearchService> search = new(MockBehavior.Strict);
        BeatmapSet played = DirectSet(2);
        search.Setup(service => service.SearchAsync(It.Is<BeatmapSearchQuery>(query => query.Status == "ranked"), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BeatmapSearchResult { Total = 3, BeatmapSets = [DirectSet(1), played], Cursor = "next" });
        search.Setup(service => service.SearchAsync(It.IsAny<BeatmapSearchQuery>(), "next", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BeatmapSearchResult { Total = 3, BeatmapSets = [played, new BeatmapSet { Id = 3 }] });
        string[] lines = (await ExecuteAsync(DirectSearch(search), Authenticated("&r=7"))).Split('\n');
        Assert.That(lines, Has.Length.EqualTo(2));
        Assert.That(lines[0], Is.EqualTo("1"));
        Assert.That(lines[1], Does.StartWith("2.osz|"));
        _scores.Verify(repository => repository.GetByUserIdAsync(1), Times.Once);
    }

    [Test]
    public async Task DirectPlayedCategoryWithoutHistoryAvoidsTheUpstreamSearch()
    {
        _scores.Setup(repository => repository.GetByUserIdAsync(1)).ReturnsAsync([]);
        Mock<IBeatmapSearchService> search = new(MockBehavior.Strict);
        Assert.That(await ExecuteAsync(DirectSearch(search), Authenticated("&r=7")), Is.EqualTo("0"));
        search.VerifyNoOtherCalls();
    }

    [Test]
    public async Task DirectRepeatingCursorReturnsTheProtocolErrorInsteadOfLooping()
    {
        Mock<IBeatmapSearchService> search = new();
        search.Setup(service => service.SearchAsync(It.IsAny<BeatmapSearchQuery>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BeatmapSearchResult { Cursor = "same" });
        Assert.That(await ExecuteAsync(DirectSearch(search), Authenticated()), Is.EqualTo("-1\nFailed to retrieve data from the beatmap server."));
        search.Verify(service => service.SearchAsync(It.IsAny<BeatmapSearchQuery>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestCase("&s=42&b=99&c=BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", 42, null, null)]
    [TestCase("&b=99&c=BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", null, 99, null)]
    [TestCase("&c=BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", null, null, Checksum)]
    public async Task DirectSetLookupHonorsIdentifierPriorityAndOmitsDifficulties(string parameters, int? setId, int? beatmapId, string? checksum)
    {
        Mock<IBeatmapSearchService> search = new(MockBehavior.Strict);
        search.Setup(service => service.LookupAsync(setId, beatmapId, checksum, It.IsAny<CancellationToken>())).ReturnsAsync(DirectSet(42));
        SearchSetRouteHandler handler = new(_authenticator, search.Object, NullLogger<SearchSetRouteHandler>.Instance);
        string body = await ExecuteAsync(handler, Authenticated(parameters));
        Assert.That(body, Does.StartWith("42.osz|"));
        Assert.That(body.Split('|'), Has.Length.EqualTo(13));
        Assert.That(body, Does.Not.Contain("\n"));
        search.VerifyAll();
    }

    [TestCase("")]
    [TestCase("&s=0")]
    [TestCase("&s=no&b=99")]
    [TestCase("&b=-1")]
    [TestCase("&c=not-a-checksum")]
    public async Task DirectSetLookupInvalidIdentifiersReturnAnEmptyBody(string parameters)
    {
        Mock<IBeatmapSearchService> search = new(MockBehavior.Strict);
        SearchSetRouteHandler handler = new(_authenticator, search.Object, NullLogger<SearchSetRouteHandler>.Instance);
        DefaultHttpContext context = Authenticated(parameters);
        Assert.That(await ExecuteAsync(handler, context), Is.Empty);
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        search.VerifyNoOtherCalls();
    }

    [Test]
    public async Task DirectTranslatesUpstreamFailuresButPreservesRequestCancellation()
    {
        Mock<IBeatmapSearchService> search = new();
        search.Setup(service => service.SearchAsync(It.IsAny<BeatmapSearchQuery>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FetcherException("upstream unavailable"));
        search.Setup(service => service.LookupAsync(42, null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FetcherException("upstream unavailable"));
        Assert.That(await ExecuteAsync(DirectSearch(search), Authenticated()), Does.StartWith("-1\n"));
        SearchSetRouteHandler lookup = new(_authenticator, search.Object, NullLogger<SearchSetRouteHandler>.Instance);
        Assert.That(await ExecuteAsync(lookup, Authenticated("&s=42")), Is.Empty);
        search.Setup(service => service.LookupAsync(42, null, null, It.IsAny<CancellationToken>())).ReturnsAsync((BeatmapSet?)null);
        Assert.That(await ExecuteAsync(lookup, Authenticated("&s=42")), Is.Empty);

        DefaultHttpContext context = Authenticated();
        CancellationToken cancelled = new(canceled: true);
        search.Setup(service => service.SearchAsync(It.IsAny<BeatmapSearchQuery>(), null, It.IsAny<CancellationToken>()))
            .Returns<BeatmapSearchQuery, string?, CancellationToken>((_, _, _) =>
            {
                context.RequestAborted = cancelled;
                return Task.FromCanceled<BeatmapSearchResult>(cancelled);
            });

        Assert.ThrowsAsync<TaskCanceledException>(() => ExecuteAsync(DirectSearch(search), context));
    }

    private static BeatmapSet DirectSet(int id) => new()
    {
        Id = id,
        Artist = "Artist",
        Title = "Title",
        Creator = "Mapper",
        Beatmaps = [new Beatmap { Id = id * 10, BeatmapSetId = id, Version = "Normal" }]
    };

    private SearchRouteHandler DirectSearch(Mock<IBeatmapSearchService> search)
        => new(_authenticator, search.Object, _scores.Object, NullLogger<SearchRouteHandler>.Instance);
}
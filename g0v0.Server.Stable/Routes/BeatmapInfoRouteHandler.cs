// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Fetching;
using g0v0.Server.Stable.Services;
using osu.Game.Scoring;
using Score = g0v0.Server.Common.Database.Models.Score;

namespace g0v0.Server.Stable.Routes;

/// <summary>
/// Serves <c>POST /web/osu-getbeatmapinfo.php</c>: the batch beatmap status lookup the stable
/// client performs for its local maps.
/// </summary>
/// <remarks>
/// <para>
/// Request (query string): <c>u</c> and <c>h</c> hold the username and password MD5. The JSON body
/// lists the local files the client wants to know about:
/// </para>
/// <code>
/// {"Filenames":["Artist - Title (Mapper) [Version].osu", ...],"Ids":[1234567, ...]}
/// </code>
/// <para>
/// Response (<c>text/plain</c>), one line per known beatmap, terminated by <c>\n</c>:
/// </para>
/// <code>
/// &lt;index in Filenames&gt;|&lt;beatmap id&gt;|&lt;beatmap set id&gt;|&lt;md5&gt;|&lt;status&gt;|&lt;osu grade&gt;|&lt;taiko grade&gt;|&lt;catch grade&gt;|&lt;mania grade&gt;
/// </code>
/// <para>
/// The index keeps the client's request order, a grade of <c>N</c> means the mode was never played,
/// and a beatmap the server cannot resolve is skipped entirely - the client applies each line to the
/// index it carries, so a gap is harmless.
/// </para>
/// <para>
/// The status returned here is what tells the client a map is ranked, which in turn enables score
/// submission after a play; a missing endpoint leaves every map "not submitted" and the results
/// screen reports "score submission disabled".
/// </para>
/// </remarks>
/// <param name="authenticator">Authenticates the <c>u</c>/<c>h</c> query parameters.</param>
/// <param name="beatmaps">Resolves a requested beatmap by ID without leaving the database.</param>
/// <param name="fetcher">Resolves local <c>.osu</c> filenames through the osu! API.</param>
/// <param name="scores">Reads the player's grades per ruleset.</param>
/// <param name="logger">The logger.</param>
public sealed class BeatmapInfoRouteHandler(
    StableWebAuthenticator authenticator,
    IBeatmapRepository beatmaps,
    IFetcher fetcher,
    IScoreRepository scores,
    ILogger<BeatmapInfoRouteHandler> logger) : IRouteHandler
{
    private const int RulesetCount = 4;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    /// <inheritdoc />
    public async Task<IResult> HandleAsync(HttpContext context)
    {
        User? user = await authenticator.AuthenticateAsync(
            context.Request.Query["u"],
            context.Request.Query["h"],
            context.RequestAborted).ConfigureAwait(false);
        if (user is null)
        {
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }

        BeatmapInfoRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<BeatmapInfoRequest>(context.Request.Body, JsonOptions, context.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return Results.StatusCode(StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.StatusCode(StatusCodes.Status400BadRequest);
        }

        logger.LogDebug(
            "Resolving {FilenameCount} filename(s) and {IdCount} id(s) for user {UserId}.",
            request.Filenames.Count,
            request.Ids.Count,
            user.Id);
        List<string> lines = new(request.Filenames.Count);
        for (int index = 0; index < request.Filenames.Count; index++)
        {
            Beatmap? beatmap = await ResolveAsync(request, index).ConfigureAwait(false);
            if (beatmap is null || string.IsNullOrEmpty(beatmap.Checksum))
            {
                // Unknown maps are skipped; the client keys every line by its index, so a gap is fine.
                continue;
            }

            string[] grades = await GetGradesAsync(user.Id, beatmap.Id).ConfigureAwait(false);
            lines.Add(string.Join(
                '|',
                index.ToString(CultureInfo.InvariantCulture),
                beatmap.Id.ToString(CultureInfo.InvariantCulture),
                beatmap.BeatmapSetId.ToString(CultureInfo.InvariantCulture),
                beatmap.Checksum,
                StableWebFormat.ToRankStatus(beatmap.Status).ToString(CultureInfo.InvariantCulture),
                string.Join('|', grades)));
        }

        logger.LogDebug("Answered {KnownCount} of {RequestedCount} beatmap info entries.", lines.Count, request.Filenames.Count);
        return Results.Text(string.Join('\n', lines));
    }

    /// <summary>
    /// Resolves one requested beatmap, preferring the ID the client already knows.
    /// </summary>
    /// <param name="request">The parsed request body.</param>
    /// <param name="index">The position being resolved.</param>
    /// <returns>The stored beatmap, or <see langword="null"/> when it cannot be resolved.</returns>
    /// <remarks>
    /// The ID lookup is a single indexed query, while the filename path costs two API calls, so the
    /// client is always asked for its IDs first and the filename is only used as a fallback for maps
    /// the client has but the server does not.
    /// </remarks>
    private async Task<Beatmap?> ResolveAsync(BeatmapInfoRequest request, int index)
    {
        if (index < request.Ids.Count && request.Ids[index] > 0)
        {
            Beatmap? stored = await beatmaps.GetByIdAsync(request.Ids[index]).ConfigureAwait(false);
            if (stored is not null)
            {
                return stored;
            }
        }

        if (index >= request.Filenames.Count)
        {
            return null;
        }

        try
        {
            return await fetcher.FetchBeatmapByFilenameAsync(request.Filenames[index]).ConfigureAwait(false);
        }
        catch (FetcherException exception)
        {
            // A map the osu! API no longer knows is simply not reported back to the client.
            logger.LogDebug(exception, "Beatmap info lookup for {Filename} failed.", request.Filenames[index]);
            return null;
        }
    }

    private async Task<string[]> GetGradesAsync(int userId, int beatmapId)
    {
        string[] grades = new string[RulesetCount];
        Array.Fill(grades, StableWebFormat.ToGradeLetter(rank: null));
        IReadOnlyList<Score> played = await scores.GetByBeatmapIdAsync(beatmapId).ConfigureAwait(false);
        foreach (IGrouping<int, Score> mode in played.Where(score => score.UserId == userId && score.Mode is >= 0 and < RulesetCount).GroupBy(score => score.Mode))
        {
            ScoreRank best = mode.Select(score => score.Rank).OrderBy(rank => rank).First();
            grades[mode.Key] = StableWebFormat.ToGradeLetter(best);
        }

        return grades;
    }

    /// <summary>
    /// Deserializes the JSON body the stable client sends.
    /// </summary>
    public sealed class BeatmapInfoRequest
    {
        /// <summary>
        /// Gets or sets the local <c>.osu</c> filenames.
        /// </summary>
        [JsonPropertyName("Filenames")]
        public IList<string> Filenames { get; set; } = [];

        /// <summary>
        /// Gets or sets the beatmap IDs the client happens to know.
        /// </summary>
        [JsonPropertyName("Ids")]
        public IList<int> Ids { get; set; } = [];
    }
}
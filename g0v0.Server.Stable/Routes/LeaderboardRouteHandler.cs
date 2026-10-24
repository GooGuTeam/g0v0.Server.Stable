// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Fetching;
using g0v0.Server.Common.Rulesets;
using g0v0.Server.Common.Utils;
using g0v0.Server.Stable.Services;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Online.API;
using osu.Game.Rulesets.Mods;
using Beatmap = g0v0.Server.Common.Database.Models.Beatmap;

namespace g0v0.Server.Stable.Routes;

/// <summary>
/// Serves <c>GET /web/osu-osz2-getscores.php</c>: the beatmap leaderboard the stable client shows on
/// the song select screen.
/// </summary>
/// <remarks>
/// <para>
/// Request (query string, all values URL-encoded by the client):
/// </para>
/// <code>
/// GET /web/osu-osz2-getscores.php
///     ?c=&lt;beatmap md5&gt;    beatmap checksum
///     &amp;f=&lt;filename&gt;      local .osu filename
///     &amp;m=&lt;0-3&gt;           vanilla ruleset
///     &amp;i=&lt;beatmap set id&gt;
///     &amp;mods=&lt;bitmask&gt;     LegacyMods of the selected mods
///     &amp;s=&lt;0|1&gt;           1 when the request comes from the editor
///     &amp;v=&lt;0-4&gt;           0 global, 2 mods, 3 friends, 4 country
///     &amp;a=&lt;0|1&gt;
///     &amp;us=&amp;ha=            username and password MD5
///     &amp;vv=&amp;v=              client protocol version
/// </code>
/// <para>
/// Response (<c>text/plain</c>, lines separated by <c>\n</c>):
/// </para>
/// <code>
/// &lt;status&gt;|&lt;has_osz2&gt;|&lt;beatmap_id&gt;|&lt;beatmap_set_id&gt;|&lt;score_count&gt;|&lt;featured_artist&gt;|&lt;license&gt;
/// &lt;offset&gt;
/// &lt;artist&gt; - &lt;title&gt; [&lt;version&gt;]
/// &lt;star rating, two decimals&gt;
/// &lt;personal best row, empty line when unplayed&gt;
/// &lt;score row&gt;            one per leaderboard entry, best first
/// ...
/// </code>
/// <para>
/// Each score row is the pipe-delimited form produced by <see cref="StableWebFormat.FormatScoreRow"/>.
/// The two degenerate replies are <c>-1|false</c> when the osu! API does not know the beatmap and
/// <c>1|false</c> when only its filename does (the client then offers to update it); a map without a
/// ranked leaderboard answers <c>&lt;status&gt;|false</c>.
/// </para>
/// <para>
/// Only stable scores are returned: a legacy score is the one that carries a
/// <c>classic_total_score</c>, which is the value the stable client displays and sorts by.
/// </para>
/// </remarks>
/// <param name="authenticator">Authenticates the <c>us</c>/<c>ha</c> query parameters.</param>
/// <param name="fetcher">Resolves beatmaps by checksum and by filename.</param>
/// <param name="beatmaps">Reads beatmap metadata.</param>
/// <param name="beatmapSets">Reads the artist, title and status of the beatmap set.</param>
/// <param name="scores">Reads the leaderboard rows.</param>
/// <param name="relationships">Resolves the friends leaderboard.</param>
/// <param name="users">Resolves the display names of the leaderboard owners.</param>
/// <param name="rulesets">Converts the reported mod bitmask to the stored mods.</param>
/// <param name="configs">Reads the gameplay feature flags.</param>
/// <param name="logger">The logger.</param>
public sealed class LeaderboardRouteHandler(
    StableWebAuthenticator authenticator,
    IFetcher fetcher,
    IBeatmapRepository beatmaps,
    IBeatmapSetRepository beatmapSets,
    IScoreRepository scores,
    IRelationshipRepository relationships,
    IUserRepository users,
    RulesetManager rulesets,
    ConfigManager configs,
    ILogger<LeaderboardRouteHandler> logger) : IRouteHandler
{
    private const int DefaultLimit = 50;

    private const int ModsLeaderboard = 2;

    private const int FriendsLeaderboard = 3;

    private const int CountryLeaderboard = 4;

    /// <summary>
    /// The lowest client status that has a leaderboard (2 = ranked).
    /// </summary>
    private const int RankedStatus = 2;

    private readonly GameConfig _game = configs.Get<GameConfig>();

    /// <inheritdoc />
    public async Task<IResult> HandleAsync(HttpContext context)
    {
        User? user = await authenticator.AuthenticateAsync(
            context.Request.Query["us"],
            context.Request.Query["ha"],
            context.RequestAborted).ConfigureAwait(false);
        if (user is null)
        {
            logger.LogDebug("Rejected a leaderboard request with invalid credentials.");
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }

        string mapMd5 = context.Request.Query["c"].ToString();
        if (mapMd5.Length != 32 || !mapMd5.All(char.IsAsciiHexDigit))
        {
            logger.LogDebug("Rejected a leaderboard request with the malformed checksum {Checksum}.", mapMd5);
            return Results.Text("-1|false");
        }

        // ASP.NET already percent- and plus-decodes query values; decoding again would corrupt map
        // names that contain a literal '%' or '+'.
        string filename = context.Request.Query["f"].ToString().Trim();
        int modeArg = Clamp(ParseInt(context.Request.Query["m"]), 0, 3);
        int leaderboardType = Clamp(ParseInt(context.Request.Query["v"]), 0, 4);
        LegacyMods legacyMods = (LegacyMods)ParseInt(context.Request.Query["mods"]);
        bool fromEditor = ParseInt(context.Request.Query["s"]) == 1;

        Beatmap? beatmap = await FindBeatmapAsync(mapMd5, context.RequestAborted).ConfigureAwait(false);
        if (beatmap is null)
        {
            bool known = await ExistsByFilenameAsync(filename).ConfigureAwait(false);
            logger.LogDebug(
                "Leaderboard request for unknown checksum {Checksum} (filename {Filename} known: {IsKnown}).",
                mapMd5,
                filename,
                known);
            return Results.Text(known ? "1|false" : "-1|false");
        }

        int status = StableWebFormat.ToRankStatus(beatmap.Status);
        if (status < RankedStatus)
        {
            logger.LogDebug(
                "Leaderboard request for beatmap {BeatmapId} answered with the unranked status {Status}.",
                beatmap.Id,
                status);
            return Results.Text(string.Create(CultureInfo.InvariantCulture, $"{status}|false"));
        }

        int mode = ResolveMode(modeArg, legacyMods);
        BeatmapSet? set = await beatmapSets.GetByIdAsync(beatmap.BeatmapSetId).ConfigureAwait(false);

        List<Score> leaderboard = [];
        Score? personalBest = null;
        int personalBestRank = 0;
        int count = 0;
        if (!fromEditor)
        {
            (IReadOnlyCollection<int>? userIds, string? country) = leaderboardType switch
            {
                FriendsLeaderboard => (await GetFriendIdsAsync(user).ConfigureAwait(false), null),
                CountryLeaderboard => ((IReadOnlyCollection<int>?)null, user.CountryCode),
                _ => (null, null)
            };
            IReadOnlyCollection<string>? modFilter = leaderboardType == ModsLeaderboard
                ? ModAcronyms(mode, legacyMods)
                : null;

            IReadOnlyList<Score> rows = await scores.GetBeatmapLeaderboardAsync(
                beatmap.Id,
                mode,
                DefaultLimit,
                legacyOnly: true,
                userIds,
                country,
                modFilter,
                context.RequestAborted).ConfigureAwait(false);
            leaderboard = rows.ToList();
            count = leaderboard.Count;
            personalBest = await scores.GetBestBeatmapScoreAsync(
                beatmap.Id,
                mode,
                user.Id,
                legacyOnly: true,
                context.RequestAborted).ConfigureAwait(false);
            if (personalBest is not null)
            {
                personalBestRank = await scores.GetBeatmapLeaderboardRankAsync(
                    beatmap.Id,
                    mode,
                    user.Id,
                    legacyOnly: true,
                    context.RequestAborted).ConfigureAwait(false);
            }
        }

        Dictionary<int, string> names = await GetDisplayNamesAsync(leaderboard, personalBest, user).ConfigureAwait(false);
        List<string> lines =
        [
            string.Create(CultureInfo.InvariantCulture, $"{status}|false|{beatmap.Id}|{beatmap.BeatmapSetId}|{count}|0|"),
            "0",
            string.Create(CultureInfo.InvariantCulture, $"{set?.Artist ?? string.Empty} - {set?.Title ?? string.Empty} [{beatmap.Version}]"),
            beatmap.DifficultyRating.ToString("0.##", CultureInfo.InvariantCulture),
            personalBest is null
                ? string.Empty
                : StableWebFormat.FormatScoreRow(personalBest, NameOf(names, personalBest.UserId), personalBestRank, ModsOf(personalBest))
        ];

        lines.AddRange(leaderboard.Select((score, index) => StableWebFormat.FormatScoreRow(
            score,
            NameOf(names, score.UserId),
            index + 1,
            ModsOf(score))));

        return Results.Text(string.Join('\n', lines));
    }

    private static int ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;

    private static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);

    private static string NameOf(Dictionary<int, string> names, int userId) =>
        names.TryGetValue(userId, out string? name) ? name : string.Empty;

    private LegacyMods ModsOf(Score score) => StableWebFormat.ToLegacyMods(rulesets, score.Mode, score.Mods);

    private async Task<int[]> GetFriendIdsAsync(User user)
    {
        int[] friends = await relationships.GetAllFriendIds(user.Id).ConfigureAwait(false);
        return friends.Append(user.Id).Distinct().ToArray();
    }

    private async Task<Dictionary<int, string>> GetDisplayNamesAsync(IReadOnlyList<Score> leaderboard, Score? personalBest, User requester)
    {
        List<int> ids = leaderboard.Select(score => score.UserId)
            .Concat(personalBest is null ? [] : [personalBest.UserId])
            .Distinct()
            .ToList();
        Dictionary<int, string> names = new() { [requester.Id] = requester.Username };
        if (ids.Count == 0)
        {
            return names;
        }

        IReadOnlyList<User> loaded = await users.GetByIdsAsync(ids).ConfigureAwait(false);
        foreach (User owner in loaded)
        {
            names[owner.Id] = owner.Username;
        }

        return names;
    }

    private string[]? ModAcronyms(int mode, LegacyMods legacyMods)
    {
        try
        {
            return rulesets.GetRuleset(ScoreModeUtils.GetBaseRulesetId(mode))
                .ConvertFromLegacyMods(legacyMods)
                .Select(mod => mod.Acronym)
                .ToArray();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Maps the reported mode and mod bitmask to the stored ruleset ID.
    /// </summary>
    /// <param name="modeArg">The vanilla mode the client is in.</param>
    /// <param name="legacyMods">The reported legacy mods.</param>
    /// <returns>The stored ruleset ID.</returns>
    private int ResolveMode(int modeArg, LegacyMods legacyMods)
    {
        Mod[] converted;
        try
        {
            converted = rulesets.GetRuleset(modeArg).ConvertFromLegacyMods(legacyMods).ToArray();
        }
        catch (ArgumentException)
        {
            return modeArg;
        }

        return ScoreModeUtils.GetScoreMode(modeArg, converted.Select(mod => new APIMod(mod)), _game);
    }

    private async Task<Beatmap?> FindBeatmapAsync(string checksum, CancellationToken cancellationToken)
    {
        try
        {
            // The repository already owns the read-then-fetch flow, including persistence, so the
            // route only has to translate a lookup failure into the protocol's "unknown map" reply.
            return await beatmaps.GetOrFetchByChecksumAsync(checksum, fetcher, cancellationToken).ConfigureAwait(false);
        }
        catch (FetcherException exception)
        {
            logger.LogDebug(exception, "Leaderboard lookup for checksum {Checksum} failed.", checksum);
            return null;
        }
    }

    private async Task<bool> ExistsByFilenameAsync(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return false;
        }

        try
        {
            return await fetcher.FetchBeatmapByFilenameAsync(filename).ConfigureAwait(false) is not null;
        }
        catch (FetcherException)
        {
            return false;
        }
    }
}
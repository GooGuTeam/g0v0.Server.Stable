// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text;
using g0v0.Server.Common.Beatmaps;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Fetching;
using g0v0.Server.Common.Http;
using g0v0.Server.Stable.Services;
using Newtonsoft.Json;

namespace g0v0.Server.Stable.Routes;

/// <summary>Serves authenticated osu!direct searches in pages of 100 sets.</summary>
/// <param name="authenticator">Authenticates the u/h credentials.</param>
/// <param name="search">The shared cached search service.</param>
/// <param name="scores">Reads local play history for the played category.</param>
/// <param name="logger">Search failure diagnostics.</param>
public sealed class SearchRouteHandler(
    StableWebAuthenticator authenticator,
    IBeatmapSearchService search,
    IScoreRepository scores,
    ILogger<SearchRouteHandler> logger) : IRouteHandler
{
    private const int PageSize = 100;
    private const int MaximumUpstreamPages = 200;

    /// <inheritdoc/>
    public async Task<IResult> HandleAsync(HttpContext context)
    {
        IQueryCollection parameters = context.Request.Query;
        User? user = await authenticator.AuthenticateAsync(parameters["u"], parameters["h"], context.RequestAborted).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (!ReadInt(parameters, "r", 4, out int status) || status is < 0 or > 8
            || !ReadInt(parameters, "m", -1, out int mode) || mode is < -1 or > 3
            || !ReadInt(parameters, "p", 0, out int page) || page is < 0 or > 99)
        {
            return Results.BadRequest();
        }

        string keywords = parameters["q"].ToString().Trim();
        string sort = keywords switch
        {
            "Newest" => "updated_desc",
            "Top Rated" or "Top+Rated" => "rating_desc",
            "Most Played" or "Most+Played" => "plays_desc",
            "" => "ranked_desc",
            _ => "relevance_desc"
        };
        if (keywords is "Newest" or "Top Rated" or "Top+Rated" or "Most Played" or "Most+Played")
        {
            keywords = string.Empty;
        }

        if (status is 1 or 6)
        {
            keywords = (keywords + (status == 1 ? " status=ranked" : " status=approved")).Trim();
        }

        BeatmapSearchQuery query = new()
        {
            Query = keywords,
            Status = status switch
            {
                0 or 1 or 6 or 7 => "ranked",
                2 => "pending",
                3 => "qualified",
                5 => "graveyard",
                8 => "loved",
                _ => "any"
            },
            Sort = sort,
            Mode = mode == -1 ? null : mode,
            IncludeExplicitContent = true
        };

        try
        {
            HashSet<int>? played = status == 7
                ? (await scores.GetByUserIdAsync(user.Id).ConfigureAwait(false)).Select(score => score.BeatmapId).ToHashSet()
                : null;
            if (played is { Count: 0 })
            {
                return Results.Text("0");
            }

            (List<BeatmapSet> sets, bool more) = await GetPageAsync(query, page, played, context.RequestAborted).ConfigureAwait(false);
            List<string> lines = [(more ? PageSize + 1 : sets.Count).ToString(CultureInfo.InvariantCulture)];
            lines.AddRange(sets.Select(set => StableWebFormat.FormatDirectSet(set, includeDifficulties: true)));
            return Results.Text(string.Join('\n', lines), "text/plain", Encoding.UTF8);
        }
        catch (Exception exception) when (exception is FetcherException or HttpServiceException or HttpRequestException or JsonException
            || (exception is OperationCanceledException && !context.RequestAborted.IsCancellationRequested))
        {
            logger.LogWarning(exception, "osu!direct search failed for user {UserId}.", user.Id);
            return Results.Text("-1\nFailed to retrieve data from the beatmap server.");
        }
    }

    private static bool ReadInt(IQueryCollection query, string key, int fallback, out int value)
    {
        value = fallback;
        return !query.ContainsKey(key) || int.TryParse(query[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private async Task<(List<BeatmapSet> Sets, bool More)> GetPageAsync(BeatmapSearchQuery query, int page, HashSet<int>? played, CancellationToken cancellationToken)
    {
        int skip = page * PageSize;
        List<BeatmapSet> sets = [];
        HashSet<int> seenSets = [];
        HashSet<string> cursors = new(StringComparer.Ordinal);
        string? cursor = null;
        for (int upstreamPage = 0; upstreamPage < MaximumUpstreamPages; upstreamPage++)
        {
            BeatmapSearchResult result = await search.SearchAsync(query, cursor, cancellationToken).ConfigureAwait(false);
            IEnumerable<BeatmapSet> candidates = result.BeatmapSets.Where(set => seenSets.Add(set.Id) && set.Beatmaps.Count > 0
                && (played is null || set.Beatmaps.Any(map => played.Contains(map.Id) && (query.Mode is null || map.Mode == query.Mode))));
            foreach (BeatmapSet set in candidates)
            {
                if (skip > 0)
                {
                    skip--;
                }
                else if (sets.Count < PageSize)
                {
                    sets.Add(set);
                }
                else
                {
                    return (sets, true);
                }
            }

            if (!result.HasMore)
            {
                return (sets, false);
            }

            if (!cursors.Add(result.Cursor!))
            {
                throw new FetcherException("The beatmap search cursor did not advance.");
            }

            if (sets.Count == PageSize)
            {
                return (sets, true);
            }

            cursor = result.Cursor;
        }

        throw new FetcherException("The requested beatmap search page exceeds the upstream traversal limit.");
    }
}
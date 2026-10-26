// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text;
using g0v0.Server.Common.Beatmaps;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Fetching;
using g0v0.Server.Common.Http;
using g0v0.Server.Common.Utils;
using g0v0.Server.Stable.Services;
using Newtonsoft.Json;

namespace g0v0.Server.Stable.Routes;

/// <summary>Returns one osu!direct set record by set ID, beatmap ID, or checksum.</summary>
/// <param name="authenticator">Authenticates the u/h credentials.</param>
/// <param name="search">The shared metadata lookup service.</param>
/// <param name="logger">Lookup failure diagnostics.</param>
public sealed class SearchSetRouteHandler(
    StableWebAuthenticator authenticator,
    IBeatmapSearchService search,
    ILogger<SearchSetRouteHandler> logger) : IRouteHandler
{
    /// <inheritdoc/>
    public async Task<IResult> HandleAsync(HttpContext context)
    {
        IQueryCollection query = context.Request.Query;
        if (await authenticator.AuthenticateAsync(query["u"], query["h"], context.RequestAborted).ConfigureAwait(false) is null)
        {
            return Results.Unauthorized();
        }

        int? setId = null;
        int? beatmapId = null;
        string? checksum = null;
        if (query.ContainsKey("s"))
        {
            if (!int.TryParse(query["s"], NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id <= 0)
            {
                return Results.Text(string.Empty);
            }

            setId = id;
        }
        else if (query.ContainsKey("b"))
        {
            if (!int.TryParse(query["b"], NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id <= 0)
            {
                return Results.Text(string.Empty);
            }

            beatmapId = id;
        }
        else if (UserPasswordHasher.IsValidDigest(query["c"]))
        {
            checksum = query["c"].ToString().ToLowerInvariant();
        }
        else
        {
            return Results.Text(string.Empty);
        }

        try
        {
            BeatmapSet? set = await search.LookupAsync(setId, beatmapId, checksum, context.RequestAborted).ConfigureAwait(false);
            return Results.Text(set is null ? string.Empty : StableWebFormat.FormatDirectSet(set, includeDifficulties: false), "text/plain", Encoding.UTF8);
        }
        catch (Exception exception) when (exception is FetcherException or HttpServiceException or HttpRequestException or JsonException
            || (exception is OperationCanceledException && !context.RequestAborted.IsCancellationRequested))
        {
            logger.LogWarning(exception, "osu!direct set lookup failed.");
            return Results.Text(string.Empty);
        }
    }
}
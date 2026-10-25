// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Scoring;
using g0v0.Server.Stable.Services;

namespace g0v0.Server.Stable.Routes;

/// <summary>Returns compressed replay frames for the score c in ruleset m.</summary>
/// <param name="authenticator">Authenticates the u/h credentials.</param>
/// <param name="replays">Reads the shared .osr storage.</param>
public sealed class ReplayRouteHandler(StableWebAuthenticator authenticator, ReplayService replays) : IRouteHandler
{
    /// <inheritdoc/>
    public async Task<IResult> HandleAsync(HttpContext context)
    {
        IQueryCollection query = context.Request.Query;
        User? user = await authenticator.AuthenticateAsync(query["u"], query["h"], context.RequestAborted).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (!long.TryParse(query["c"], NumberStyles.None, CultureInfo.InvariantCulture, out long scoreId) || scoreId <= 0 ||
            !int.TryParse(query["m"], NumberStyles.None, CultureInfo.InvariantCulture, out int mode) || mode is < 0 or > 3)
        {
            return Results.BadRequest();
        }

        byte[]? replay = await replays.GetRawReplayAsync(scoreId, mode, context.RequestAborted).ConfigureAwait(false);
        return replay is null ? Results.NotFound() : Results.Bytes(replay, "application/octet-stream");
    }
}
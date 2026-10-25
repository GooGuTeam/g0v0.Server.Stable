// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using g0v0.Server.Common.Beatmaps;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Utils;
using g0v0.Server.Stable.Services;

namespace g0v0.Server.Stable.Routes;

/// <summary>Serves the stable rating check and vote protocol using set-wide lazer ratings.</summary>
/// <param name="authenticator">Authenticates the u/p credentials.</param>
/// <param name="feedback">The shared rating service.</param>
public sealed class RatingRouteHandler(StableWebAuthenticator authenticator, BeatmapFeedbackService feedback) : IRouteHandler
{
    /// <inheritdoc/>
    public async Task<IResult> HandleAsync(HttpContext context)
    {
        IQueryCollection query = context.Request.Query;
        User? user = await authenticator.AuthenticateAsync(query["u"], query["p"], context.RequestAborted).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Text("auth fail", statusCode: StatusCodes.Status401Unauthorized);
        }

        string checksum = query["c"].ToString();
        int? rating = null;
        if (!UserPasswordHasher.IsValidDigest(checksum))
        {
            return Results.BadRequest();
        }

        if (query.ContainsKey("v"))
        {
            if (!int.TryParse(query["v"], NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value is < 1 or > 10)
            {
                return Results.BadRequest();
            }

            rating = value;
        }

        BeatmapRatingResult result = await feedback.RateAsync(user.Id, checksum.ToLowerInvariant(), rating, context.RequestAborted).ConfigureAwait(false);
        return Results.Text(result.Status switch
        {
            BeatmapRatingStatus.NotFound => "no exist",
            BeatmapRatingStatus.NotRanked => "not ranked",
            BeatmapRatingStatus.CanRate => "ok",
            _ => "alreadyvoted\n" + result.AverageRating.ToString(CultureInfo.InvariantCulture),
        });
    }
}
// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using g0v0.Server.Common.Beatmaps;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Fetching;
using g0v0.Server.Stable.Services;

namespace g0v0.Server.Stable.Routes;

/// <summary>Adds the set identified by a to the authenticated user's favourites.</summary>
/// <param name="authenticator">Authenticates the u/h credentials.</param>
/// <param name="feedback">The shared favourites service.</param>
public sealed class AddFavouriteRouteHandler(StableWebAuthenticator authenticator, BeatmapFeedbackService feedback) : IRouteHandler
{
    /// <inheritdoc/>
    public async Task<IResult> HandleAsync(HttpContext context)
    {
        User? user = await authenticator.AuthenticateAsync(context.Request.Query["u"], context.Request.Query["h"], context.RequestAborted).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (!int.TryParse(context.Request.Query["a"], NumberStyles.None, CultureInfo.InvariantCulture, out int setId) || setId <= 0)
        {
            return Results.BadRequest();
        }

        try
        {
            bool added = await feedback.AddFavouriteAsync(user.Id, setId, context.RequestAborted).ConfigureAwait(false);
            return Results.Text(added ? "Added favourite!" : "You've already favourited this beatmap!");
        }
        catch (FetcherException)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }
}
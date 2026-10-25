// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Stable.Services;

namespace g0v0.Server.Stable.Routes;

/// <summary>Returns the user's favourite beatmap set IDs, one per line.</summary>
/// <param name="authenticator">Authenticates the u/h credentials.</param>
/// <param name="feedback">The shared feedback repository.</param>
public sealed class FavouritesRouteHandler(StableWebAuthenticator authenticator, IBeatmapFeedbackRepository feedback) : IRouteHandler
{
    /// <inheritdoc/>
    public async Task<IResult> HandleAsync(HttpContext context)
    {
        User? user = await authenticator.AuthenticateAsync(context.Request.Query["u"], context.Request.Query["h"], context.RequestAborted).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        IReadOnlyList<int> favourites = await feedback.GetFavouritesAsync(user.Id, context.RequestAborted).ConfigureAwait(false);
        return Results.Text(string.Join('\n', favourites.Select(id => id.ToString(CultureInfo.InvariantCulture))));
    }
}
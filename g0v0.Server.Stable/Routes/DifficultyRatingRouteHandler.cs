// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Stable.Routes;

/// <summary>Forwards difficulty calculation to osu! while preserving the POST method and body.</summary>
public sealed class DifficultyRatingRouteHandler : IRouteHandler
{
    /// <inheritdoc/>
    public Task<IResult> HandleAsync(HttpContext context)
        => Task.FromResult(Results.Redirect("https://osu.ppy.sh/difficulty-rating", preserveMethod: true));
}
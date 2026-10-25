// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Configuration;

namespace g0v0.Server.Stable.Routes;

/// <summary>Serves the unauthenticated seasonal background URL array.</summary>
/// <param name="configs">The shared game configuration.</param>
public sealed class SeasonalRouteHandler(ConfigManager configs) : IRouteHandler
{
    /// <inheritdoc/>
    public Task<IResult> HandleAsync(HttpContext context)
        => Task.FromResult(Results.Json((configs.TryGet<GameConfig>() ?? new GameConfig()).SeasonalBackgrounds));
}
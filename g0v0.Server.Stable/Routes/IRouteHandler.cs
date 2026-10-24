// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Stable.Routes;

/// <summary>
/// Handles an HTTP request to a stable protocol endpoint.
/// </summary>
public interface IRouteHandler
{
    /// <summary>
    /// Processes the request and produces its HTTP response.
    /// </summary>
    /// <param name="context">The current request context.</param>
    /// <returns>The endpoint response.</returns>
    Task<IResult> HandleAsync(HttpContext context);
}
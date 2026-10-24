// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Configuration;

namespace g0v0.Server.Stable;

/// <summary>
/// Resolves configuration and local data relative to the host content root.
/// </summary>
/// <param name="environment">The host environment.</param>
public class WebPathProvider(IHostEnvironment environment) : IPathProvider
{
    /// <inheritdoc />
    public string GetBasePath() => environment.ContentRootPath;
}
// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using g0v0.Server.Common.Beatmaps;

namespace g0v0.Server.Stable.Routes;

/// <summary>Redirects archive downloads, accepting stable's n suffix for video-free sets.</summary>
/// <param name="downloads">The shared mirror selector.</param>
public sealed class BeatmapDownloadRouteHandler(BeatmapDownloadService downloads) : IRouteHandler
{
    /// <inheritdoc/>
    public Task<IResult> HandleAsync(HttpContext context)
    {
        string value = context.Request.RouteValues["map_set_id"]?.ToString() ?? string.Empty;
        bool noVideo = value.EndsWith('n');
        string id = noVideo ? value[..^1] : value;
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out int setId) || setId <= 0)
        {
            return Task.FromResult(Results.BadRequest());
        }

        string url = downloads.GetDownloadUrl(setId, noVideo, context.Connection.RemoteIpAddress);
        return Task.FromResult(Results.Redirect(url));
    }
}
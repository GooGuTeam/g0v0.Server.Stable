// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Fetching;

namespace g0v0.Server.Stable.Routes;

/// <summary>Returns an updated .osu file identified by its full filename.</summary>
/// <param name="fetcher">Resolves the filename and fetches the raw beatmap with mirror fallback.</param>
public sealed class BeatmapFileRouteHandler(IFetcher fetcher) : IRouteHandler
{
    /// <inheritdoc/>
    public async Task<IResult> HandleAsync(HttpContext context)
    {
        string filename = context.Request.RouteValues["map_filename"]?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(filename) || !filename.EndsWith(".osu", StringComparison.OrdinalIgnoreCase) ||
            filename.IndexOfAny(['/', '\\', '\0', '\r', '\n']) >= 0)
        {
            return Results.BadRequest();
        }

        try
        {
            Beatmap? beatmap = await fetcher.FetchBeatmapByFilenameAsync(filename, context.RequestAborted).ConfigureAwait(false);
            if (beatmap is null)
            {
                return Results.NotFound();
            }

            string content = await fetcher.FetchBeatmapRawAsync(beatmap.Id, context.RequestAborted).ConfigureAwait(false);
            return Results.Text(content, "text/plain", Encoding.UTF8);
        }
        catch (FetcherException)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }
}
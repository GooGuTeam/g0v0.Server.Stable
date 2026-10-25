// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Screenshots;
using g0v0.Server.Stable.Services;
using Microsoft.AspNetCore.Http.Features;

namespace g0v0.Server.Stable.Routes;

/// <summary>Accepts stable multipart screenshot uploads and returns their share identifier.</summary>
/// <param name="authenticator">Authenticates the u/p form credentials.</param>
/// <param name="screenshots">The shared screenshot service.</param>
public sealed class ScreenshotRouteHandler(StableWebAuthenticator authenticator, ScreenshotService screenshots) : IRouteHandler
{
    private const int MaximumRequestSize = ScreenshotService.MaximumUploadSize + (64 * 1024);

    /// <inheritdoc/>
    public async Task<IResult> HandleAsync(HttpContext context)
    {
        if (context.Request.ContentLength > MaximumRequestSize)
        {
            return TooLarge();
        }

        if (!context.Request.HasFormContentType)
        {
            return Results.BadRequest();
        }

        IHttpMaxRequestBodySizeFeature? size = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (size is { IsReadOnly: false })
        {
            size.MaxRequestBodySize = MaximumRequestSize;
        }

        IFormCollection form;
        try
        {
            form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return Results.BadRequest();
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge();
        }

        // Stable URL-escapes this field even though the upload uses multipart form data.
        string username = Uri.UnescapeDataString(form["u"].ToString());
        User? user = await authenticator.AuthenticateAsync(username, form["p"], context.RequestAborted).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        IFormFile? file = form.Files.GetFile("ss");
        if (file is null || form.Files.Count != 1 || !int.TryParse(form["v"], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return Results.BadRequest();
        }

        if (file.Length > ScreenshotService.MaximumUploadSize)
        {
            return TooLarge();
        }

        using MemoryStream content = new();
        await file.CopyToAsync(content, context.RequestAborted).ConfigureAwait(false);
        string? hash = await screenshots.UploadAsync(user.Id, content.ToArray(), context.RequestAborted).ConfigureAwait(false);
        return hash is null ? Results.Text("Invalid file type", statusCode: StatusCodes.Status400BadRequest) : Results.Text(hash);
    }

    private static IResult TooLarge()
        => Results.Text("Screenshot file too large.", statusCode: StatusCodes.Status400BadRequest);
}
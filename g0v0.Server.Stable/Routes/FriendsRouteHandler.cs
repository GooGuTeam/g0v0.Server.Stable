// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Stable.Services;

namespace g0v0.Server.Stable.Routes;

/// <summary>
/// Serves <c>GET /web/osu-getfriends.php</c>: the friend IDs the stable client loads after logging in.
/// </summary>
/// <remarks>
/// <para>
/// Request (query string): <c>u</c> and <c>h</c> hold the username and password MD5.
/// </para>
/// <code>
/// GET /web/osu-getfriends.php?u=&lt;username&gt;&amp;h=&lt;password md5&gt;
/// </code>
/// <para>
/// Response (<c>text/plain</c>): one user ID per line, with no trailing newline.
/// </para>
/// <code>
/// 2
/// 3
/// 5
/// </code>
/// <para>
/// An empty body means the player has no friends. The client falls back to the <c>S83 FRIENDS_LIST</c>
/// packet it received at login when this request fails, so the endpoint is only needed to refresh
/// the list without reconnecting.
/// </para>
/// </remarks>
/// <param name="authenticator">Authenticates the <c>u</c>/<c>h</c> query parameters.</param>
/// <param name="relationships">Reads the follow relationships.</param>
/// <param name="logger">The logger.</param>
public sealed class FriendsRouteHandler(
    StableWebAuthenticator authenticator,
    IRelationshipRepository relationships,
    ILogger<FriendsRouteHandler> logger) : IRouteHandler
{
    /// <inheritdoc />
    public async Task<IResult> HandleAsync(HttpContext context)
    {
        User? user = await authenticator.AuthenticateAsync(
            context.Request.Query["u"],
            context.Request.Query["h"],
            context.RequestAborted).ConfigureAwait(false);
        if (user is null)
        {
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }

        int[] friends = await relationships.GetAllFriendIds(user.Id).ConfigureAwait(false);
        logger.LogDebug("Returning {FriendCount} friend(s) of user {UserId}.", friends.Length, user.Id);
        return Results.Text(string.Join('\n', friends.Select(id => id.ToString(CultureInfo.InvariantCulture))));
    }
}
// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text;

namespace g0v0.Server.Stable.Routes;

/// <summary>
/// Serves <c>GET /p/doyoureallywanttoaskpeppy</c>, the page the stable client opens when a player
/// tries to send a private message to the account with user ID 2.
/// </summary>
/// <remarks>
/// <para>
/// On official Bancho this page belongs to peppy and leads to the
/// <c>Do_you_really_want_to_ask_peppy</c> wiki article. g0v0! allocates user IDs starting from 4, so
/// no account owns ID 2 and the client cannot reach this endpoint normally; it is kept as an
/// easter egg for players who try anyway.
/// </para>
/// <para>
/// Response (<c>text/html</c>, UTF-8): the explanation page, including links to peppy's profile, the
/// wiki article, the g0v0! server, the Stable source code and the g0v0! project contacts.
/// </para>
/// </remarks>
public sealed class CallPeppyRouteHandler : IRouteHandler
{
    private const string Page = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Do you really want to ask peppy?</title>
        </head>
        <body>
        <h1>Do you really want to ask peppy?</h1>
        <p>Congratulations, you found an easter egg! 🎉</p>
        <p>In osu!(stable), this page is what you see when you send a private message to the player with user ID 2 &mdash; on Bancho, that is <a href="http://osu.ppy.sh/u/2">peppy</a> &mdash; and it actually leads to:</p>
        <p><a href="https://osu.ppy.sh/wiki/Do_you_really_want_to_ask_peppy">https://osu.ppy.sh/wiki/Do_you_really_want_to_ask_peppy</a></p>
        <p>In early versions of g0v0!, however, user ID 2 belonged to BanchoBot, which meant you could not start a private chat with the bot. We have fixed this in <a href="https://github.com/GooGuTeam/g0v0-server/pull/215">GooGuTeam/g0v0-server#215</a>.</p>
        <p>We now also allocate users starting from id=4, so no user has the id 2. That means you cannot reach this page from the client. We still implemented this endpoint as an easter egg, though.</p>
        <p>To fix this in g0v0!, we migrated the ID of the user with id=3 to the very end of the list and then gave the now-vacant id 3 to BanchoBot. On the <a href="http://osu.g0v0.top">g0v0! server</a>, id=3 belongs to MingxuanGame, the core developer of g0v0!, so I (the author of this page) made a certain sacrifice for this 😭.</p>
        <p>We are glad you were willing to explore this (or maybe you just tried it on a whim and found it :)).</p>
        <p>If you want to contact peppy, please visit the address above; if you encounter server problems, please report them to the server administrators; if you find server bugs, please report them to the server administrators or <a href="https://github.com/GooGuTeam">GooGuTeam</a>. If you want to contact me, you can reach me via <a href="https://github.com/MingxuanGame">GitHub</a> or <a href="mailto:MingxuanGame@outlook.com">email</a>.</p>
        <footer>
        <p>Server powered by g0v0.Server.Stable | <a href="https://github.com/GooGuTeam/g0v0.Server.Stable">Source code</a></p>
        </footer>
        </body>
        </html>
        """;

    /// <inheritdoc/>
    public Task<IResult> HandleAsync(HttpContext context)
        => Task.FromResult(Results.Content(Page, "text/html", Encoding.UTF8));
}
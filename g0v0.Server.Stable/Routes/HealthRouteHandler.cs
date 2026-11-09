// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Stable.Manager;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Routes;

/// <summary>Serves the unauthenticated health counters of the stable server.</summary>
/// <remarks>
/// The payload carries <c>identify</c> so a shared monitor can tell the stable
/// and realtime servers apart. The counters are read from the live in-memory
/// registries plus the durable pending-score queue, so no session or database
/// lookup beyond the queue count is required.
/// </remarks>
public sealed class HealthRouteHandler(
    IPlayerManager players,
    IRoomManager rooms,
    IScoreRepository scores) : IRouteHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <inheritdoc/>
    public async Task<IResult> HandleAsync(HttpContext context)
    {
        IReadOnlyList<IPlayer> sessions = players.GetAllPlayers();
        int pendingScores = await scores.CountUnprocessedScoresAsync("stable").ConfigureAwait(false);

        return Results.Json(
            new
            {
                identify = "stable",
                players_online = sessions.Select(session => session.Id).Distinct().Count(),
                active_sessions = sessions.Count,
                tournament_sessions = sessions.Count(session => session.IsTourneyClient),
                multiplayer_rooms = rooms.GetAllRooms().Count,
                pending_scores = pendingScores,
            },
            JsonOptions);
    }
}
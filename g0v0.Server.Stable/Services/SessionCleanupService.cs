// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Configuration;
using g0v0.Server.Stable.Manager;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Services;

/// <summary>Expires Stable sessions that stop polling and clears their online state and memberships.</summary>
/// <param name="players">The exact-session registry.</param>
/// <param name="chats">The chat memberships.</param>
/// <param name="scopes">Creates scopes for account persistence.</param>
/// <param name="configs">The optional Stable configuration.</param>
/// <param name="clock">The UTC clock and timer provider.</param>
/// <param name="logger">Cleanup diagnostics.</param>
public sealed class SessionCleanupService(
    IPlayerManager players,
    IChatServer chats,
    IServiceScopeFactory scopes,
    ConfigManager configs,
    TimeProvider clock,
    ILogger<SessionCleanupService> logger) : BackgroundService
{
    private readonly StableConfig _config = configs.TryGet<StableConfig>() ?? new StableConfig();

    /// <summary>Checks current inactivity and disconnects each expired exact session.</summary>
    /// <param name="cancellationToken">The shutdown token.</param>
    /// <returns>The number of expired sessions removed from the registry.</returns>
    public async Task<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_config.SessionTimeout, TimeSpan.Zero);
        DateTimeOffset cutoff = clock.GetUtcNow() - _config.SessionTimeout;
        int removed = 0;
        foreach (IPlayer player in players.GetAllPlayers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!players.RemoveInactivePlayer(player, cutoff))
            {
                continue;
            }

            removed++;
            try
            {
                await SessionLifecycle.DisconnectAsync(player, players, chats, scopes, clock, logger).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Could not finish cleanup of expired Stable session {UserId}.", player.Id);
            }
        }

        return removed;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_config.SessionCleanupInterval, TimeSpan.Zero);
                await Task.Delay(_config.SessionCleanupInterval, clock, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
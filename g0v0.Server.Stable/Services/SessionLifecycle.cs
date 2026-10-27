// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Stable.Manager;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Services;

internal static class SessionLifecycle
{
    internal static async Task DisconnectAsync(
        IPlayer player,
        IPlayerManager players,
        IChatServer chats,
        IServiceScopeFactory scopes,
        TimeProvider clock,
        ILogger? logger = null)
    {
        await chats.DisconnectAsync(player).ConfigureAwait(false);
        await player.Offline().ConfigureAwait(false);
        if (players.GetPlayerAllInstances(player.Id).Count != 0)
        {
            return;
        }

        using IServiceScope scope = scopes.CreateScope();
        logger ??= scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SessionLifecycle).FullName!);
        IUserRepository users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        User? user = await users.GetByIdAsync(player.Id).ConfigureAwait(false);
        if (user is null)
        {
            logger.LogWarning("Disconnected session {UserId} has no user row.", player.Id);
            return;
        }

        // A reconnect may have completed while the database request was in flight.
        if (players.GetPlayerAllInstances(player.Id).Count != 0)
        {
            return;
        }

        user.IsOnline = false;
        user.LastVisit = clock.GetUtcNow();
        await users.UpdateAsync(user).ConfigureAwait(false);
        logger.LogInformation("User {UserId} went offline at {LastVisit:O}.", user.Id, user.LastVisit);
    }
}
// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Manager;

public partial class PlayerManager
{
    /// <inheritdoc/>
    public bool RemoveInactivePlayer(IPlayer player, DateTimeOffset cutoff)
    {
        ArgumentNullException.ThrowIfNull(player);
        lock (_lock)
        {
            if (!IsRegistered(player) || player.LastReceiveTime >= cutoff)
            {
                return false;
            }

            RemovePlayerCore(player);
            return true;
        }
    }

    /// <inheritdoc/>
    public bool RefreshSession(IPlayer player, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(player);
        lock (_lock)
        {
            if (!IsRegistered(player))
            {
                return false;
            }

            player.LastReceiveTime = now;
            return true;
        }
    }
}
// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Objects.States;
using g0v0.Server.Stable.Packets.Server;
using osu.Game.Beatmaps.Legacy;

namespace g0v0.Server.Stable.Manager;

/// <summary>
/// Maintains session indexes and spectator relationships atomically.
/// </summary>
public class PlayerManager : IPlayerManager
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, IPlayer> _playersByToken = new(StringComparer.Ordinal);
    private readonly Dictionary<int, Dictionary<string, IPlayer>> _playersById = new();
    private readonly Dictionary<IPlayer, IPlayer> _spectating = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IPlayer, HashSet<IPlayer>> _spectators = new(ReferenceEqualityComparer.Instance);

    /// <inheritdoc />
    public void AddPlayer(IPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(player.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(player.Token);
        lock (_lock)
        {
            if (_playersByToken.TryGetValue(player.Token, out IPlayer? existing))
            {
                if (ReferenceEquals(existing, player))
                {
                    return;
                }

                throw new InvalidOperationException("The session token is already registered.");
            }

            if (player is Player concrete)
            {
                concrete.AttachManager(this);
            }

            if (!player.IsTourneyClient && _playersById.TryGetValue(player.Id, out Dictionary<string, IPlayer>? sessions))
            {
                IPlayer? previous = sessions.Values.FirstOrDefault(p => !p.IsTourneyClient);
                if (previous != null)
                {
                    RemovePlayerCore(previous, isKicked: true, announce: false);
                }
            }

            if (!_playersById.TryGetValue(player.Id, out Dictionary<string, IPlayer>? players))
            {
                players = new Dictionary<string, IPlayer>(StringComparer.Ordinal);
                _playersById.Add(player.Id, players);
            }

            _playersByToken.Add(player.Token, player);
            players.Add(player.Token, player);
            IPlayer visible = GetPlayer(player.Id)!;
            if (!visible.IsRestricted && ReferenceEquals(visible, player))
            {
                foreach (IPlayer other in _playersByToken.Values.Where(other => !ReferenceEquals(other, player)))
                {
                    other.OnPlayerOnline(player);
                }
            }

            foreach (IPlayer other in _playersById.Keys
                         .Where(id => id != player.Id)
                         .Select(id => GetPlayer(id)!)
                         .Where(other => !other.IsRestricted))
            {
                player.OnPlayerOnline(other);
            }
        }
    }

    /// <inheritdoc />
    public bool RemovePlayer(IPlayer player, bool isKicked = false)
    {
        ArgumentNullException.ThrowIfNull(player);
        lock (_lock)
        {
            if (!IsRegistered(player))
            {
                return false;
            }

            RemovePlayerCore(player, isKicked);
            return true;
        }
    }

    /// <inheritdoc />
    public IPlayer? GetPlayer(int id)
    {
        lock (_lock)
        {
            return _playersById.TryGetValue(id, out Dictionary<string, IPlayer>? players)
                ? players.Values.FirstOrDefault(p => !p.IsTourneyClient) ?? players.Values.FirstOrDefault()
                : null;
        }
    }

    /// <inheritdoc />
    public IPlayer? GetPlayer(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        lock (_lock)
        {
            return _playersByToken.GetValueOrDefault(token);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<IPlayer> GetPlayerAllInstances(int id)
    {
        lock (_lock)
        {
            return _playersById.TryGetValue(id, out Dictionary<string, IPlayer>? players) ? players.Values.ToArray() : [];
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<IPlayer> GetAllPlayers()
    {
        lock (_lock)
        {
            return _playersByToken.Values.ToArray();
        }
    }

    /// <inheritdoc />
    public void UserWatchingPlayer(IPlayer source, IPlayer target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        lock (_lock)
        {
            if (!IsRegistered(source) || !IsRegistered(target))
            {
                throw new InvalidOperationException("Both spectator sessions must be registered.");
            }

            if (source.IsRestricted || target.IsRestricted || ReferenceEquals(source, target))
            {
                throw new InvalidOperationException("This session cannot spectate the requested target.");
            }

            if (_spectating.TryGetValue(source, out IPlayer? current) && ReferenceEquals(current, target))
            {
                return;
            }

            StopWatching(source);
            _spectating.Add(source, target);
            if (!_spectators.TryGetValue(target, out HashSet<IPlayer>? watchers))
            {
                watchers = new HashSet<IPlayer>(ReferenceEqualityComparer.Instance);
                _spectators.Add(target, watchers);
            }

            foreach (IPlayer fellow in watchers)
            {
                fellow.OnFellowSpectatorJoined(source);
                source.OnFellowSpectatorJoined(fellow);
            }

            watchers.Add(source);
            target.OnWatched(source);
        }
    }

    /// <inheritdoc />
    public bool UserStoppedWatchingPlayer(IPlayer source, IPlayer? expectedTarget = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_lock)
        {
            return (expectedTarget == null || (_spectating.TryGetValue(source, out IPlayer? current) && ReferenceEquals(current, expectedTarget)))
                && StopWatching(source);
        }
    }

    /// <inheritdoc />
    public IPlayer? GetSpectatingPlayer(IPlayer source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_lock)
        {
            return _spectating.GetValueOrDefault(source);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<IPlayer> GetWatchingPlayers(IPlayer target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (_lock)
        {
            return _spectators.TryGetValue(target, out HashSet<IPlayer>? watchers) ? watchers.ToArray() : [];
        }
    }

    /// <inheritdoc />
    public void ChangePlayerActivity(IPlayer player, Activity activity, LegacyMods mods = LegacyMods.None)
    {
        ArgumentNullException.ThrowIfNull(activity);
        lock (_lock)
        {
            RequireRegistered(player);
            player.Activity = activity;
            player.Mods = mods;
            if (player.IsRestricted || !ReferenceEquals(GetPlayer(player.Id), player))
            {
                player.OnPlayerChangeActivity(player);
                return;
            }

            foreach (IPlayer other in _playersByToken.Values)
            {
                other.OnPlayerChangeActivity(player);
            }
        }
    }

    /// <inheritdoc />
    public void BroadcastUserSentFrames(IPlayer player, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        lock (_lock)
        {
            RequireRegistered(player);
            if (!_spectators.TryGetValue(player, out HashSet<IPlayer>? watchers))
            {
                return;
            }

            foreach (IPlayer watcher in watchers.ToArray())
            {
                watcher.OnUserSentFrames(data);
            }
        }
    }

    /// <inheritdoc />
    public void UserCannotSpectate(IPlayer source)
    {
        lock (_lock)
        {
            RequireRegistered(source);
            if (!_spectating.TryGetValue(source, out IPlayer? target))
            {
                return;
            }

            PacketSpectatorCantSpectate packet = new() { UserId = source.Id };
            target.Enqueue(packet);
            foreach (IPlayer fellow in _spectators[target].Where(fellow => !ReferenceEquals(fellow, source)))
            {
                fellow.Enqueue(packet);
            }
        }
    }

    private void RequireRegistered(IPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (!IsRegistered(player))
        {
            throw new InvalidOperationException("The session is no longer registered.");
        }
    }

    // Callers hold _lock while changing any session index or spectator relationship.
    private bool IsRegistered(IPlayer player)
    {
        return _playersByToken.TryGetValue(player.Token, out IPlayer? current) && ReferenceEquals(current, player);
    }

    private void RemovePlayerCore(IPlayer player, bool isKicked = false, bool announce = true)
    {
        StopWatching(player);
        if (_spectators.TryGetValue(player, out HashSet<IPlayer>? watchers))
        {
            foreach (IPlayer watcher in watchers.ToArray())
            {
                StopWatching(watcher);
                watcher.OnWatchedPlayerOffline(player);
            }
        }

        _playersByToken.Remove(player.Token);
        Dictionary<string, IPlayer> sessions = _playersById[player.Id];
        sessions.Remove(player.Token);
        if (sessions.Count == 0)
        {
            _playersById.Remove(player.Id);
        }

        if (isKicked)
        {
            player.OnPlayerOffline(player, isKicked: true);
        }

        if (!announce || player.IsRestricted)
        {
            return;
        }

        IPlayer? remaining = GetPlayer(player.Id);
        foreach (IPlayer other in _playersByToken.Values)
        {
            if (remaining == null)
            {
                other.OnPlayerOffline(player);
            }
            else if (!remaining.IsRestricted)
            {
                other.OnPlayerOnline(remaining);
            }
        }
    }

    private bool StopWatching(IPlayer source)
    {
        if (!_spectating.Remove(source, out IPlayer? target))
        {
            return false;
        }

        HashSet<IPlayer> watchers = _spectators[target];
        watchers.Remove(source);
        target.OnWatchedStopped(source);
        foreach (IPlayer fellow in watchers)
        {
            fellow.OnFellowSpectatorLeft(source);
        }

        if (watchers.Count == 0)
        {
            _spectators.Remove(target);
        }

        return true;
    }
}
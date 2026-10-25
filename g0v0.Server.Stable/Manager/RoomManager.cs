// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using System.Globalization;
using g0v0.Server.Common.Communication;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Rulesets;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Objects.Rooms;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Server;
using osu.Game.Online.Multiplayer;
using DatabaseRoom = g0v0.Server.Common.Database.Models.Room;
using StableRoom = g0v0.Server.Stable.Objects.Rooms.Room;

namespace g0v0.Server.Stable.Manager;

/// <summary>
/// Owns the live Stable registry. Membership takes the registry gate before a room lock;
/// gameplay takes only its room lock, allowing independent rooms to progress concurrently.
/// </summary>
public sealed class RoomManager : IRoomManager, IDisposable
{
    private readonly ILogger<RoomManager> _logger;
    private readonly IServiceScopeFactory _scopes;
    private readonly IPlayerManager _players;
    private readonly IChatServer _chat;
    private readonly RulesetManager _rulesets;
    private readonly SemaphoreSlim _membershipLock = new(1, 1);
    private readonly ConcurrentDictionary<int, StableRoom> _rooms = new();
    private readonly ConcurrentDictionary<IPlayer, StableRoom> _members = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<IPlayer, StableRoom> _observers = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<IPlayer, byte> _lobby = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Initializes a new instance of the <see cref="RoomManager"/> class.
    /// </summary>
    /// <param name="logger">Room lifecycle diagnostics.</param>
    /// <param name="scopes">Creates short-lived persistence scopes.</param>
    /// <param name="players">The session registry.</param>
    /// <param name="chat">The room channel transport.</param>
    /// <param name="rulesets">Resolves legacy mods for playlist storage.</param>
    public RoomManager(
        ILogger<RoomManager> logger,
        IServiceScopeFactory scopes,
        IPlayerManager players,
        IChatServer chat,
        RulesetManager rulesets)
    {
        _logger = logger;
        _scopes = scopes;
        _players = players;
        _chat = chat;
        _rulesets = rulesets;
        _players.PlayerRemoved += OnPlayerRemoved;
    }

    /// <inheritdoc/>
    public IRoom? GetRoom(int matchId) => _rooms.GetValueOrDefault(matchId);

    /// <inheritdoc/>
    public IRoom? GetPlayerRoom(IPlayer player) => _members.GetValueOrDefault(player);

    /// <inheritdoc/>
    public IReadOnlyList<IRoom> GetAllRooms() => _rooms.Values.ToArray();

    /// <inheritdoc/>
    public async Task CreateRoomAsync(IPlayer host, Match settings)
    {
        await _membershipLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!CanJoin(host) || !StableRoom.ValidSettings(settings))
            {
                host.Enqueue(new PacketMatchJoinFail());
                return;
            }

            int id = Enumerable.Range(0, short.MaxValue + 1).FirstOrDefault(id => !_rooms.ContainsKey(id), -1);
            Match normalized = StableRoom.CopySettings(settings);
            normalized.InProgress = false;
            normalized.HostId = host.Id;
            if (id < 0 || !await StableRoom.ResolveBeatmapAsync(normalized, _scopes).ConfigureAwait(false))
            {
                host.Enqueue(new PacketMatchJoinFail());
                return;
            }

            using IServiceScope scope = _scopes.CreateScope();
            IRoomRepository repository = scope.ServiceProvider.GetRequiredService<IRoomRepository>();
            DatabaseRoom stored = await repository.CreateRoom(
                new MultiplayerRoom(0) { Settings = StableRoom.ToRoomSettings(normalized) }, host.Id, ServerIdentify.Stable, G0V0RoomCategory.Realtime).ConfigureAwait(false);
            StableRoom? room = null;
            try
            {
                ChatChannel channel = await scope.ServiceProvider.GetRequiredService<IChatRepository>()
                    .GetOrCreateMultiplayerChannelAsync(stored.Id).ConfigureAwait(false);
                await repository.UpdateRoomChannelId(stored.Id, channel.ChannelId).ConfigureAwait(false);
                stored.ChannelId = channel.ChannelId;
                room = new StableRoom(stored, checked((short)id), normalized, this, _scopes, _chat, _rulesets);
                await room.RoomLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    await room.InitializePlaylistAsync().ConfigureAwait(false);
                    if (!await room.JoinAsync(host, normalized.Password ?? string.Empty, creating: true).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException("The creating session disconnected before joining its room.");
                    }

                    _members[host] = room;
                    LeaveLobby(host);
                    AttachLifecycle(host);
                    _rooms[id] = room;
                    BroadcastLobby(new PacketNewMatch { Match = room.GetMatch() });
                }
                finally
                {
                    room.RoomLock.Release();
                }
            }
            catch
            {
                if (room != null)
                {
                    ForgetPlayer(host, room);
                    RemoveRoom(room);
                    await _chat.LeaveMultiplayerChannelAsync(host, room.Channel).ConfigureAwait(false);
                    room.Dispose();
                }

                await repository.DeleteRoom(stored.Id).ConfigureAwait(false);
                throw;
            }

            _logger.LogInformation("Created Stable room {MatchId} backed by room {RoomId} for {UserId}.", id, stored.Id, host.Id);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Could not create Stable room for {UserId}.", host.Id);
            host.Enqueue(new PacketMatchJoinFail());
        }
        finally
        {
            _membershipLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task JoinRoomAsync(IPlayer player, int matchId, string password)
    {
        await _membershipLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!CanJoin(player) || !_rooms.TryGetValue(matchId, out StableRoom? room))
            {
                player.Enqueue(new PacketMatchJoinFail());
                return;
            }

            await room.RoomLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!await room.JoinAsync(player, password).ConfigureAwait(false))
                {
                    player.Enqueue(new PacketMatchJoinFail());
                    return;
                }

                _members[player] = room;
                LeaveLobby(player);
                AttachLifecycle(player);
            }
            finally
            {
                room.RoomLock.Release();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Could not join Stable room {MatchId} for {UserId}.", matchId, player.Id);
            player.Enqueue(new PacketMatchJoinFail());
        }
        finally
        {
            _membershipLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task LeaveRoomAsync(IPlayer player)
    {
        await _membershipLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await LeaveRoomCoreAsync(player).ConfigureAwait(false);
        }
        finally
        {
            _membershipLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync(IPlayer player)
    {
        await _membershipLock.WaitAsync().ConfigureAwait(false);
        try
        {
            LeaveLobby(player);
            await LeaveRoomCoreAsync(player).ConfigureAwait(false);
            if (_observers.TryGetValue(player, out StableRoom? observed))
            {
                await observed.RoomLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    await observed.ObserveAsync(player, join: false).ConfigureAwait(false);
                }
                finally
                {
                    observed.RoomLock.Release();
                }
            }
        }
        finally
        {
            _membershipLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task JoinLobbyAsync(IPlayer player)
    {
        await _membershipLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!player.IsOnline || player.IsRestricted || _members.ContainsKey(player) || !_lobby.TryAdd(player, 0))
            {
                return;
            }

            AttachLifecycle(player);
            foreach (StableRoom room in _rooms.Values)
            {
                await room.RoomLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (!room.IsDisposed && _lobby.ContainsKey(player))
                    {
                        player.Enqueue(new PacketNewMatch { Match = room.GetMatch() });
                    }
                }
                finally
                {
                    room.RoomLock.Release();
                }
            }
        }
        finally
        {
            _membershipLock.Release();
        }
    }

    /// <inheritdoc/>
    public void LeaveLobby(IPlayer player) => _lobby.TryRemove(player, out _);

    /// <inheritdoc/>
    public async Task ExecuteAsync(IPlayer player, Func<IRoom, Task> command)
    {
        if (!_members.TryGetValue(player, out StableRoom? room))
        {
            return;
        }

        await room.RoomLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!room.IsDisposed && player is { IsOnline: true, IsRestricted: false }
                && ReferenceEquals(GetPlayerRoom(player), room))
            {
                await command(room).ConfigureAwait(false);
            }
        }
        finally
        {
            room.RoomLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task SendRoomInfoAsync(IPlayer player, int matchId)
    {
        if (!CanObserve(player) || !_rooms.TryGetValue(matchId, out StableRoom? room))
        {
            return;
        }

        await room.RoomLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!room.IsDisposed)
            {
                player.Enqueue(new PacketUpdateMatch { Match = room.GetMatch() });
            }
        }
        finally
        {
            room.RoomLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task WatchRoomAsync(IPlayer player, int matchId, bool join)
    {
        if (!CanObserve(player))
        {
            return;
        }

        await _membershipLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!CanObserve(player))
            {
                return;
            }

            if (!join && (!_observers.TryGetValue(player, out StableRoom? current) || current.MatchId != matchId))
            {
                return;
            }

            if (!_rooms.TryGetValue(matchId, out StableRoom? room) || _members.ContainsKey(player))
            {
                return;
            }

            if (join && _observers.TryGetValue(player, out StableRoom? old) && !ReferenceEquals(old, room))
            {
                await old.RoomLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    await old.ObserveAsync(player, join: false).ConfigureAwait(false);
                }
                finally
                {
                    old.RoomLock.Release();
                }
            }

            await room.RoomLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (room.IsDisposed)
                {
                    return;
                }

                await room.ObserveAsync(player, join).ConfigureAwait(false);
                if (join)
                {
                    _observers[player] = room;
                    AttachLifecycle(player);
                }
            }
            finally
            {
                room.RoomLock.Release();
            }
        }
        finally
        {
            _membershipLock.Release();
        }
    }

    /// <inheritdoc/>
    public Task InviteAsync(IPlayer player, int userId)
    {
        return ExecuteAsync(player, room =>
        {
            IPlayer? target = _players.GetPlayer(userId);
            if (target is not { IsRestricted: false } || ReferenceEquals(target, player))
            {
                return Task.CompletedTask;
            }

            Match match = room.GetMatch(includePassword: true);
            string link = string.Create(CultureInfo.InvariantCulture, $"osump://{room.MatchId}/{Uri.EscapeDataString(match.Password ?? string.Empty)}");
            target.Enqueue(new PacketMatchInvite
            {
                Message = new Message
                {
                    Sender = player.Name,
                    SenderId = player.Id,
                    Recipient = target.Name,
                    Text = $"Come join my multiplayer match: [{link} {match.Name}]",
                },
            });

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Releases manager resources after request processing has stopped.
    /// </summary>
    public void Dispose()
    {
        _players.PlayerRemoved -= OnPlayerRemoved;
        _membershipLock.Dispose();
        foreach (StableRoom room in _rooms.Values)
        {
            room.Dispose();
        }
    }

    internal void ForgetPlayer(IPlayer player, StableRoom room) => _members.TryRemove(new KeyValuePair<IPlayer, StableRoom>(player, room));

    internal void ForgetObserver(IPlayer player, StableRoom room) => _observers.TryRemove(new KeyValuePair<IPlayer, StableRoom>(player, room));

    internal void RemoveRoom(StableRoom room)
    {
        if (_rooms.TryRemove(new KeyValuePair<int, StableRoom>(room.MatchId, room)))
        {
            BroadcastLobby(new PacketDisposeMatch { Id = room.MatchId });
        }
    }

    internal void BroadcastLobby(IPacket packet)
    {
        foreach (IPlayer player in _lobby.Keys)
        {
            if (player is { IsOnline: true, IsRestricted: false })
            {
                player.Enqueue(packet);
            }
            else
            {
                LeaveLobby(player);
            }
        }
    }

    private static bool CanObserve(IPlayer player) => player is { IsOnline: true, IsRestricted: false, IsTourneyClient: true } && player.Privileges.HasFlag(ClientPrivileges.Supporter);

    private bool CanJoin(IPlayer player) => player is { IsOnline: true, IsRestricted: false, IsTourneyClient: false }
        && !_members.ContainsKey(player) && !_observers.ContainsKey(player);

    private void AttachLifecycle(IPlayer player)
    {
        if (player is Player concrete)
        {
            concrete.AttachRoomManager(this);
        }
    }

    private async Task LeaveRoomCoreAsync(IPlayer player)
    {
        if (!_members.TryGetValue(player, out StableRoom? room))
        {
            return;
        }

        await room.RoomLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await room.LeaveAsync(player).ConfigureAwait(false);
        }
        finally
        {
            room.RoomLock.Release();
        }
    }

    private void OnPlayerRemoved(object? sender, PlayerRemovedEventArgs args) => _ = CleanupRemovedPlayerAsync(args.Player);

    private async Task CleanupRemovedPlayerAsync(IPlayer player)
    {
        try
        {
            await DisconnectAsync(player).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not remove disconnected Stable session {UserId} from multiplayer.", player.Id);
        }
    }
}
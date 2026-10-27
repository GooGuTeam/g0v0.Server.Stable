// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Fetching;
using g0v0.Server.Common.Rulesets;
using g0v0.Server.Stable.Manager;
using g0v0.Server.Stable.Objects.Chats;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Server;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using DatabaseRoom = g0v0.Server.Common.Database.Models.Room;
using MatchType = osu.Game.Online.Rooms.MatchType;

namespace g0v0.Server.Stable.Objects.Rooms;

/// <summary>
/// Maintains Stable slots and match barriers, mapping each selection and round to the shared playlist.
/// </summary>
public sealed partial class Room : IRoom, IDisposable
{
    internal const LegacyMods SpeedMods = LegacyMods.DoubleTime | LegacyMods.Nightcore | LegacyMods.HalfTime;
    private readonly IServiceScopeFactory _scopes;
    private readonly IChatServer _chat;
    private readonly RulesetManager _rulesets;
    private readonly RoomManager _manager;
    private readonly RoomSlot[] _slots = Enumerable.Range(0, 16).Select(_ => new RoomSlot()).ToArray();
    private readonly List<MultiplayerPlaylistItem> _playlist = [];
    private readonly HashSet<IPlayer> _observers = new(ReferenceEqualityComparer.Instance);
    private Match _match;
    private bool _allLoaded;
    private bool _allSkipped;

    internal Room(
        DatabaseRoom stored,
        short matchId,
        Match settings,
        RoomManager manager,
        IServiceScopeFactory scopes,
        IChatServer chat,
        RulesetManager rulesets)
    {
        RoomId = stored.Id;
        CreatorId = stored.HostId ?? 0;
        _hostUserId = stored.HostId;
        IsTournament = stored.TournamentMode;
        _size = Math.Clamp(stored.MaxParticipants ?? InitialSize(settings, IsTournament), 2, 16);
        foreach (RoomSlot slot in _slots.Skip(_size))
        {
            slot.Status = SlotStatus.Locked;
        }

        MatchId = matchId;
        _match = settings;
        _manager = manager;
        _scopes = scopes;
        _chat = chat;
        _rulesets = rulesets;
        Channel = new StableChannel("#multiplayer", settings.Name, autoJoin: false, channelId: stored.ChannelId);
        _playlist.AddRange(stored.Playlists.OrderBy(item => item.PlaylistOrder).Select(item => item.ToMultiplayerPlaylistItem()));
        CurrentItem = _playlist.FirstOrDefault(item => !item.Expired)
            ?? _playlist.OrderByDescending(item => item.PlayedAt ?? DateTimeOffset.MinValue).FirstOrDefault();
    }

    /// <inheritdoc/>
    public long RoomId { get; }

    /// <inheritdoc/>
    public short MatchId { get; }

    /// <inheritdoc/>
    public SemaphoreSlim RoomLock { get; } = new(1, 1);

    /// <inheritdoc/>
    public IReadOnlyList<RoomSlot> Slots => _slots;

    /// <inheritdoc/>
    public IReadOnlyList<MultiplayerPlaylistItem> Playlist => _playlist;

    /// <inheritdoc/>
    public MultiplayerPlaylistItem? CurrentItem { get; private set; }

    /// <inheritdoc/>
    public IPlayer? Host { get; private set; }

    /// <inheritdoc/>
    public StableChannel Channel { get; }

    /// <inheritdoc/>
    public bool InProgress => _match.InProgress;

    /// <inheritdoc/>
    public bool IsDisposed { get; private set; }

    /// <inheritdoc/>
    public Match GetMatch(bool includePassword = false)
    {
        Match result = CopySettings(_match);
        result.Id = MatchId;
        result.HostId = _hostUserId ?? 0;
        result.Password = string.IsNullOrEmpty(_match.Password) ? null : includePassword ? _match.Password : string.Empty;
        result.SlotStatuses = _slots.Select(slot => slot.Status).ToArray();
        result.SlotTeams = _slots.Select(slot => slot.Team).ToArray();
        result.SlotUserIds = _slots.Select(slot => slot.Player?.Id).ToArray();
        result.SlotMods = _match.FreeMods ? _slots.Select(slot => slot.Mods).ToArray() : null;
        return result;
    }

    /// <inheritdoc/>
    public Task ChangeSlotAsync(IPlayer player, int slotId)
    {
        int previous = FindSlotIndex(player);
        if (InProgress || previous < 0 || !ValidSlot(slotId) || slotId >= _size || _slots[slotId].Status != SlotStatus.Open)
        {
            return Task.CompletedTask;
        }

        (_slots[previous], _slots[slotId]) = (_slots[slotId], _slots[previous]);
        BroadcastState();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task LockSlotAsync(IPlayer player, int slotId)
    {
        if (!IsHost(player) || InProgress || !ValidSlot(slotId) || slotId >= _size || ReferenceEquals(_slots[slotId].Player, Host))
        {
            return;
        }

        RoomSlot slot = _slots[slotId];
        if (slot.Player is { } occupant)
        {
            await LeaveAsync(occupant, locked: true).ConfigureAwait(false);

            // Stable leaves the match when its user ID disappears from the slot snapshot.
            occupant.Enqueue(new PacketUpdateMatch { Match = GetMatch() });
            return;
        }

        slot.Reset(slot.Status == SlotStatus.Locked ? SlotStatus.Open : SlotStatus.Locked);
        BroadcastState();
    }

    /// <inheritdoc/>
    public Task ChangeStateAsync(IPlayer player, SlotStatus state)
    {
        RoomSlot? slot = FindSlot(player);
        if (InProgress || slot is null || state is not (SlotStatus.Ready or SlotStatus.NotReady or SlotStatus.NoMap)
            || (state == SlotStatus.Ready && (slot.Status == SlotStatus.NoMap || CurrentItem is null)))
        {
            return Task.CompletedTask;
        }

        slot.Status = state;
        BroadcastState(lobby: false);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task ChangeSettingsAsync(IPlayer player, Match settings)
    {
        if (!IsHost(player) || InProgress || !ValidSettings(settings))
        {
            return;
        }

        Match next = CopySettings(settings);
        next.InProgress = false;
        next.Password = _match.Password;
        next.Mods = _match.Mods;
        if (next.FreeMods != _match.FreeMods)
        {
            next.Mods = next.FreeMods ? _match.Mods & SpeedMods : (_match.Mods & SpeedMods) | FindSlot(player)!.Mods;
        }

        if (!await ResolveBeatmapAsync(next, _scopes).ConfigureAwait(false))
        {
            player.Enqueue(new PacketNotification { Message = "The selected beatmap could not be resolved. Please select a submitted beatmap." });
            player.Enqueue(new PacketUpdateMatch { Match = GetMatch(includePassword: true) });
            return;
        }

        bool mapChanged = next.MapId != _match.MapId || !string.Equals(next.MapMd5, _match.MapMd5, StringComparison.Ordinal);
        bool playSettingsChanged = mapChanged || next.Mode != _match.Mode || next.FreeMods != _match.FreeMods
            || next.WinCondition != _match.WinCondition || next.TeamType != _match.TeamType;
        using (IServiceScope scope = _scopes.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRoomRepository>()
                .UpdateRoomSettings(RoomId, ToRoomSettings(next, _size)).ConfigureAwait(false);
        }

        if (mapChanged || next.Mode != _match.Mode || next.FreeMods != _match.FreeMods || next.WinCondition != _match.WinCondition)
        {
            await PersistSelectionAsync(next, newSelection: mapChanged).ConfigureAwait(false);
        }

        foreach (RoomSlot slot in _slots.Where(slot => slot.Player != null))
        {
            if (next.FreeMods != _match.FreeMods)
            {
                slot.Mods = next.FreeMods ? _match.Mods & ~SpeedMods : LegacyMods.None;
            }

            if (next.TeamType != _match.TeamType)
            {
                slot.Team = DefaultTeam(next.TeamType);
            }

            if (playSettingsChanged && slot.Status == SlotStatus.Ready)
            {
                slot.Status = SlotStatus.NotReady;
            }
        }

        _match = next;
        BroadcastState();
    }

    /// <inheritdoc/>
    public async Task ChangePasswordAsync(IPlayer player, string? password)
    {
        if (!IsHost(player) || password?.Length > 40)
        {
            return;
        }

        Match next = CopySettings(_match);
        next.Password = string.IsNullOrEmpty(password) ? null : password;
        using IServiceScope scope = _scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRoomRepository>()
            .UpdateRoomSettings(RoomId, ToRoomSettings(next, _size)).ConfigureAwait(false);
        _match = next;
        BroadcastState();
    }

    /// <inheritdoc/>
    public async Task ChangeModsAsync(IPlayer player, LegacyMods mods)
    {
        RoomSlot? slot = FindSlot(player);
        if (InProgress || slot is null || (!_match.FreeMods && !IsHost(player)) || !ValidMods(mods))
        {
            return;
        }

        Match next = CopySettings(_match);
        if (!_match.FreeMods || IsHost(player))
        {
            next.Mods = _match.FreeMods ? mods & SpeedMods : mods;
        }

        if (next.Mods != _match.Mods)
        {
            await PersistSelectionAsync(next, newSelection: false).ConfigureAwait(false);
            UnreadyPlayers();
        }

        if (_match.FreeMods)
        {
            slot.Mods = mods & ~SpeedMods;
            if (slot.Status == SlotStatus.Ready)
            {
                slot.Status = SlotStatus.NotReady;
            }
        }

        _match = next;
        BroadcastState();
    }

    /// <inheritdoc/>
    public Task ChangeTeamAsync(IPlayer player)
    {
        RoomSlot? slot = FindSlot(player);
        if (InProgress || slot is null || !IsTeamMode(_match.TeamType))
        {
            return Task.CompletedTask;
        }

        slot.Team = slot.Team == MatchTeam.Blue ? MatchTeam.Red : MatchTeam.Blue;
        if (slot.Status == SlotStatus.Ready)
        {
            slot.Status = SlotStatus.NotReady;
        }

        BroadcastState(lobby: false);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task TransferHostAsync(IPlayer player, int slotId)
    {
        if (!IsHost(player) || !ValidSlot(slotId) || _slots[slotId].Player is not { } target || ReferenceEquals(target, Host))
        {
            return;
        }

        await SetHostAsync(target).ConfigureAwait(false);
        BroadcastState();
    }

    /// <inheritdoc/>
    public async Task StartAsync(IPlayer player)
    {
        if (!IsHost(player) || InProgress || _match.MapId <= 0 || CurrentItem is null
            || !_slots.Any(slot => slot is { Player: not null, Status: SlotStatus.Ready or SlotStatus.NotReady }))
        {
            return;
        }

        if (CurrentItem.Expired)
        {
            if (_playlist.Exists(item => !item.Expired))
            {
                await AdvanceToNextPlaylistItemAsync().ConfigureAwait(false);
            }
            else
            {
                await PersistSelectionAsync(_match, newSelection: true).ConfigureAwait(false);
            }
        }

        using IServiceScope scope = _scopes.CreateScope();
        IRoomRepository repository = scope.ServiceProvider.GetRequiredService<IRoomRepository>();

        // Seal the round's item before releasing gameplay; replaying the same map needs a new item.
        MultiplayerPlaylistItem started = CreatePlaylistItem(_match, CurrentItem!.OwnerID);
        started.ID = CurrentItem.ID;
        started.PlaylistOrder = CurrentItem.PlaylistOrder;
        started.Expired = true;
        started.PlayedAt = DateTimeOffset.UtcNow;
        await repository.EditItemToPlaylist(started, RoomId, started.OwnerID).ConfigureAwait(false);
        ReplaceCurrentItem(started);
        await repository.UpdateRoomStatus(RoomId, MultiplayerRoomState.WaitingForLoad).ConfigureAwait(false);

        _allLoaded = false;
        _allSkipped = false;
        foreach (RoomSlot slot in _slots)
        {
            slot.ResetGameplay();
            if (slot.Player != null && slot.Status != SlotStatus.NoMap)
            {
                slot.Status = SlotStatus.Playing;
            }
        }

        _match.InProgress = true;
        BroadcastGameplay(new PacketMatchStart { Match = GetMatch(includePassword: true) });
        BroadcastState();
    }

    /// <inheritdoc/>
    public async Task LoadCompleteAsync(IPlayer player)
    {
        RoomSlot? slot = PlayingSlot(player);
        if (slot is null)
        {
            return;
        }

        slot.Loaded = true;
        await CheckBarriersAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task ScoreUpdateAsync(IPlayer player, ScoreFrame frame)
    {
        RoomSlot? slot = PlayingSlot(player);
        if (slot is null || !slot.Loaded || !ValidFrame(frame)
            || (slot.Score != null && frame.Time < slot.Score.Time))
        {
            return Task.CompletedTask;
        }

        // Never trust the ID in the payload, even when the rest of the frame is well formed.
        ScoreFrame snapshot = frame.Copy();
        snapshot.Id = checked((byte)FindSlotIndex(player));
        slot.Score = snapshot;
        BroadcastGameplay(new PacketMatchScoreUpdate { Frame = snapshot });
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task FailAsync(IPlayer player)
    {
        RoomSlot? slot = PlayingSlot(player);
        if (slot is not { Failed: false })
        {
            return Task.CompletedTask;
        }

        slot.Failed = true;
        BroadcastGameplay(new PacketMatchPlayerFailed { SlotId = FindSlotIndex(player) });
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task SkipAsync(IPlayer player)
    {
        RoomSlot? slot = PlayingSlot(player);
        if (slot is null)
        {
            return;
        }

        if (!slot.Skipped)
        {
            slot.Skipped = true;
            BroadcastGameplay(new PacketMatchPlayerSkipped { UserId = player.Id });
        }

        await CheckBarriersAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task CompleteAsync(IPlayer player)
    {
        RoomSlot? slot = FindSlot(player);
        if (!InProgress || slot is null || slot.Status is not (SlotStatus.Playing or SlotStatus.Complete))
        {
            return;
        }

        slot.Status = SlotStatus.Complete;
        await CheckBarriersAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task AbortAsync(IPlayer player)
    {
        if (IsHost(player) && InProgress)
        {
            await FinishAsync(aborted: true).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Releases the room lock when the owner shuts down and no operations remain.
    /// </summary>
    public void Dispose() => RoomLock.Dispose();

    internal static int InitialSize(Match settings, bool tournamentMode)
    {
        if (tournamentMode)
        {
            return 8;
        }

        int locked = settings.SlotStatuses.Count(status => status == SlotStatus.Locked);
        return Math.Clamp(16 - locked, 2, 16);
    }

    internal static bool ValidSettings(Match match) => !string.IsNullOrWhiteSpace(match.Name) && match.Name.Length <= 100
        && match.Password?.Length is not > 40 && match.Mode is >= 0 and <= 3
        && Enum.IsDefined(match.TeamType) && Enum.IsDefined(match.WinCondition) && ValidMods(match.Mods);

    internal static Match CopySettings(Match match) => new()
    {
        Name = match.Name,
        Password = string.IsNullOrEmpty(match.Password) ? null : match.Password,
        MapId = match.MapId,
        MapMd5 = match.MapMd5,
        MapName = match.MapName,
        Mode = match.Mode,
        Mods = match.Mods,
        FreeMods = match.FreeMods,
        WinCondition = match.WinCondition,
        TeamType = match.TeamType,
        Seed = match.Seed,
        InProgress = match.InProgress,
        Type = 0,
    };

    internal static MultiplayerRoomSettings ToRoomSettings(Match match, int size = 16) => new()
    {
        Name = match.Name,
        Password = match.Password ?? string.Empty,
        MatchType = IsTeamMode(match.TeamType) ? MatchType.TeamVersus : MatchType.HeadToHead,
        QueueMode = QueueMode.HostOnly,
        MaxParticipants = checked((byte)size),
    };

    internal static async Task<bool> ResolveBeatmapAsync(Match match, IServiceScopeFactory scopes)
    {
        if (match.MapId == -1)
        {
            match.MapName = string.Empty;
            match.MapMd5 = string.Empty;
            return true;
        }

        if (match.MapMd5.Length != 32 || !match.MapMd5.All(char.IsAsciiHexDigit))
        {
            return false;
        }

        using IServiceScope scope = scopes.CreateScope();
        IBeatmapRepository repository = scope.ServiceProvider.GetRequiredService<IBeatmapRepository>();
        string checksum = match.MapMd5.ToLowerInvariant();
        Beatmap? beatmap = await repository.GetByChecksumAsync(checksum).ConfigureAwait(false);
        if (beatmap is null && scope.ServiceProvider.GetService<IFetcher>() is { } fetcher)
        {
            try
            {
                beatmap = await repository.GetOrFetchByChecksumAsync(checksum, fetcher).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                scope.ServiceProvider.GetService<ILogger<Room>>()?.LogWarning(exception, "Could not resolve multiplayer beatmap {Checksum}.", checksum);
                return false;
            }
        }

        if (beatmap is null || !string.Equals(beatmap.Checksum, checksum, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        match.MapId = beatmap.Id;
        match.MapMd5 = checksum;
        if (beatmap.Mode != 0)
        {
            match.Mode = checked((sbyte)beatmap.Mode);
        }

        return true;
    }

    internal async Task<bool> JoinAsync(IPlayer player, string password, bool creating = false)
    {
        if (IsDisposed || _bannedUsers.Contains(player.Id) || (!creating && _joinsLocked) || player.IsRestricted || player.IsTourneyClient || !player.IsOnline
            || _slots.Any(slot => slot.Player?.Id == player.Id)
            || (!creating && !string.Equals(_match.Password ?? string.Empty, password, StringComparison.Ordinal)))
        {
            return false;
        }

        RoomSlot? slot = _slots.Take(_size).FirstOrDefault(slot => slot.Status == SlotStatus.Open);
        if (slot is null)
        {
            return false;
        }

        using IServiceScope scope = _scopes.CreateScope();
        IRoomRepository repository = scope.ServiceProvider.GetRequiredService<IRoomRepository>();
        await repository.AddUserToRoom(RoomId, player.Id).ConfigureAwait(false);
        try
        {
            await _chat.JoinMultiplayerChannelAsync(player, Channel).ConfigureAwait(false);
        }
        catch
        {
            await repository.RemoveUserFromRoom(RoomId, player.Id).ConfigureAwait(false);
            throw;
        }

        slot.Player = player;
        slot.Status = SlotStatus.NotReady;
        slot.Team = DefaultTeam(_match.TeamType);
        if (creating)
        {
            Host = player;
            if (_match.FreeMods)
            {
                slot.Mods = _match.Mods & ~SpeedMods;
                _match.Mods &= SpeedMods;
            }
        }

        if (!creating && Host is null && (_hostUserId is null || _hostUserId == player.Id))
        {
            await SetHostAsync(player).ConfigureAwait(false);
        }

        player.Enqueue(new PacketMatchJoinSuccess { Match = GetMatch(includePassword: true) });
        BroadcastState();
        return true;
    }

    internal async Task InitializePlaylistAsync()
    {
        await PersistSelectionAsync(_match, newSelection: true).ConfigureAwait(false);
    }

    internal async Task LeaveAsync(IPlayer player, bool locked = false)
    {
        RoomSlot? slot = FindSlot(player);
        if (slot is null)
        {
            return;
        }

        using IServiceScope scope = _scopes.CreateScope();
        IRoomRepository repository = scope.ServiceProvider.GetRequiredService<IRoomRepository>();
        await repository.RemoveUserFromRoom(RoomId, player.Id).ConfigureAwait(false);
        await _chat.LeaveMultiplayerChannelAsync(player, Channel).ConfigureAwait(false);
        slot.Reset(status: locked ? SlotStatus.Locked : SlotStatus.Open);
        _manager.ForgetPlayer(player, this);
        if (!_slots.Any(other => other.Player != null))
        {
            if (IsTournament && !_closing)
            {
                Host = null;
                _hostUserId = CreatorId;
                _match.InProgress = false;
                await repository.UpdateRoomHost(RoomId, CreatorId).ConfigureAwait(false);
                await repository.UpdateRoomStatus(RoomId, MultiplayerRoomState.Open).ConfigureAwait(false);
                BroadcastState();
                return;
            }

            await repository.UpdateRoomStatus(RoomId, MultiplayerRoomState.Open).ConfigureAwait(false);
            await repository.UpdateRoomHost(RoomId, hostUserId: null).ConfigureAwait(false);
            await repository.SetRoomEndDate(RoomId, DateTimeOffset.UtcNow).ConfigureAwait(false);
            Host = null;
            _match.InProgress = false;
            IsDisposed = true;
            foreach (IPlayer observer in _observers.ToArray())
            {
                await ObserveAsync(observer, join: false).ConfigureAwait(false);
                observer.Enqueue(new PacketDisposeMatch { Id = MatchId });
            }

            _manager.RemoveRoom(this);
            return;
        }

        if (ReferenceEquals(Host, player))
        {
            await SetHostAsync(_slots.First(other => other.Player != null).Player!).ConfigureAwait(false);
        }

        await CheckBarriersAsync().ConfigureAwait(false);
        BroadcastState();
    }

    internal async Task ObserveAsync(IPlayer player, bool join)
    {
        if (join)
        {
            if (!IsDisposed && !_bannedUsers.Contains(player.Id) && !_observers.Contains(player))
            {
                await _chat.JoinMultiplayerChannelAsync(player, Channel).ConfigureAwait(false);
                _observers.Add(player);
                player.Enqueue(new PacketUpdateMatch { Match = GetMatch() });
            }
        }
        else if (_observers.Contains(player))
        {
            await _chat.LeaveMultiplayerChannelAsync(player, Channel).ConfigureAwait(false);
            _observers.Remove(player);
            _manager.ForgetObserver(player, this);
        }
    }

    private static bool ValidSlot(int index) => index is >= 0 and < 16;

    private static bool IsTeamMode(MatchTeamType type) => type is MatchTeamType.TeamVs or MatchTeamType.TagTeamVs;

    private static MatchTeam DefaultTeam(MatchTeamType type) => IsTeamMode(type) ? MatchTeam.Red : MatchTeam.Neutral;

    private static bool ValidMods(LegacyMods mods) => (int)mods >= 0
        && !((mods & (LegacyMods.DoubleTime | LegacyMods.Nightcore)) != LegacyMods.None && mods.HasFlag(LegacyMods.HalfTime));

    private static bool ValidFrame(ScoreFrame frame) => !frame.ScoreV2
        ? frame.ComboPortion is null && frame.BonusPortion is null
        : frame is { ComboPortion: { } combo, BonusPortion: { } bonus }
          && double.IsFinite(combo) && double.IsFinite(bonus);

    private bool IsHost(IPlayer player) => !IsDisposed && ReferenceEquals(Host, player);

    private int FindSlotIndex(IPlayer player) => Array.FindIndex(_slots, slot => ReferenceEquals(slot.Player, player));

    private RoomSlot? FindSlot(IPlayer player) => _slots.FirstOrDefault(slot => ReferenceEquals(slot.Player, player));

    private RoomSlot? PlayingSlot(IPlayer player) => InProgress && FindSlot(player) is { Status: SlotStatus.Playing } slot ? slot : null;

    private void UnreadyPlayers()
    {
        foreach (RoomSlot slot in _slots.Where(slot => slot.Status == SlotStatus.Ready))
        {
            slot.Status = SlotStatus.NotReady;
        }
    }

    private async Task SetHostAsync(IPlayer player)
    {
        using IServiceScope scope = _scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRoomRepository>().UpdateRoomHost(RoomId, player.Id).ConfigureAwait(false);
        Host = player;
        _hostUserId = player.Id;
        player.Enqueue(new PacketMatchTransferHost());
    }

    private void BroadcastState(bool lobby = true)
    {
        PacketUpdateMatch members = new() { Match = GetMatch(includePassword: true) };
        foreach (RoomSlot slot in _slots.Where(slot => slot.Player != null))
        {
            slot.Player!.Enqueue(members);
        }

        PacketUpdateMatch publicState = new() { Match = GetMatch() };
        foreach (IPlayer observer in _observers)
        {
            observer.Enqueue(publicState);
        }

        if (lobby && ReferenceEquals(_manager.GetRoom(MatchId), this))
        {
            _manager.BroadcastLobby(publicState);
        }
    }

    private void BroadcastGameplay(IPacket packet)
    {
        foreach (RoomSlot slot in _slots.Where(slot => slot is { Player: not null, Status: SlotStatus.Playing or SlotStatus.Complete }))
        {
            slot.Player!.Enqueue(packet);
        }

        foreach (IPlayer observer in _observers)
        {
            // Observers do not receive the password-bearing start payload.
            observer.Enqueue(packet is PacketMatchStart ? new PacketMatchStart { Match = GetMatch() } : packet);
        }
    }

    private async Task CheckBarriersAsync()
    {
        if (!InProgress)
        {
            return;
        }

        RoomSlot[] playing = _slots.Where(slot => slot.Status == SlotStatus.Playing).ToArray();
        if (playing.Length == 0)
        {
            await FinishAsync(aborted: false).ConfigureAwait(false);
            return;
        }

        if (!_allLoaded && playing.All(slot => slot.Loaded))
        {
            using IServiceScope scope = _scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IRoomRepository>()
                .UpdateRoomStatus(RoomId, MultiplayerRoomState.Playing).ConfigureAwait(false);
            _allLoaded = true;
            BroadcastGameplay(new PacketMatchAllPlayersLoaded());
        }

        if (!_allSkipped && playing.All(slot => slot.Skipped))
        {
            _allSkipped = true;
            BroadcastGameplay(new PacketMatchSkip());
        }
    }

    private async Task FinishAsync(bool aborted)
    {
        using IServiceScope scope = _scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRoomRepository>()
            .UpdateRoomStatus(RoomId, MultiplayerRoomState.Open).ConfigureAwait(false);
        BroadcastGameplay(aborted ? new PacketMatchAbort() : new PacketMatchComplete());
        _match.InProgress = false;
        foreach (RoomSlot slot in _slots)
        {
            if (slot.Status is SlotStatus.Playing or SlotStatus.Complete)
            {
                slot.Status = SlotStatus.NotReady;
            }

            slot.Loaded = false;
            slot.Skipped = false;
        }

        if (!aborted && _playlist.Exists(item => !item.Expired))
        {
            await AdvanceToNextPlaylistItemAsync().ConfigureAwait(false);
        }

        BroadcastState();
    }

    private MultiplayerPlaylistItem CreatePlaylistItem(Match settings, int ownerId)
    {
        Ruleset ruleset = _rulesets.GetRuleset(settings.Mode);
        LegacyMods required = settings.FreeMods ? settings.Mods & SpeedMods : settings.Mods;
        if (settings.WinCondition == MatchWinCondition.ScoreV2)
        {
            required |= LegacyMods.ScoreV2;
        }

        List<APIMod> requiredMods = ruleset.ConvertFromLegacyMods(required).Select(mod => new APIMod(mod)).ToList();
        if (ruleset.CreateMod<ModClassic>() is { } classic)
        {
            requiredMods.Add(new APIMod(classic));
        }

        List<APIMod> allowed = settings.FreeMods
            ? Enum.GetValues<LegacyMods>().Where(mod => mod != LegacyMods.None && (mod & SpeedMods) == LegacyMods.None)
                .SelectMany(mod => ruleset.ConvertFromLegacyMods(mod)).Select(mod => new APIMod(mod))
                .Where(mod => requiredMods.TrueForAll(requiredMod => !string.Equals(mod.Acronym, requiredMod.Acronym, StringComparison.Ordinal)))
                .DistinctBy(mod => mod.Acronym).ToList()
            : [];
        return new MultiplayerPlaylistItem
        {
            BeatmapID = settings.MapId,
            BeatmapChecksum = settings.MapMd5,
            RulesetID = settings.Mode,
            OwnerID = ownerId,
            RequiredMods = requiredMods,
            AllowedMods = allowed,
            Freestyle = false,
            WinCondition = settings.WinCondition switch
            {
                MatchWinCondition.Accuracy => WinCondition.Accuracy,
                MatchWinCondition.Combo => WinCondition.Combo,
                _ => WinCondition.Score,
            },
        };
    }

    private async Task PersistSelectionAsync(Match settings, bool newSelection)
    {
        using IServiceScope scope = _scopes.CreateScope();
        IRoomRepository repository = scope.ServiceProvider.GetRequiredService<IRoomRepository>();
        if (settings.MapId <= 0)
        {
            await ExpireSelectionAsync(repository).ConfigureAwait(false);
            CurrentItem = null;
            return;
        }

        bool append = newSelection || CurrentItem is null || CurrentItem.Expired;
        MultiplayerPlaylistItem item = CreatePlaylistItem(settings, append ? Host?.Id ?? _match.HostId : CurrentItem!.OwnerID);
        if (append)
        {
            await ExpireSelectionAsync(repository).ConfigureAwait(false);
            item.PlaylistOrder = checked((ushort)_playlist.Count);
            Playlist stored = await repository.AddItemToPlaylist(item, RoomId, item.OwnerID).ConfigureAwait(false);
            item.ID = stored.Id;
            _playlist.Add(item);
            CurrentItem = item;
        }
        else
        {
            item.ID = CurrentItem!.ID;
            item.PlaylistOrder = CurrentItem.PlaylistOrder;
            await repository.EditItemToPlaylist(item, RoomId, item.OwnerID).ConfigureAwait(false);
            ReplaceCurrentItem(item);
        }
    }

    private async Task ExpireSelectionAsync(IRoomRepository repository)
    {
        if (CurrentItem is not { Expired: false } current)
        {
            return;
        }

        MultiplayerPlaylistItem expired = CreatePlaylistItem(_match, current.OwnerID);
        expired.ID = current.ID;
        expired.PlaylistOrder = current.PlaylistOrder;
        expired.Expired = true;
        expired.PlayedAt = current.PlayedAt;
        await repository.EditItemToPlaylist(expired, RoomId, expired.OwnerID).ConfigureAwait(false);
        ReplaceCurrentItem(expired);
    }

    private void ReplaceCurrentItem(MultiplayerPlaylistItem item)
    {
        int index = _playlist.FindIndex(existing => existing.ID == item.ID);
        if (index < 0)
        {
            return;
        }

        bool wasCurrent = CurrentItem?.ID == item.ID;
        _playlist[index] = item;
        if (wasCurrent)
        {
            CurrentItem = item;
        }
    }

    private async Task AdvanceToNextPlaylistItemAsync()
    {
        MultiplayerPlaylistItem? next = _playlist.Where(item => !item.Expired)
            .OrderBy(item => item.PlaylistOrder)
            .FirstOrDefault();
        if (next is null || next.ID == CurrentItem?.ID)
        {
            return;
        }

        await ApplyPlaylistItemAsync(next).ConfigureAwait(false);
    }

    private async Task ApplyPlaylistItemAsync(MultiplayerPlaylistItem item)
    {
        using IServiceScope scope = _scopes.CreateScope();
        IBeatmapRepository maps = scope.ServiceProvider.GetRequiredService<IBeatmapRepository>();
        Beatmap? map = await maps.GetByIdAsync(item.BeatmapID).ConfigureAwait(false);
        if (map is null && scope.ServiceProvider.GetService<IFetcher>() is { } fetcher)
        {
            map = await maps.GetOrFetchByIdAsync(item.BeatmapID, fetcher).ConfigureAwait(false);
        }

        if (map?.Checksum is not { Length: 32 })
        {
            return;
        }

        Match next = CopySettings(_match);
        next.MapId = item.BeatmapID;
        next.MapMd5 = map.Checksum;
        next.MapName = map.Version;
        next.Mode = (sbyte)item.RulesetID;
        next.Mods = _rulesets.GetRuleset(item.RulesetID).ConvertToLegacyMods(item.RequiredMods
            .Select(mod => _rulesets.GetRuleset(item.RulesetID).CreateModFromAcronym(mod.Acronym))
            .Where(mod => mod is not null)
            .Cast<Mod>()
            .ToArray());
        next.WinCondition = item.WinCondition switch
        {
            WinCondition.Accuracy => MatchWinCondition.Accuracy,
            WinCondition.Combo => MatchWinCondition.Combo,
            _ => MatchWinCondition.Score,
        };
        _match = next;
        CurrentItem = item;
        using IServiceScope settingsScope = _scopes.CreateScope();
        await settingsScope.ServiceProvider.GetRequiredService<IRoomRepository>()
            .UpdateRoomSettings(RoomId, ToRoomSettings(next, _size)).ConfigureAwait(false);
    }
}
// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Objects.Chats;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Packets;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Online.Rooms;

namespace g0v0.Server.Stable.Objects.Rooms;

/// <summary>
/// A server-authoritative Stable room. Hold RoomLock when reading state or invoking mutations.
/// </summary>
public interface IRoom
{
    /// <summary>
    /// Gets the persistent room identifier.
    /// </summary>
    long RoomId { get; }

    /// <summary>
    /// Gets the independently allocated Stable protocol identifier.
    /// </summary>
    short MatchId { get; }

    /// <summary>
    /// Gets the lock serializing this room's state and persistence.
    /// </summary>
    SemaphoreSlim RoomLock { get; }

    /// <summary>
    /// Gets the sixteen server-owned slots.
    /// </summary>
    IReadOnlyList<RoomSlot> Slots { get; }

    /// <summary>
    /// Gets the persistent playlist history.
    /// </summary>
    IReadOnlyList<MultiplayerPlaylistItem> Playlist { get; }

    /// <summary>
    /// Gets the selected playlist entry, or null while selecting a beatmap.
    /// </summary>
    MultiplayerPlaylistItem? CurrentItem { get; }

    /// <summary>
    /// Gets the current host session.
    /// </summary>
    IPlayer? Host { get; }

    /// <summary>
    /// Gets the private room chat channel.
    /// </summary>
    StableChannel Channel { get; }

    /// <summary>
    /// Gets a value indicating whether gameplay is in progress.
    /// </summary>
    bool InProgress { get; }

    /// <summary>
    /// Gets a value indicating whether the room has ended.
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    /// Copies the wire state, optionally revealing the password to room members.
    /// </summary>
    /// <param name="includePassword">Whether this recipient is allowed to see the room password.</param>
    /// <returns>An independent wire snapshot.</returns>
    Match GetMatch(bool includePassword = false);

    /// <summary>
    /// Moves the caller to an open slot.
    /// </summary>
    /// <param name="player">The acting session.</param>
    /// <param name="slotId">The zero-based slot index.</param>
    /// <returns>A task representing completion of the operation.</returns>
    Task ChangeSlotAsync(IPlayer player, int slotId);

    /// <summary>
    /// Toggles a slot lock as host, removing an occupant immediately.
    /// The maximum participant count mirrors the number of unlocked slots after the toggle.
    /// </summary>
    /// <param name="player">The acting session.</param>
    /// <param name="slotId">The zero-based slot index.</param>
    /// <returns>A task representing completion of the operation.</returns>
    Task LockSlotAsync(IPlayer player, int slotId);

    /// <summary>
    /// Changes a participant's readiness or beatmap availability.
    /// </summary>
    /// <param name="player">The acting session.</param>
    /// <param name="state">The requested slot state.</param>
    /// <returns>A task representing completion of the operation.</returns>
    Task ChangeStateAsync(IPlayer player, SlotStatus state);

    /// <summary>
    /// Changes host-owned settings and persists the corresponding playlist selection.
    /// </summary>
    /// <param name="player">The acting session.</param>
    /// <param name="settings">The untrusted client settings.</param>
    /// <returns>A task representing completion of the operation.</returns>
    Task ChangeSettingsAsync(IPlayer player, Match settings);

    /// <summary>
    /// Changes the room password as host.
    /// </summary>
    /// <param name="player">The acting session.</param>
    /// <param name="password">The supplied room password.</param>
    /// <returns>A task representing completion of the operation.</returns>
    Task ChangePasswordAsync(IPlayer player, string? password);

    /// <summary>
    /// Changes global or per-slot mods according to the free-mod rules.
    /// </summary>
    /// <param name="player">The acting session.</param>
    /// <param name="mods">The requested legacy mods.</param>
    /// <returns>A task representing completion of the operation.</returns>
    Task ChangeModsAsync(IPlayer player, LegacyMods mods);

    /// <summary>
    /// Toggles a participant's team in team modes.
    /// </summary>
    /// <param name="player">The acting session.</param>
    /// <returns>A task representing completion of the operation.</returns>
    Task ChangeTeamAsync(IPlayer player);

    /// <summary>
    /// Transfers host to an occupied slot.
    /// </summary>
    /// <param name="player">The acting session.</param>
    /// <param name="slotId">The zero-based slot index.</param>
    /// <returns>A task representing completion of the operation.</returns>
    Task TransferHostAsync(IPlayer player, int slotId);

    /// <summary>
    /// Starts gameplay for members who have the selected beatmap.
    /// </summary>
    /// <param name="player">The acting session.</param>
    /// <returns>A task representing completion of the operation.</returns>
    Task StartAsync(IPlayer player);

    /// <summary>
    /// Records loading completion and releases the loading barrier once.
    /// </summary>
    /// <param name="player">The acting session.</param>
    /// <returns>A task representing completion of the operation.</returns>
    Task LoadCompleteAsync(IPlayer player);

    /// <summary>
    /// Validates and records a parsed score snapshot, replacing its slot identifier.
    /// </summary>
    /// <param name="player">The acting session.</param>
    /// <param name="frame">The parsed score snapshot.</param>
    /// <returns>A task representing completion of the operation.</returns>
    Task ScoreUpdateAsync(IPlayer player, ScoreFrame frame);

    /// <summary>
    /// Records a player failure without prematurely completing their play.
    /// </summary>
    /// <param name="player">The acting session.</param>
    /// <returns>A task representing completion of the operation.</returns>
    Task FailAsync(IPlayer player);

    /// <summary>
    /// Records a participant's skip vote and releases the skip barrier once.
    /// </summary>
    /// <param name="player">The acting session.</param>
    /// <returns>A task representing completion of the operation.</returns>
    Task SkipAsync(IPlayer player);

    /// <summary>
    /// Completes a participant's play and finishes the round when everyone is done.
    /// </summary>
    /// <param name="player">The acting session.</param>
    /// <returns>A task representing completion of the operation.</returns>
    Task CompleteAsync(IPlayer player);

    /// <summary>
    /// Aborts gameplay and returns participants to the room.
    /// </summary>
    /// <param name="player">The acting session.</param>
    /// <returns>A task representing completion of the operation.</returns>
    Task AbortAsync(IPlayer player);
}
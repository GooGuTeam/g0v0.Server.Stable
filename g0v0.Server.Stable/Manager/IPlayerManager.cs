// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Objects.States;
using osu.Game.Beatmaps.Legacy;

namespace g0v0.Server.Stable.Manager;

/// <summary>
/// Manages connected stable sessions and their spectator relationships.
/// </summary>
public interface IPlayerManager
{
    /// <summary>
    /// Occurs when an exact session is removed or replaced.
    /// </summary>
    event EventHandler<PlayerRemovedEventArgs>? PlayerRemoved;

    /// <summary>
    /// Adds a session, replacing an existing ordinary session for the same account.
    /// </summary>
    /// <param name="player">The session to register. Tournament sessions may coexist.</param>
    void AddPlayer(IPlayer player);

    /// <summary>
    /// Removes this exact session and clears its spectator relationships.
    /// </summary>
    /// <param name="player">The session to remove.</param>
    /// <param name="isKicked">Whether a replacement connection caused the removal.</param>
    /// <returns>Whether the registered session was removed.</returns>
    bool RemovePlayer(IPlayer player, bool isKicked = false);

    /// <summary>
    /// Gets an account's ordinary session, or a tournament session if no ordinary session exists.
    /// </summary>
    /// <param name="id">The account ID.</param>
    /// <returns>A connected session, or null.</returns>
    IPlayer? GetPlayer(int id);

    /// <summary>
    /// Gets a session using its case-sensitive token.
    /// </summary>
    /// <param name="token">The session token.</param>
    /// <returns>The connected session, or null.</returns>
    IPlayer? GetPlayer(string token);

    /// <summary>
    /// Gets a snapshot of all sessions for an account.
    /// </summary>
    /// <param name="id">The account ID.</param>
    /// <returns>The connected sessions.</returns>
    IReadOnlyList<IPlayer> GetPlayerAllInstances(int id);

    /// <summary>
    /// Gets a snapshot of all connected sessions.
    /// </summary>
    /// <returns>The connected sessions.</returns>
    IReadOnlyList<IPlayer> GetAllPlayers();

    /// <summary>
    /// Starts watching a connected session, replacing the source's previous target.
    /// </summary>
    /// <param name="source">The connected watcher.</param>
    /// <param name="target">The connected target, which must be a different session.</param>
    void UserWatchingPlayer(IPlayer source, IPlayer target);

    /// <summary>
    /// Stops watching the source's current target.
    /// </summary>
    /// <param name="source">The watcher.</param>
    /// <param name="expectedTarget">Only stop this target when provided.</param>
    /// <returns>Whether a spectator relationship was removed.</returns>
    bool UserStoppedWatchingPlayer(IPlayer source, IPlayer? expectedTarget = null);

    /// <summary>
    /// Gets the session currently watched by the source.
    /// </summary>
    /// <param name="source">The watcher.</param>
    /// <returns>The watched session, or null.</returns>
    IPlayer? GetSpectatingPlayer(IPlayer source);

    /// <summary>
    /// Gets a snapshot of sessions watching the target.
    /// </summary>
    /// <param name="target">The watched session.</param>
    /// <returns>The connected watchers.</returns>
    IReadOnlyList<IPlayer> GetWatchingPlayers(IPlayer target);

    /// <summary>
    /// Updates a registered player's activity and broadcasts it to visible sessions.
    /// </summary>
    /// <param name="player">The source session.</param>
    /// <param name="activity">The new activity.</param>
    /// <param name="mods">The stable mod flags.</param>
    void ChangePlayerActivity(IPlayer player, Activity activity, LegacyMods mods = LegacyMods.None);

    /// <summary>
    /// Sends replay payloads only to sessions currently watching this source.
    /// </summary>
    /// <param name="player">The broadcasting session.</param>
    /// <param name="data">The frame bundle payload.</param>
    void BroadcastUserSentFrames(IPlayer player, byte[] data);

    /// <summary>
    /// Notifies a spectator's current host and fellow spectators about a playback failure.
    /// </summary>
    /// <param name="source">The spectator reporting the failure.</param>
    void UserCannotSpectate(IPlayer source);
}
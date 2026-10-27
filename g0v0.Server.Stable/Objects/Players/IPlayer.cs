// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Objects.States;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Server;
using osu.Game.Beatmaps.Legacy;

namespace g0v0.Server.Stable.Objects.Players;

/// <summary>
/// Represents one connected stable client session.
/// </summary>
public interface IPlayer
{
    /// <summary>
    /// Gets the account ID.
    /// </summary>
    int Id { get; }

    /// <summary>
    /// Gets the immutable session token.
    /// </summary>
    string Token { get; }

    /// <summary>
    /// Gets or sets the activity supplied by the client's change-action packet.
    /// </summary>
    Activity Activity { get; set; }

    /// <summary>
    /// Gets or sets the automatic private-message reply used while this session is away.
    /// </summary>
    string? AwayMessage { get; set; }

    /// <summary>
    /// Gets a value indicating whether this is a tournament client session.
    /// </summary>
    bool IsTourneyClient { get; }

    /// <summary>
    /// Gets or sets a value indicating whether only restricted packet handlers are allowed.
    /// </summary>
    bool IsRestricted { get; set; }

    /// <summary>
    /// Gets or sets the UTC time of the last authenticated packet request.
    /// </summary>
    DateTimeOffset LastReceiveTime { get; set; }

    /// <summary>
    /// Gets a value indicating whether this exact session is registered.
    /// </summary>
    bool IsOnline { get; }

    /// <summary>
    /// Gets or sets the displayed username.
    /// </summary>
    string Name { get; set; }

    /// <summary>
    /// Gets or sets the UTC offset in hours.
    /// </summary>
    int UtcOffset { get; set; }

    /// <summary>
    /// Gets or sets the stable country identifier.
    /// </summary>
    byte CountryCode { get; set; }

    /// <summary>
    /// Gets or sets the visible client privileges.
    /// </summary>
    ClientPrivileges Privileges { get; set; }

    /// <summary>
    /// Gets or sets the original stable mod flags.
    /// </summary>
    LegacyMods Mods { get; set; }

    /// <summary>
    /// Gets or sets the client's requested presence subscription filter.
    /// </summary>
    PresenceFilter PresenceFilter { get; set; }

    /// <summary>
    /// Gets the score statistics used when building status packets.
    /// </summary>
    PacketUserStats Statistics { get; }

    /// <summary>
    /// Registers this session and announces it to other clients.
    /// </summary>
    /// <returns>A completed action task.</returns>
    Task Online();

    /// <summary>
    /// Unregisters this exact session and clears its spectator relationships.
    /// </summary>
    /// <param name="isKicked">Whether a replacement session caused the disconnect.</param>
    /// <returns>A completed action task.</returns>
    Task Offline(bool isKicked = false);

    /// <summary>
    /// Updates activity and broadcasts the new stable status.
    /// </summary>
    /// <param name="activity">The new activity.</param>
    /// <param name="mods">The original stable mod flags.</param>
    /// <returns>A completed action task.</returns>
    Task ChangePlayerActivityAsync(Activity activity, LegacyMods mods = LegacyMods.None);

    /// <summary>
    /// Starts watching another connected session.
    /// </summary>
    /// <param name="target">The target session.</param>
    /// <returns>A completed action task.</returns>
    Task WatchPlayer(IPlayer target);

    /// <summary>
    /// Stops watching, optionally only if the current target still matches.
    /// </summary>
    /// <param name="target">The expected target, or null to stop the current target.</param>
    /// <returns>A completed action task.</returns>
    Task StopWatchPlayer(IPlayer? target = null);

    /// <summary>
    /// Sends replay frames to the current spectators.
    /// </summary>
    /// <param name="frames">The replay frame bundle.</param>
    /// <returns>A completed action task.</returns>
    Task SendFrames(ReplayFrameBundle frames);

    /// <summary>
    /// Notifies the current host and fellow spectators that spectating failed.
    /// </summary>
    /// <returns>A completed action task.</returns>
    Task CannotSpectate();

    /// <summary>
    /// Queues a serialized packet snapshot for the next HTTP poll.
    /// </summary>
    /// <param name="packet">The server packet.</param>
    void Enqueue(IPacket packet);

    /// <summary>
    /// Drains the queued frames as one atomic batch.
    /// </summary>
    /// <returns>The serialized frames, or an empty array.</returns>
    byte[] Dequeue();

    /// <summary>
    /// Builds a snapshot of this player's presence.
    /// </summary>
    /// <returns>The presence packet.</returns>
    PacketUserPresence GetPresence();

    /// <summary>
    /// Builds a snapshot of this player's activity and statistics.
    /// </summary>
    /// <returns>The stats packet.</returns>
    PacketUserStats GetStats();

    /// <summary>
    /// Queues an online announcement for another session.
    /// </summary>
    /// <param name="player">The session being announced.</param>
    void OnPlayerOnline(IPlayer player);

    /// <summary>
    /// Queues an offline announcement or a replacement reconnect request.
    /// </summary>
    /// <param name="player">The disconnected session.</param>
    /// <param name="isKicked">Whether this session was replaced.</param>
    void OnPlayerOffline(IPlayer player, bool isKicked = false);

    /// <summary>
    /// Queues another player's updated status.
    /// </summary>
    /// <param name="player">The player whose activity changed.</param>
    void OnPlayerChangeActivity(IPlayer player);

    /// <summary>
    /// Queues a spectator-joined event.
    /// </summary>
    /// <param name="source">The new spectator.</param>
    void OnWatched(IPlayer source);

    /// <summary>
    /// Queues a spectator-left event.
    /// </summary>
    /// <param name="source">The departing spectator.</param>
    void OnWatchedStopped(IPlayer source);

    /// <summary>
    /// Queues an event for another spectator joining the same host.
    /// </summary>
    /// <param name="source">The new fellow spectator.</param>
    void OnFellowSpectatorJoined(IPlayer source);

    /// <summary>
    /// Queues an event for another spectator leaving the same host.
    /// </summary>
    /// <param name="source">The departing fellow spectator.</param>
    void OnFellowSpectatorLeft(IPlayer source);

    /// <summary>
    /// Queues replay data received from the watched player.
    /// </summary>
    /// <param name="data">The complete replay payload without its packet header.</param>
    void OnUserSentFrames(byte[] data);

    /// <summary>
    /// Queues a notification that the watched session disconnected.
    /// </summary>
    /// <param name="player">The disconnected host.</param>
    void OnWatchedPlayerOffline(IPlayer player);
}
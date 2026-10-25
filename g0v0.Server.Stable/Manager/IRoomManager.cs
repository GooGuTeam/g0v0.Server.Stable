// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Objects.Rooms;
using g0v0.Server.Stable.Packets;

namespace g0v0.Server.Stable.Manager;

/// <summary>
/// Owns live Stable rooms and serializes membership and room commands.
/// </summary>
public interface IRoomManager
{
    /// <summary>
    /// Gets a live room by its Stable protocol identifier.
    /// </summary>
    /// <param name="matchId">The Stable protocol room identifier.</param>
    /// <returns>The current room, or null.</returns>
    IRoom? GetRoom(int matchId);

    /// <summary>
    /// Gets the room occupied by this exact session.
    /// </summary>
    /// <param name="player">The acting session.</param>
    /// <returns>The current room, or null.</returns>
    IRoom? GetPlayerRoom(IPlayer player);

    /// <summary>
    /// Gets a snapshot of the live room registry.
    /// </summary>
    /// <returns>A room registry snapshot.</returns>
    IReadOnlyList<IRoom> GetAllRooms();

    /// <summary>
    /// Creates and joins a room, assigning server-owned identifiers and slots.
    /// </summary>
    /// <param name="host">The creating session.</param>
    /// <param name="settings">The untrusted client settings.</param>
    /// <returns>A task representing completion of the operation.</returns>
    Task CreateRoomAsync(IPlayer host, Match settings);

    /// <summary>
    /// Joins an existing live room after checking its password and capacity.
    /// </summary>
    /// <param name="player">The acting session.</param>
    /// <param name="matchId">The Stable protocol room identifier.</param>
    /// <param name="password">The supplied room password.</param>
    /// <returns>A task representing completion of the operation.</returns>
    Task JoinRoomAsync(IPlayer player, int matchId, string password);

    /// <summary>
    /// Leaves the current room and persists participation.
    /// </summary>
    /// <param name="player">The acting session.</param>
    /// <returns>A task representing completion of the operation.</returns>
    Task LeaveRoomAsync(IPlayer player);

    /// <summary>
    /// Removes room, observer, and lobby memberships for this exact session.
    /// </summary>
    /// <param name="player">The acting session.</param>
    /// <returns>A task representing completion of the operation.</returns>
    Task DisconnectAsync(IPlayer player);

    /// <summary>
    /// Subscribes to the lobby and sends password-redacted room snapshots.
    /// </summary>
    /// <param name="player">The acting session.</param>
    /// <returns>A task representing completion of the operation.</returns>
    Task JoinLobbyAsync(IPlayer player);

    /// <summary>
    /// Unsubscribes from lobby updates.
    /// </summary>
    /// <param name="player">The acting session.</param>
    void LeaveLobby(IPlayer player);

    /// <summary>
    /// Runs a command under the current room lock, rechecking session membership.
    /// </summary>
    /// <param name="player">The acting session.</param>
    /// <param name="command">The operation to run while membership is valid.</param>
    /// <returns>A task representing completion of the operation.</returns>
    Task ExecuteAsync(IPlayer player, Func<IRoom, Task> command);

    /// <summary>
    /// Sends a password-redacted room snapshot to an authorized tournament client.
    /// </summary>
    /// <param name="player">The acting session.</param>
    /// <param name="matchId">The Stable protocol room identifier.</param>
    /// <returns>A task representing completion of the operation.</returns>
    Task SendRoomInfoAsync(IPlayer player, int matchId);

    /// <summary>
    /// Adds or removes a tournament observer without occupying a player slot.
    /// </summary>
    /// <param name="player">The acting session.</param>
    /// <param name="matchId">The Stable protocol room identifier.</param>
    /// <param name="join">Whether to join or leave the observer channel.</param>
    /// <returns>A task representing completion of the operation.</returns>
    Task WatchRoomAsync(IPlayer player, int matchId, bool join);

    /// <summary>
    /// Invites an online player to the sender's current room.
    /// </summary>
    /// <param name="player">The acting session.</param>
    /// <param name="userId">The invited account identifier.</param>
    /// <returns>A task representing completion of the operation.</returns>
    Task InviteAsync(IPlayer player, int userId);
}
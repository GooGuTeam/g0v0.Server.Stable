// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Stable.Packets;

/// <summary>
/// Wire values for UserAction.
/// </summary>
public enum UserAction : byte
{
    /// <summary>
    /// Idle.
    /// </summary>
    Idle = 0,

    /// <summary>
    /// Afk.
    /// </summary>
    Afk = 1,

    /// <summary>
    /// Playing.
    /// </summary>
    Playing = 2,

    /// <summary>
    /// Editing.
    /// </summary>
    Editing = 3,

    /// <summary>
    /// Modding.
    /// </summary>
    Modding = 4,

    /// <summary>
    /// Multiplayer.
    /// </summary>
    Multiplayer = 5,

    /// <summary>
    /// Watching.
    /// </summary>
    Watching = 6,

    /// <summary>
    /// Unknown.
    /// </summary>
    Unknown = 7,

    /// <summary>
    /// Testing.
    /// </summary>
    Testing = 8,

    /// <summary>
    /// Submitting.
    /// </summary>
    Submitting = 9,

    /// <summary>
    /// Paused.
    /// </summary>
    Paused = 10,

    /// <summary>
    /// Lobby.
    /// </summary>
    Lobby = 11,

    /// <summary>
    /// Multiplaying.
    /// </summary>
    Multiplaying = 12,

    /// <summary>
    /// OsuDirect.
    /// </summary>
    OsuDirect = 13
}
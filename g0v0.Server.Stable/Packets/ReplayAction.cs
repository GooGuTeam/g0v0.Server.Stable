// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Stable.Packets;

/// <summary>
/// Wire values for ReplayAction.
/// </summary>
public enum ReplayAction : byte
{
    /// <summary>
    /// Standard.
    /// </summary>
    Standard = 0,

    /// <summary>
    /// NewSong.
    /// </summary>
    NewSong = 1,

    /// <summary>
    /// Skip.
    /// </summary>
    Skip = 2,

    /// <summary>
    /// Completion.
    /// </summary>
    Completion = 3,

    /// <summary>
    /// Fail.
    /// </summary>
    Fail = 4,

    /// <summary>
    /// Pause.
    /// </summary>
    Pause = 5,

    /// <summary>
    /// Unpause.
    /// </summary>
    Unpause = 6,

    /// <summary>
    /// SongSelect.
    /// </summary>
    SongSelect = 7,

    /// <summary>
    /// WatchingOther.
    /// </summary>
    WatchingOther = 8
}
// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Stable.Packets;

/// <summary>
/// Wire bit flags for SlotStatus.
/// </summary>
[Flags]
public enum SlotStatus : byte
{
    /// <summary>
    /// Open.
    /// </summary>
    Open = 1,

    /// <summary>
    /// Locked.
    /// </summary>
    Locked = 2,

    /// <summary>
    /// NotReady.
    /// </summary>
    NotReady = 4,

    /// <summary>
    /// Ready.
    /// </summary>
    Ready = 8,

    /// <summary>
    /// NoMap.
    /// </summary>
    NoMap = 16,

    /// <summary>
    /// Playing.
    /// </summary>
    Playing = 32,

    /// <summary>
    /// Complete.
    /// </summary>
    Complete = 64,

    /// <summary>
    /// Quit.
    /// </summary>
    Quit = 128
}
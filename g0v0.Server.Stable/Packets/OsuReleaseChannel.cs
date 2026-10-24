// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Stable.Packets;

/// <summary>
/// Identifies an osu! client release channel.
/// </summary>
public enum OsuReleaseChannel
{
    /// <summary>
    /// The stable release channel.
    /// </summary>
    Stable,

    /// <summary>
    /// The beta release channel (deprecated).
    /// </summary>
    Beta,

    /// <summary>
    /// The cutting-edge release channel.
    /// </summary>
    CuttingEdge,

    /// <summary>
    /// The development release channel.
    /// </summary>
    Dev,

    /// <summary>
    /// The tournament client release channel.
    /// </summary>
    Tourney
}
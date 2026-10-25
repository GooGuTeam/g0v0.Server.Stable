// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Manager;

/// <summary>
/// Identifies the exact session removed from the player registry.
/// </summary>
/// <param name="player">The disconnected session.</param>
public sealed class PlayerRemovedEventArgs(IPlayer player) : EventArgs
{
    /// <summary>
    /// Gets the disconnected session.
    /// </summary>
    public IPlayer Player { get; } = player;
}
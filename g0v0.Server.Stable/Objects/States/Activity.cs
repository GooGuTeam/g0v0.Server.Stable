// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Packets;
using osu.Game.Rulesets.Mods;

namespace g0v0.Server.Stable.Objects.States;

/// <summary>
/// Stores the activity reported by a stable client's change-action packet.
/// </summary>
public class Activity
{
    /// <summary>
    /// Gets or sets the current user action.
    /// </summary>
    public UserAction Action { get; set; }

    /// <summary>
    /// Gets or sets the action's description.
    /// </summary>
    public string InfoText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current beatmap's hash.
    /// </summary>
    public string MapHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the selected mods.
    /// </summary>
    public Mod[] Mods { get; set; } = [];

    /// <summary>
    /// Gets or sets the legacy game mode.
    /// </summary>
    public byte Mode { get; set; }

    /// <summary>
    /// Gets or sets the current beatmap ID.
    /// </summary>
    public int MapId { get; set; }
}
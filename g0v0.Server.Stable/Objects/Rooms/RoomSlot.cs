// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Packets;
using osu.Game.Beatmaps.Legacy;

namespace g0v0.Server.Stable.Objects.Rooms;

/// <summary>
/// Holds one server-owned slot. All mutations require its room's lock.
/// </summary>
public sealed class RoomSlot
{
    /// <summary>Gets the occupying session.</summary>
    public IPlayer? Player { get; internal set; }

    /// <summary>Gets the current slot status.</summary>
    public SlotStatus Status { get; internal set; } = SlotStatus.Open;

    /// <summary>Gets the selected team.</summary>
    public MatchTeam Team { get; internal set; }

    /// <summary>Gets the personal free mods.</summary>
    public LegacyMods Mods { get; internal set; }

    /// <summary>Gets a value indicating whether this player loaded the current round.</summary>
    public bool Loaded { get; internal set; }

    /// <summary>Gets a value indicating whether this player voted to skip.</summary>
    public bool Skipped { get; internal set; }

    /// <summary>Gets a value indicating whether this player failed the current round.</summary>
    public bool Failed { get; internal set; }

    /// <summary>Gets the latest parsed score snapshot for this round.</summary>
    public ScoreFrame? Score { get; internal set; }

    internal void Reset(SlotStatus status = SlotStatus.Open)
    {
        Player = null;
        Status = status;
        Team = MatchTeam.Neutral;
        Mods = LegacyMods.None;
        ResetGameplay();
    }

    internal void ResetGameplay()
    {
        Loaded = false;
        Skipped = false;
        Failed = false;
        Score = null;
    }
}
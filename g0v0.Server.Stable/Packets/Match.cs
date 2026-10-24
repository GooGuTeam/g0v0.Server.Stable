// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using osu.Game.Beatmaps.Legacy;

namespace g0v0.Server.Stable.Packets;

/// <summary>
/// The multiplayer room state a match packet carries.
/// </summary>
/// <remarks>
/// Most members are serialized unconditionally. <see cref="Password"/>, <see cref="SlotUserIds"/> and
/// <see cref="SlotMods"/> are conditional and are described on the member itself.
/// </remarks>
public class Match
{
    /// <summary>
    /// Gets or sets the room identifier the client created.
    /// </summary>
    public short Id { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the room is in a match.
    /// </summary>
    public bool InProgress { get; set; }

    /// <summary>
    /// Gets or sets the room type (0 = standard, 1 = free mods, 2 = tag coop, ...).
    /// </summary>
    public sbyte Type { get; set; }

    /// <summary>
    /// Gets or sets the mods every slot inherits unless free mods are enabled.
    /// </summary>
    public LegacyMods Mods { get; set; }

    /// <summary>
    /// Gets or sets the room name shown in the match listing.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the room password.
    /// </summary>
    /// <remarks>
    /// A <see langword="null"/> value is serialized as the single byte <c>0x00</c> (no password), while
    /// an empty string is serialized as <c>0x0B 0x00</c> (a password that exists but is hidden from
    /// this recipient).
    /// </remarks>
    [OsuField(DistinguishEmptyString = true)]
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets the artist, title and difficulty of the selected beatmap.
    /// </summary>
    public string MapName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the selected beatmap ID.
    /// </summary>
    public int MapId { get; set; }

    /// <summary>
    /// Gets or sets the checksum of the selected beatmap.
    /// </summary>
    public string MapMd5 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the status flags of the sixteen slots.
    /// </summary>
    [OsuField(ArrayLength = 16)]
    public SlotStatus[] SlotStatuses { get; set; } = new SlotStatus[16];

    /// <summary>
    /// Gets or sets the team of the sixteen slots.
    /// </summary>
    [OsuField(ArrayLength = 16)]
    public MatchTeam[] SlotTeams { get; set; } = new MatchTeam[16];

    /// <summary>
    /// Gets or sets the user ID of each slot.
    /// </summary>
    /// <remarks>
    /// An entry is only serialized for an occupied slot; a <see langword="null"/> entry marks an empty
    /// slot that contributes no bytes, which is what keeps the fixed sixteen-slot layout readable.
    /// </remarks>
    [OsuField(ArrayLength = 16, ElementFlags = nameof(SlotStatuses), ElementMask = 124)]
    public int?[] SlotUserIds { get; set; } = new int?[16];

    /// <summary>
    /// Gets or sets the user ID of the room host.
    /// </summary>
    public int HostId { get; set; }

    /// <summary>
    /// Gets or sets the ruleset of the room.
    /// </summary>
    public sbyte Mode { get; set; }

    /// <summary>
    /// Gets or sets the win condition (0 = score, 1 = accuracy, 2 = combo, 3 = score v2).
    /// </summary>
    public MatchWinCondition WinCondition { get; set; }

    /// <summary>
    /// Gets or sets the team mode (0 = head to head, 1 = tag coop, 2 = team versus, 3 = tag team versus).
    /// </summary>
    public MatchTeamType TeamType { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether each slot may pick its own mods.
    /// </summary>
    public bool FreeMods { get; set; }

    /// <summary>
    /// Gets or sets the per-slot mods.
    /// </summary>
    /// <remarks>
    /// The array is only serialized when <see cref="FreeMods"/> is set, so a room without free mods
    /// ends the packet after the previous field.
    /// </remarks>
    [OsuField(ArrayLength = 16, Condition = nameof(FreeMods))]
    public LegacyMods[]? SlotMods { get; set; }

    /// <summary>
    /// Gets or sets the random seed the client uses to order the beatmap and slot shuffle.
    /// </summary>
    public int Seed { get; set; }
}
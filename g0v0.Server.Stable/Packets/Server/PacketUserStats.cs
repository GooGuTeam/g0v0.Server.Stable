// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using osu.Game.Beatmaps.Legacy;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S11 USER_STATS. Payload members are declared in wire order.
/// </summary>
[OsuPacket(11)]
public class PacketUserStats : IPacket
{
    /// <summary>
    /// Gets or sets UserId.
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Gets or sets Action.
    /// </summary>
    public UserAction Action { get; set; }

    /// <summary>
    /// Gets or sets InfoText.
    /// </summary>
    public string InfoText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets MapHash.
    /// </summary>
    public string MapHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets Mods.
    /// </summary>
    public LegacyMods Mods { get; set; }

    /// <summary>
    /// Gets or sets Mode.
    /// </summary>
    public byte Mode { get; set; }

    /// <summary>
    /// Gets or sets MapId.
    /// </summary>
    public int MapId { get; set; }

    /// <summary>
    /// Gets or sets RankedScore.
    /// </summary>
    public long RankedScore { get; set; }

    /// <summary>
    /// Gets or sets the payload value. Wire accuracy is a fraction between 0 and 1.
    /// </summary>
    public float Accuracy { get; set; }

    /// <summary>
    /// Gets or sets PlayCount.
    /// </summary>
    public int PlayCount { get; set; }

    /// <summary>
    /// Gets or sets TotalScore.
    /// </summary>
    public long TotalScore { get; set; }

    /// <summary>
    /// Gets or sets Rank.
    /// </summary>
    public int Rank { get; set; }

    /// <summary>
    /// Gets or sets Pp.
    /// </summary>
    public ushort Pp { get; set; }
}
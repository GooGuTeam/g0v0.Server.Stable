// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets;

/// <summary>
/// The score snapshot a client attaches to a replay frame bundle or a multiplayer score update.
/// </summary>
/// <remarks>
/// The two <c>double</c> fields are only serialized when <see cref="ScoreV2"/> is set; a score V1
/// client must therefore leave them at <see langword="null"/> to keep the frame length correct.
/// </remarks>
public class ScoreFrame
{
    /// <summary>
    /// Gets or sets the sample timestamp in milliseconds from the start of the play.
    /// </summary>
    public int Time { get; set; }

    /// <summary>
    /// Gets or sets the per-player score identifier the server assigned for this play.
    /// </summary>
    public byte Id { get; set; }

    /// <summary>
    /// Gets or sets the number of 300 (great) judgements.
    /// </summary>
    public ushort Num300 { get; set; }

    /// <summary>
    /// Gets or sets the number of 100 (ok) judgements.
    /// </summary>
    public ushort Num100 { get; set; }

    /// <summary>
    /// Gets or sets the number of 50 (meh) judgements.
    /// </summary>
    public ushort Num50 { get; set; }

    /// <summary>
    /// Gets or sets the ruleset-specific strong-hit count (geki, or the mania equivalent).
    /// </summary>
    public ushort NumGeki { get; set; }

    /// <summary>
    /// Gets or sets the ruleset-specific combo-bonus count (katu, or the mania equivalent).
    /// </summary>
    public ushort NumKatu { get; set; }

    /// <summary>
    /// Gets or sets the number of missed judgements.
    /// </summary>
    public ushort NumMiss { get; set; }

    /// <summary>
    /// Gets or sets the total score at this sample.
    /// </summary>
    public int TotalScore { get; set; }

    /// <summary>
    /// Gets or sets the highest combo reached during the play.
    /// </summary>
    public ushort MaxCombo { get; set; }

    /// <summary>
    /// Gets or sets the combo at this sample.
    /// </summary>
    public ushort CurrentCombo { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the play so far has no misses or low judgements.
    /// </summary>
    public bool Perfect { get; set; }

    /// <summary>
    /// Gets or sets the current health, in the <c>0</c>-<c>200</c> range the client displays.
    /// </summary>
    public byte CurrentHp { get; set; }

    /// <summary>
    /// Gets or sets the tag byte the client echoes back, used to pair score frames of one submission.
    /// </summary>
    public byte TagByte { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the play used the score V2 ruleset.
    /// </summary>
    public bool ScoreV2 { get; set; }

    /// <summary>
    /// Gets or sets the combo-based share of the score. Present only when <see cref="ScoreV2"/> is set.
    /// </summary>
    [OsuField(Condition = nameof(ScoreV2))]
    public double? ComboPortion { get; set; }

    /// <summary>
    /// Gets or sets the bonus-based share of the score. Present only when <see cref="ScoreV2"/> is set.
    /// </summary>
    [OsuField(Condition = nameof(ScoreV2))]
    public double? BonusPortion { get; set; }
}
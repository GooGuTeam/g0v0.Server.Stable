// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets;

/// <summary>
/// A batch of replay frames the client sends while spectating, or receives while watching someone.
/// </summary>
public class ReplayFrameBundle
{
    /// <summary>
    /// Gets or sets the ruleset-specific extra value the client attaches to the batch.
    /// </summary>
    public int Extra { get; set; }

    /// <summary>
    /// Gets or sets the number of frames in <see cref="Frames"/>, which also decides how many are read.
    /// </summary>
    public ushort FrameCount { get; set; }

    /// <summary>
    /// Gets or sets the cursor samples of this batch.
    /// </summary>
    [OsuField(LengthFrom = nameof(FrameCount))]
    public ReplayFrame[] Frames { get; set; } = [];

    /// <summary>
    /// Gets or sets the action the bundle announces, such as a pause, a skip or the end of the play.
    /// </summary>
    public ReplayAction Action { get; set; }

    /// <summary>
    /// Gets or sets the score snapshot attached to the bundle.
    /// </summary>
    public ScoreFrame Score { get; set; } = new();

    /// <summary>
    /// Gets or sets the sequence number of the bundle, used by the spectator to drop duplicates.
    /// </summary>
    public ushort Sequence { get; set; }
}
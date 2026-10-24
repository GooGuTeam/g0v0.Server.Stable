// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Stable.Packets;

/// <summary>
/// Wire values for MatchWinCondition.
/// </summary>
public enum MatchWinCondition : byte
{
    /// <summary>
    /// Score.
    /// </summary>
    Score = 0,

    /// <summary>
    /// Accuracy.
    /// </summary>
    Accuracy = 1,

    /// <summary>
    /// Combo.
    /// </summary>
    Combo = 2,

    /// <summary>
    /// ScoreV2.
    /// </summary>
    ScoreV2 = 3
}
// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Stable.Packets;

/// <summary>
/// Wire values for MatchTeamType.
/// </summary>
public enum MatchTeamType : byte
{
    /// <summary>
    /// HeadToHead.
    /// </summary>
    HeadToHead = 0,

    /// <summary>
    /// TagCoop.
    /// </summary>
    TagCoop = 1,

    /// <summary>
    /// TeamVs.
    /// </summary>
    TeamVs = 2,

    /// <summary>
    /// TagTeamVs.
    /// </summary>
    TagTeamVs = 3
}
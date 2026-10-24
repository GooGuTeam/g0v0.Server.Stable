// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Stable.Packets;

/// <summary>
/// Wire bit flags for ClientPrivileges.
/// </summary>
[Flags]
public enum ClientPrivileges
{
    /// <summary>
    /// Player.
    /// </summary>
    Player = 1,

    /// <summary>
    /// Moderator.
    /// </summary>
    Moderator = 2,

    /// <summary>
    /// Supporter.
    /// </summary>
    Supporter = 4,

    /// <summary>
    /// Owner.
    /// </summary>
    Owner = 8,

    /// <summary>
    /// Developer.
    /// </summary>
    Developer = 16
}
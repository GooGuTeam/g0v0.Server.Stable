// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Stable.Packets;

/// <summary>
/// Wire values for LoginFailureReason.
/// </summary>
public enum LoginFailureReason
{
    /// <summary>
    /// AuthenticationFailed.
    /// </summary>
    AuthenticationFailed = -1,

    /// <summary>
    /// OldClient.
    /// </summary>
    OldClient = -2,

    /// <summary>
    /// Banned.
    /// </summary>
    Banned = -3,

    /// <summary>
    /// ErrorOccurred.
    /// </summary>
    ErrorOccurred = -5,

    /// <summary>
    /// NeedsSupporter.
    /// </summary>
    NeedsSupporter = -6,

    /// <summary>
    /// PasswordReset.
    /// </summary>
    PasswordReset = -7,

    /// <summary>
    /// RequiresVerification.
    /// </summary>
    RequiresVerification = -8
}
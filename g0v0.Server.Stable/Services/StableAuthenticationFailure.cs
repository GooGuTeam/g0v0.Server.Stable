// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Stable.Services;

/// <summary>
/// Describes why a stable credential pair was rejected.
/// </summary>
public enum StableAuthenticationFailure
{
    /// <summary>
    /// The credentials were accepted.
    /// </summary>
    None,

    /// <summary>
    /// The submitted username or password hash is malformed.
    /// </summary>
    InvalidRequest,

    /// <summary>
    /// The username does not exist, or the password does not match.
    /// </summary>
    IncorrectCredentials,

    /// <summary>
    /// The account exists but is a bot and may not log in.
    /// </summary>
    AccountDisabled
}
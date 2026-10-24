// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;

namespace g0v0.Server.Stable.Services;

/// <summary>
/// Holds the outcome of a stable authentication attempt.
/// </summary>
/// <param name="User">The authenticated user, or <see langword="null"/> when the attempt failed.</param>
/// <param name="Failure">The reason the attempt failed, or <see cref="StableAuthenticationFailure.None"/>.</param>
public readonly record struct StableAuthenticationResult(User? User, StableAuthenticationFailure Failure)
{
    /// <summary>
    /// Gets a value indicating whether the attempt succeeded.
    /// </summary>
    public bool Succeeded => User is not null;
}
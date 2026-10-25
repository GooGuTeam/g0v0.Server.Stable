// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Utils;

namespace g0v0.Server.Stable.Services;

/// <summary>
/// Authenticates the stable clients, which identify themselves with a username and the password MD5
/// instead of a bancho session token.
/// </summary>
/// <remarks>
/// The login handshake and the <c>/web/*.php</c> endpoints send the same credential pair, so both go
/// through this service. The web endpoints only need the user, while the login handshake also has to
/// tell "wrong password" apart from "this account may not log in" to pick the right failure token.
/// </remarks>
/// <param name="users">The user repository.</param>
/// <param name="logins">Reads the stored password hashes.</param>
public sealed class StableWebAuthenticator(IUserRepository users, ILoginRepository logins)
{
    /// <summary>
    /// Authenticates a user from the query parameters of a stable web request.
    /// </summary>
    /// <param name="username">The <c>u</c> (or <c>us</c>) query parameter.</param>
    /// <param name="passwordMd5">The <c>h</c> (or <c>ha</c>, <c>p</c>) query parameter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The authenticated user, or <see langword="null"/> when the credentials are wrong.</returns>
    public async Task<User?> AuthenticateAsync(string? username, string? passwordMd5, CancellationToken cancellationToken = default)
        => (await AuthenticateDetailedAsync(username, passwordMd5, cancellationToken).ConfigureAwait(false)).User;

    /// <summary>
    /// Authenticates a client and reports why the credentials were rejected.
    /// </summary>
    /// <param name="username">The submitted username.</param>
    /// <param name="passwordMd5">The submitted password MD5.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The authenticated user, or the rejection reason.</returns>
    public async Task<StableAuthenticationResult> AuthenticateDetailedAsync(
        string? username,
        string? passwordMd5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || !UserPasswordHasher.IsValidDigest(passwordMd5))
        {
            return new StableAuthenticationResult(User: null, StableAuthenticationFailure.InvalidRequest);
        }

        User? user = await users.GetByUsernameAsync(username).ConfigureAwait(false);
        if (user is null)
        {
            return new StableAuthenticationResult(User: null, StableAuthenticationFailure.IncorrectCredentials);
        }

        // A bot never receives a password, so the check runs before the hash is read: the login
        // handshake reports such an account as disabled instead of as a wrong password.
        if (user.IsBot)
        {
            return new StableAuthenticationResult(User: null, StableAuthenticationFailure.AccountDisabled);
        }

        string? hash = await logins.GetPasswordHashAsync(user.Id, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(hash))
        {
            return new StableAuthenticationResult(User: null, StableAuthenticationFailure.IncorrectCredentials);
        }

        // A malformed stored hash is treated as a mismatch by the shared hasher, so a legacy row
        // never turns a login attempt into a server fault.
        return UserPasswordHasher.VerifyDigest(passwordMd5, hash)
            ? new StableAuthenticationResult(User: user, StableAuthenticationFailure.None)
            : new StableAuthenticationResult(User: null, StableAuthenticationFailure.IncorrectCredentials);
    }
}
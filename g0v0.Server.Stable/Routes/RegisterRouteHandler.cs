// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Net;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.GeoIP;
using g0v0.Server.Common.Utils;
using Microsoft.EntityFrameworkCore;

namespace g0v0.Server.Stable.Routes;

/// <summary>
/// Serves <c>POST /users</c>: the account creation form of the osu! clients.
/// </summary>
/// <remarks>
/// <para>
/// Request (<c>application/x-www-form-urlencoded</c>), with the bracketed field names the client
/// sends:
/// </para>
/// <code>
/// user[username]=peppy&amp;user[user_email]=peppy@example.com&amp;user[password]=hunter22&amp;check=0
/// </code>
/// <para>
/// <c>check=0</c> (the default) creates the account; any other value only validates the submitted
/// fields, which is what bancho.py's <c>check</c> flag does.
/// </para>
/// <para>
/// Response: <c>ok</c> as <c>text/plain</c> on success. A rejected submission answers with the
/// <c>form_error</c> document both the lazer API and bancho.py return, so the client can attach
/// each message to the matching input:
/// </para>
/// <code>
/// {"form_error":{"user":{"username":["..."],"user_email":[""],"password":[""]}}}
/// </code>
/// <para>
/// The password is hashed the way the stable protocol expects it: the hex MD5 of the plain text is
/// passed through bcrypt, so <see cref="Services.StableWebAuthenticator"/> can verify the MD5 the
/// login handshake sends.
/// </para>
/// </remarks>
/// <param name="users">User persistence.</param>
/// <param name="configs">The shared configuration manager.</param>
/// <param name="geoip">Local country lookup for the new account.</param>
/// <param name="clock">The current UTC time.</param>
/// <param name="logger">The logger.</param>
public sealed class RegisterRouteHandler(
    IUserRepository users,
    ConfigManager configs,
    IGeoIPService geoip,
    TimeProvider clock,
    ILogger<RegisterRouteHandler> logger) : IRouteHandler
{
    private const string SuccessBody = "ok";
    private const string RegistrationDisabledMessage = "In-game registration is disabled. Please register on the website.";
    private const string GenericFailureMessage = "An error occurred while creating your account. Please try again.";
    private const string DefaultCountryCode = "CN";

    private readonly RegistrationConfig _registration = configs.TryGet<RegistrationConfig>() ?? new RegistrationConfig();
    private readonly GameConfig _game = configs.TryGet<GameConfig>() ?? new GameConfig();

    /// <inheritdoc />
    public async Task<IResult> HandleAsync(HttpContext context)
    {
        if (!context.Request.HasFormContentType)
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        IFormCollection form;
        try
        {
            form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            return Results.StatusCode(StatusCodes.Status400BadRequest);
        }

        // Registration is refused before any field is validated, so a disabled endpoint neither
        // reveals which usernames exist nor writes anything.
        if (!_registration.AllowInGameRegistration)
        {
            return FormFailure(StatusCodes.Status400BadRequest, password: [RegistrationDisabledMessage]);
        }

        string username = form["user[username]"].ToString().Trim();
        string email = form["user[user_email]"].ToString().Trim();
        string password = form["user[password]"].ToString();

        RegistrationErrors errors = new();
        errors.Username.AddRange(UserCredentialValidator.ValidateUsername(username, _registration.DisallowedUsernames));
        errors.Email.AddRange(UserCredentialValidator.ValidateEmail(email));
        errors.Password.AddRange(UserCredentialValidator.ValidatePassword(password));

        if (!string.IsNullOrEmpty(username) && await users.UsernameExistsAsync(username).ConfigureAwait(false))
        {
            errors.Username.Add("Username already taken by another player.");
        }

        if (!string.IsNullOrEmpty(email) && await users.GetByEmailAsync(email).ConfigureAwait(false) is not null)
        {
            errors.Email.Add("Email already taken by another player.");
        }

        if (!errors.IsEmpty)
        {
            return FormFailure(StatusCodes.Status422UnprocessableEntity, errors.Username, errors.Email, errors.Password);
        }

        // "Checking values only" is how the client asks whether a submission would be accepted.
        if (!int.TryParse(form["check"], NumberStyles.Integer, CultureInfo.InvariantCulture, out int check) || check == 0)
        {
            IResult? failure = await CreateAccountAsync(context, username, email, password).ConfigureAwait(false);
            if (failure is not null)
            {
                return failure;
            }
        }

        return Results.Text(SuccessBody);
    }

    private static IResult FormFailure(int statusCode, IEnumerable<string>? username = null, IEnumerable<string>? email = null, IEnumerable<string>? password = null, string? message = null)
    {
        return Results.Json(
            new
            {
                form_error = new
                {
                    message,
                    user = new
                    {
                        username = username ?? [],
                        user_email = email ?? [],
                        password = password ?? []
                    }
                }
            },
            statusCode: statusCode);
    }

    private static string ResolveIp(HttpContext context)
    {
        return context.Connection.RemoteIpAddress switch
        {
            null => "127.0.0.1",
            { IsIPv4MappedToIPv6: true } address => address.MapToIPv4().ToString(),
            IPAddress address => address.ToString()
        };
    }

    private async Task<IResult?> CreateAccountAsync(HttpContext context, string username, string email, string password)
    {
        string ip = ResolveIp(context);
        GeoIPLookupResult location = geoip.Lookup(ip);
        DateTimeOffset now = clock.GetUtcNow();

        // The stable client logs in with the MD5 of the password, so the shared hasher covers that
        // digest rather than the plain text.
        User user = new()
        {
            Username = username,
            Email = email,
            PwBcrypt = UserPasswordHasher.HashPassword(password),
            CountryCode = location.CountryIso is { Length: 2 } country ? country.ToUpperInvariant() : DefaultCountryCode,
            JoinDate = now,
            LastVisit = now
        };

        try
        {
            // The account and its per-mode statistics rows are written together, so a retry after a
            // failure cannot be answered with "username already taken".
            await users.CreateAsync(user, UserStatisticsDefaults.GetModes(_game), context.RequestAborted).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            // A concurrent registration that won the unique index is reported like any other
            // server-side failure; the per-field checks above cover the common case.
            logger.LogWarning(exception, "Creating the account {Username} failed.", username);
            return FormFailure(StatusCodes.Status500InternalServerError, message: GenericFailureMessage);
        }

        logger.LogInformation("Registered user {UserId} ({Username}) from {Ip}.", user.Id, user.Username, ip);
        return null;
    }

    private sealed class RegistrationErrors
    {
        public List<string> Username { get; } = [];

        public List<string> Email { get; } = [];

        public List<string> Password { get; } = [];

        public bool IsEmpty => Username.Count == 0 && Email.Count == 0 && Password.Count == 0;
    }
}
// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text;
using g0v0.Server.Common.ClientIp;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.GeoIP;
using g0v0.Server.Stable.Manager;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Server;
using g0v0.Server.Stable.Services;
using osu.Game.Users;

namespace g0v0.Server.Stable.Routes;

/// <summary>
/// Serves <c>POST /</c>: the stable login handshake and the packet polling endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Request: <c>POST /</c> with <c>User-Agent: osu!</c>. Without an <c>osu-token</c> header the body is
/// the three-line login form documented on <see cref="LoginPacket" />; with an <c>osu-token</c>
/// header the body carries framed client packets and is dispatched by <see cref="PacketRouteHandler" />.
/// </para>
/// <code>
/// &lt;username&gt;\n
/// &lt;password md5&gt;\n
/// &lt;version&gt;|&lt;utc offset&gt;|&lt;display city&gt;|&lt;client hashes&gt;|&lt;pm private&gt;\n
/// </code>
/// <para>
/// Response: framed server packets (<c>application/octet-stream</c>) plus a <c>cho-token</c> header
/// carrying the new session token. On success the packets are, in order:
/// </para>
/// <code>
/// S5  PROTOCOL_VERSION     protocol revision
/// S7  USER_ID              authenticated user id
/// S71 PRIVILEGES           client privilege flags
/// S83 FRIENDS_LIST         friend ids
/// S92 SILENCE_END          remaining silence, zero when not silenced
/// S83 USER_PRESENCE        the player's own presence entry
/// S11 USER_STATS           the player's own statistics entry
/// S65 CHANNEL_INFO         one per public channel
/// S89 CHANNEL_INFO_END     terminator of the channel directory
/// </code>
/// <para>
/// The client stays in "Receiving data..." until <c>S89 CHANNEL_INFO_END</c> arrives, so the channel
/// directory is always appended, even when the server has no channels. A rejected login answers
/// <c>S5 USER_ID</c> carrying the failure reason token followed by <c>S7 NOTIFICATION</c> with the
/// message shown to the player.
/// </para>
/// </remarks>
/// <param name="authenticator">Verifies the username and password MD5 pair.</param>
/// <param name="users">User persistence.</param>
/// <param name="relationships">The user friendship repository.</param>
/// <param name="logins">Credential and login session persistence.</param>
/// <param name="players">Connected sessions.</param>
/// <param name="chats">The chat server providing the login channel directory.</param>
/// <param name="configs">The shared configuration manager.</param>
/// <param name="geoip">Local country and ASN lookup.</param>
/// <param name="clientIp">Resolves the real client address behind the reverse proxy.</param>
/// <param name="clock">The current UTC time.</param>
/// <param name="logger">The logger.</param>
public class LoginRouteHandler(
    StableWebAuthenticator authenticator,
    IUserRepository users,
    IRelationshipRepository relationships,
    ILoginRepository logins,
    IPlayerManager players,
    IChatServer chats,
    ConfigManager configs,
    IGeoIPService geoip,
    IClientIpResolver clientIp,
    TimeProvider clock,
    ILogger<LoginRouteHandler> logger) : IRouteHandler
{
    private const int ProtocolVersion = 19;

    private readonly VerificationConfig _verification = configs.Get<VerificationConfig>();

    /// <inheritdoc />
    public async Task<IResult> HandleAsync(HttpContext context)
    {
        if (!string.Equals(context.Request.Headers.UserAgent.ToString(), "osu!", StringComparison.Ordinal))
        {
            return Results.StatusCode(StatusCodes.Status422UnprocessableEntity);
        }

        LoginPacket login;
        try
        {
            using MemoryStream body = new();
            await context.Request.Body.CopyToAsync(body, context.RequestAborted);
            login = LoginPacket.Parse(body.ToArray());
        }
        catch (Exception exception) when (exception is FormatException or DecoderFallbackException)
        {
            return Failure(context, "invalid-request", LoginFailureReason.AuthenticationFailed, "Please restart osu! and try again.");
        }

        if (login.PasswordMd5.Length != 32 || !login.PasswordMd5.All(char.IsAsciiHexDigit)
                                           || login.UtcOffset is < -24 or > 24 || (!login.RunningUnderWine && login.Adapters.All(adapter => adapter.Length == 0))
                                           || login.AdaptersMd5.Length != 32 || !login.AdaptersMd5.All(char.IsAsciiHexDigit)
                                           || login.DiskSignatureMd5.Length != 32 || !login.DiskSignatureMd5.All(char.IsAsciiHexDigit))
        {
            return Failure(context, "invalid-request", LoginFailureReason.AuthenticationFailed, "Invalid login data. Please restart osu!.");
        }

        CancellationToken cancellation = context.RequestAborted;
        DateTimeOffset now = clock.GetUtcNow();
        string ip = clientIp.Resolve(context);

        // The login form and the web endpoints share one credential check, so a password change
        // cannot leave one of them behind. The detailed result is needed here to tell a wrong
        // password apart from a disabled account, which the client words differently.
        StableAuthenticationResult authentication = await authenticator
            .AuthenticateDetailedAsync(login.Username, login.PasswordMd5, cancellation)
            .ConfigureAwait(false);
        User? user = authentication.User;
        bool authenticated = authentication.Succeeded;

        GeoIPLookupResult location = geoip.Lookup(ip);
        LoginLogSession audit = new()
        {
            UserId = user?.Id ?? 0,
            IpAddress = ip,
            UserAgent = context.Request.Headers.UserAgent.ToString(),
            LoginTime = now,
            LoginSuccess = authenticated && !user!.IsBot,
            LoginMethod = "stable_password",
            CountryCode = location.CountryIso,
            CountryName = location.CountryName,
            Asn = location.Asn is >= int.MinValue and <= int.MaxValue ? (int)location.Asn.Value : null,
            Organization = location.Organization?[..Math.Min(location.Organization.Length, 200)]
        };
        if (!authenticated || user == null)
        {
            await logins.RecordAsync(session: null, device: null, audit, cancellation);
            return authentication.Failure == StableAuthenticationFailure.AccountDisabled
                ? Failure(context, "account-disabled", LoginFailureReason.Banned, "This account cannot log in.")
                : Failure(context, "incorrect-credentials", LoginFailureReason.AuthenticationFailed, "Incorrect username or password.");
        }

        bool tourney = login.Version.Stream == OsuReleaseChannel.Tourney;
        if (tourney && !user.IsSupporter)
        {
            audit.LoginSuccess = false;
            await logins.RecordAsync(session: null, device: null, audit, cancellation);
            return Failure(context, "needs-supporter", LoginFailureReason.NeedsSupporter, "A supporter account is required for tournament clients.");
        }

        IPlayer? online = players.GetPlayerAllInstances(user.Id).FirstOrDefault(player => !player.IsTourneyClient);
        if (!tourney && online != null && now - online.LastReceiveTime < TimeSpan.FromSeconds(10))
        {
            audit.LoginSuccess = false;
            await logins.RecordAsync(session: null, device: null, audit, cancellation);
            return Failure(context, "user-already-logged-in", LoginFailureReason.AuthenticationFailed, "User already logged in.");
        }

        string deviceHash = login.GetDeviceHash();
        TrustedDevice? device = await logins.GetDeviceAsync(user.Id, "stable", deviceHash, cancellation);
        LoginSession? previous = await logins.GetSessionAsync(user.Id, "stable", deviceHash, now, cancellation);
        bool trusted = device?.ExpiresAt > now || previous?.IsVerified == true;
        bool verified = !_verification.EnableSessionVerification || trusted;

        LoginSession session = previous is { IsVerified: false } ? previous : new LoginSession { UserId = user.Id, CreatedAt = now };
        session.IpAddress = ip;
        session.UserAgent = login.OsuVersionString;
        session.DeviceHash = deviceHash;
        session.IsNewDevice = !trusted;
        session.IsVerified = verified;
        session.VerifiedAt = verified ? now : null;
        session.ExpiresAt = now.AddHours(24);
        audit.LoginMethod = verified ? "stable_password" : "stable_pending_verification";

        if (!verified)
        {
            // The pending session keeps the device hash, but no device: trust is granted only
            // after the account owner approves this client through the verification endpoint,
            // which creates the trusted device (and attaches it to this session).
            session.Device = null;
            session.DeviceId = null;
            await logins.RecordAsync(session, device: null, audit, cancellation);
            context.Response.Headers["cho-token"] = "verification-required";
            return Results.Bytes(
                PacketConvert.SerializeAll([new PacketUserId(LoginFailureReason.RequiresVerification)]),
                contentType: "application/octet-stream");
        }

        device ??= new TrustedDevice
        {
            UserId = user.Id,
            ClientType = "stable",
            DeviceHash = deviceHash,
            UserAgent = login.OsuVersionString,
            CreatedAt = now
        };
        device.IpAddress = ip;
        device.UserAgent = login.OsuVersionString;
        device.LastUsedAt = now;
        device.ExpiresAt = now.AddDays(_verification.DeviceTrustDurationDays);
        session.Device = device;
        await logins.RecordAsync(session, device, audit, cancellation);

        Player player = new(user.Id, players, isTourneyClient: tourney)
        {
            LastReceiveTime = now,
            Name = user.Username,
            UtcOffset = login.UtcOffset
        };
        ClientPrivileges privileges = user.IsSupporter ? ClientPrivileges.Player | ClientPrivileges.Supporter : ClientPrivileges.Player;
        PacketUserPresence presence = new()
        {
            UserId = user.Id,
            Name = user.Username,
            UtcOffsetPlus24 = checked((byte)(login.UtcOffset + 24)),
            CountryCode = Enum.TryParse(user.CountryCode, ignoreCase: true, out CountryCode country) ? checked((byte)country) : (byte)0
        };
        presence.SetPrivilegesAndMode(privileges, 0);
        player.CountryCode = presence.CountryCode;
        player.Privileges = privileges;
        if (context.RequestServices.GetService<IPacketFacade>() is { } packetFacade)
        {
            await Packets.Client.PacketRequestStatusUpdate.RefreshStatistics(player, 0, packetFacade);
            presence.Rank = player.Statistics.Rank;
        }

        int[] friends = await relationships.GetAllFriendIds(user.Id);

        // The channel directory must be part of the login response: the stable client keeps
        // showing "Receiving data..." until it receives S89 CHANNEL_INFO_END.
        IPacket[] directory = await chats.ConnectAsync(player);
        byte[] response = PacketConvert.SerializeAll([
            new PacketProtocolVersion { Version = ProtocolVersion },
            new PacketUserId { UserId = user.Id },
            new PacketPrivileges { Privileges = privileges },
            new PacketFriendsList { Friends = friends },
            new PacketSilenceEnd(),
            presence,
            player.GetStats(),
            .. directory
        ]);
        user.IsOnline = true;
        user.LastVisit = now;
        user.PmFriendsOnly = login.PmPrivate;
        await users.UpdateAsync(user);
        await player.Online();
        using MemoryStream outgoing = new();
        outgoing.Write(response);
        outgoing.Write(player.Dequeue());
        context.Response.Headers["cho-token"] = player.Token;
        logger.LogInformation(
            "Stable login of user {UserId} from {Ip} succeeded in mode {Mode} (tournament={IsTourney}).",
            user.Id,
            ip,
            login.Version.Stream,
            tourney);
        return Results.Bytes(outgoing.ToArray(), contentType: "application/octet-stream");
    }

    private IResult Failure(HttpContext context, string token, LoginFailureReason reason, string message)
    {
        logger.LogInformation("Rejected a stable login with {Token}: {Message}", token, message);
        context.Response.Headers["cho-token"] = token;
        return Results.Bytes(
            PacketConvert.SerializeAll([
                new PacketUserId(reason),
                new PacketNotification { Message = message }
            ]),
            contentType: "application/octet-stream");
    }
}
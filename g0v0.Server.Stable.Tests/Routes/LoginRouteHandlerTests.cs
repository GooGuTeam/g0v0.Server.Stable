// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Text;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.GeoIP;
using g0v0.Server.Stable.Manager;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Server;
using g0v0.Server.Stable.Routes;
using g0v0.Server.Stable.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace g0v0.Server.Stable.Tests.Routes;

public class LoginRouteHandlerTests
{
    private const string PasswordMd5 = "5f4dcc3b5aa765d61d8327deb882cf99";
    private static readonly string[] ExpectedLoginChannels = ["#osu", "#announce"];
    private Mock<IUserRepository> _users = null!;
    private Mock<ILoginRepository> _logins = null!;
    private Mock<IRelationshipRepository> _relationships = null!;
    private Mock<IGeoIPService> _geoip = null!;
    private ServiceProvider _services = null!;
    private PlayerManager _players = null!;
    private string _configPath = null!;
    private ConfigManager _configs = null!;
    private VerificationConfig _verification = null!;
    private User _user = null!;
    private TrustedDevice? _device;
    private LoginSession? _session;
    private LoginLogSession? _audit;

    [SetUp]
    public void SetUp()
    {
        Mock<IChatRepository> chatRepository = TestChatRepository.Create();
        _services = new ServiceCollection().AddLogging()
            .AddScoped(_ => chatRepository.Object)
            .BuildServiceProvider();
        _players = new PlayerManager();
        _verification = new VerificationConfig();
        _configPath = Path.Combine(Path.GetTempPath(), "stable-Login-config-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(_configPath, "config"));
        File.WriteAllText(Path.Combine(_configPath, "config", "verification.json"), Newtonsoft.Json.JsonConvert.SerializeObject(_verification));
        _configs = new ConfigManager(_configPath);
        _verification = _configs.Get<VerificationConfig>();
        _user = new User { Id = 1, Username = "alice" };
        _device = null;
        _session = null;
        _audit = null;
        _relationships = new Mock<IRelationshipRepository>();
        _relationships.Setup(repository => repository.GetAllFriendIds(1)).ReturnsAsync([]);
        _users = new Mock<IUserRepository>();
        _users.Setup(users => users.GetByUsernameAsync("alice")).ReturnsAsync(_user);
        _logins = new Mock<ILoginRepository>();
        _logins.Setup(logins => logins.GetPasswordHashAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync("$2b$04$abcdefghijklmnopqrstuuvRd6479mlTDECYXOIYMV5.3yuvdazPi");
        _logins.Setup(logins => logins.GetDeviceAsync(1, "stable", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _device);
        _logins.Setup(logins => logins.GetSessionAsync(1, "stable", It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _session);
        _logins.Setup(logins => logins.RecordAsync(It.IsAny<LoginSession?>(), It.IsAny<TrustedDevice?>(), It.IsAny<LoginLogSession>(), It.IsAny<CancellationToken>()))
            .Callback<LoginSession?, TrustedDevice?, LoginLogSession, CancellationToken>((session, device, audit, _) =>
            {
                _session = session;
                _device = device;
                _audit = audit;
            }).Returns(Task.CompletedTask);
        _geoip = new Mock<IGeoIPService>();
        _geoip.Setup(geoip => geoip.Lookup(It.IsAny<string>())).Returns<string>(ip => new GeoIPLookupResult(ip) { CountryIso = "GB", CountryName = "United Kingdom" });
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
        Directory.Delete(_configPath, recursive: true);
    }

    [TestCase("001122334455.AABBCCDDEEFF.")]
    [TestCase("runningunderwine")]
    public async Task NewDeviceIsRecordedWithoutTrustAndCannotEnterTheOnlinePlayerManager(string adapters)
    {
        string clientHash = $"0123456789abcdef0123456789ABCDEF:{adapters}:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA:abcdef0123456789ABCDEF0123456789:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb:";
        string data = $"alice\n{PasswordMd5}\nb20261024|8|1|{clientHash}|1\n";
        DefaultHttpContext context = await Login(data);
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(context.Response.Headers["cho-token"].ToString(), Is.EqualTo("verification-required"));
        Assert.That(_players.GetAllPlayers(), Is.Empty);
        Assert.That(_session!.IsVerified, Is.False);
        Assert.That(_session.DeviceHash, Is.EqualTo(clientHash));
        Assert.That(_session.Device, Is.Null);
        Assert.That(_session.DeviceId, Is.Null);

        // Trust is granted by the verification endpoint, so an unverified login must not
        // leave a trusted-device row behind.
        Assert.That(_device, Is.Null);
        Assert.That(_audit!.LoginMethod, Is.EqualTo("stable_pending_verification"));
        using MemoryStream response = new(((MemoryStream)context.Response.Body).ToArray());
        IPacket[] packets = PacketConvert.DeserializeAll(response, new PacketFacade());
        Assert.That(packets, Has.Length.EqualTo(1));
        Assert.That(packets[0], Is.TypeOf<PacketUserId>());
        Assert.That(((PacketUserId)packets[0]).UserId, Is.EqualTo((int)LoginFailureReason.RequiresVerification));
        _logins.Verify(logins => logins.GetDeviceAsync(1, "stable", clientHash, It.IsAny<CancellationToken>()), Times.Once);
        _logins.Verify(logins => logins.GetSessionAsync(1, "stable", clientHash, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task TrustedDeviceLogsInAndReceivesAValidSessionToken()
    {
        _device = new TrustedDevice { Id = 3, UserId = 1, ClientType = "stable", UserAgent = "osu!", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) };
        DefaultHttpContext context = await Login();
        IPlayer player = _players.GetPlayer(1)!;
        Assert.That(player, Is.Not.Null);
        Assert.That(context.Response.Headers["cho-token"].ToString(), Is.EqualTo(player.Token));
        Assert.That(_session!.IsVerified, Is.True);
        Assert.That(_session.Device, Is.SameAs(_device));
        using MemoryStream response = new(((MemoryStream)context.Response.Body).ToArray());
        IPacket[] loginPackets = PacketConvert.DeserializeAll(response, new PacketFacade());
        Assert.That(loginPackets.OfType<PacketUserId>().Single().UserId, Is.EqualTo(1));

        // The channel directory must end the Login response: without S89 the client keeps
        // showing "Receiving data...".
        Assert.That(
            loginPackets.OfType<PacketChannelInfo>().Select(packet => packet.Channel.Name),
            Is.EquivalentTo(ExpectedLoginChannels));
        Assert.That(loginPackets[^1], Is.TypeOf<PacketChannelInfoEnd>());
        _users.Verify(users => users.UpdateAsync(_user), Times.Once);
    }

    [Test]
    public async Task ALocallyVerifiedPendingSessionAllowsTheNextLogin()
    {
        await Login();
        LoginSession pending = _session!;
        pending.IsVerified = true;
        await Login();
        Assert.That(_players.GetPlayer(1), Is.Not.Null);
        Assert.That(_session!.IsVerified, Is.True);
        Assert.That(_device!.ExpiresAt, Is.GreaterThan(DateTimeOffset.UtcNow));
    }

    [Test]
    public async Task RepeatedPendingLoginsReuseTheUnverifiedSession()
    {
        await Login();
        LoginSession pending = _session!;
        await Login();
        Assert.That(_session, Is.SameAs(pending));
        Assert.That(_session!.IsVerified, Is.False);
        Assert.That(_players.GetAllPlayers(), Is.Empty);
    }

    [Test]
    public async Task DisabledVerificationAutomaticallyVerifiesTheDevice()
    {
        _verification.EnableSessionVerification = false;
        await Login();
        Assert.That(_session!.IsVerified, Is.True);
        Assert.That(_players.GetPlayer(1), Is.Not.Null);
    }

    [Test]
    public async Task InvalidPasswordIsAuditedWithoutCreatingADeviceOrSession()
    {
        DefaultHttpContext context = await Login(Body().Replace(PasswordMd5, new string('0', 32), StringComparison.Ordinal));
        Assert.That(context.Response.Headers["cho-token"].ToString(), Is.EqualTo("incorrect-credentials"));
        Assert.That(_audit!.LoginSuccess, Is.False);
        Assert.That(_device, Is.Null);
        Assert.That(_session, Is.Null);
        Assert.That(_players.GetAllPlayers(), Is.Empty);
    }

    [Test]
    public async Task PlainPasswordBcryptCannotBeVerifiedUsingAStableMd5()
    {
        // Generated with Python bcrypt, the format the password hashes are stored in.
        _logins.Setup(logins => logins.GetPasswordHashAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync("$2b$04$abcdefghijklmnopqrstuughE8Ev8uGFaUgY2cNEySvxngrb/Jzdm");
        DefaultHttpContext context = await Login();
        Assert.That(context.Response.Headers["cho-token"].ToString(), Is.EqualTo("incorrect-credentials"));
        Assert.That(_players.GetAllPlayers(), Is.Empty);
    }

    [Test]
    public async Task MalformedLoginDoesNotQueryTheDatabase()
    {
        DefaultHttpContext context = await Login("invalid");
        Assert.That(context.Response.Headers["cho-token"].ToString(), Is.EqualTo("invalid-request"));
        _users.VerifyNoOtherCalls();
        _logins.VerifyNoOtherCalls();
    }

    [Test]
    public async Task BotsCannotLogIn()
    {
        _user.IsBot = true;
        DefaultHttpContext context = await Login();
        Assert.That(context.Response.Headers["cho-token"].ToString(), Is.EqualTo("account-disabled"));
        Assert.That(_audit!.LoginSuccess, Is.False);
        Assert.That(_players.GetAllPlayers(), Is.Empty);
    }

    [Test]
    public async Task AccountActivityFlagIsNotTreatedAsARestriction()
    {
        _user.IsActive = false;
        await Login();
        Assert.That(_session, Is.Not.Null);
        Assert.That(_audit!.LoginSuccess, Is.True);
    }

    [Test]
    public async Task AnActiveOrdinarySessionIsNotReplaced()
    {
        Player previous = new(1) { LastReceiveTime = DateTimeOffset.UtcNow };
        _players.AddPlayer(previous);
        DefaultHttpContext context = await Login();
        Assert.That(context.Response.Headers["cho-token"].ToString(), Is.EqualTo("user-already-logged-in"));
        Assert.That(_players.GetPlayer(1), Is.SameAs(previous));
        Assert.That(_session, Is.Null);
    }

    [Test]
    public async Task AnInactiveOrdinarySessionCanBeReplaced()
    {
        _verification.EnableSessionVerification = false;
        Player previous = new(1) { LastReceiveTime = DateTimeOffset.UtcNow.AddMinutes(-1) };
        _players.AddPlayer(previous);
        await Login();
        Assert.That(_players.GetPlayer(1), Is.Not.SameAs(previous));
        Assert.That(_session!.IsVerified, Is.True);
    }

    private static string Body() => $"alice\n{PasswordMd5}\nb20261024|8|1|path:adapter.:{new string('a', 32)}:uninstall:{new string('b', 32)}:|1\n";

    private async Task<DefaultHttpContext> Login(string? data = null)
    {
        DefaultHttpContext context = new() { RequestServices = _services };
        context.Request.Headers.UserAgent = "osu!";
        context.Connection.RemoteIpAddress = IPAddress.Parse("81.2.69.142");
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(data ?? Body()));
        context.Response.Body = new MemoryStream();
        ChatServer chats = new(NullLogger<ChatServer>.Instance, _services.GetRequiredService<IServiceScopeFactory>(), _players);
        StableWebAuthenticator authenticator = new(_users.Object, _logins.Object);
        LoginRouteHandler handler = new(
            authenticator,
            _users.Object,
            _relationships.Object,
            _logins.Object,
            _players,
            chats,
            _configs,
            _geoip.Object,
            TimeProvider.System,
            NullLogger<LoginRouteHandler>.Instance);
        IResult result = await handler.HandleAsync(context);
        await result.ExecuteAsync(context);
        return context;
    }
}
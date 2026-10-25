// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Text;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.GeoIP;
using g0v0.Server.Stable.Routes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace g0v0.Server.Stable.Tests.Routes;

public class RegisterRouteHandlerTests
{
    private const string Username = "newplayer";
    private const string Email = "newplayer@example.com";
    private const string Password = "hunter22";

    // The MD5 of the password above, the digest the stable client sends at login.
    private const string PasswordMd5 = "cb95015a436fe976eb38e45455372032";

    private Mock<IUserRepository> _users = null!;
    private Mock<IGeoIPService> _geoip = null!;
    private ServiceProvider _services = null!;
    private User? _created;
    private IReadOnlyList<int>? _modes;
    private string _configPath = null!;
    private ConfigManager _configs = null!;
    private RegistrationConfig _registration = null!;

    [SetUp]
    public void SetUp()
    {
        _configPath = Path.Combine(Path.GetTempPath(), "stable-register-config-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(_configPath, "config"));
        _registration = new RegistrationConfig();
        File.WriteAllText(Path.Combine(_configPath, "config", "registration.json"), Newtonsoft.Json.JsonConvert.SerializeObject(_registration));
        _configs = new ConfigManager(_configPath);
        _registration = _configs.Get<RegistrationConfig>();
        _created = null;
        _modes = null;
        _users = new Mock<IUserRepository>();
        _users.Setup(users => users.CreateAsync(It.IsAny<User>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .Callback<User, IReadOnlyList<int>, CancellationToken>((user, modes, _) =>
            {
                user.Id = 42;
                _created = user;
                _modes = modes;
            }).Returns(Task.CompletedTask);
        _geoip = new Mock<IGeoIPService>();
        _geoip.Setup(geoip => geoip.Lookup(It.IsAny<string>()))
            .Returns<string>(ip => new GeoIPLookupResult(ip) { CountryIso = "GB" });

        // The result types resolve logging and JSON options from the request services.
        _services = new ServiceCollection().AddLogging().BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
        Directory.Delete(_configPath, recursive: true);
    }

    [Test]
    public async Task CreatesTheAccountAndSeedsOneStatisticsRowPerAvailableMode()
    {
        DefaultHttpContext context = await PostFormAsync();

        string payload = await ReadBodyAsync(context);
        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(200));
            Assert.That(payload, Is.EqualTo("ok"));
            Assert.That(_created, Is.Not.Null);
            Assert.That(_created!.Username, Is.EqualTo(Username));
            Assert.That(_created.Email, Is.EqualTo(Email));
            Assert.That(_created.CountryCode, Is.EqualTo("GB"));
            Assert.That(_modes, Is.EqualTo(new List<int> { 0, 1, 2, 3 }));
        });
    }

    [Test]
    public async Task StoresABcryptHashOfThePasswordMd5TheLoginHandshakeSends()
    {
        await PostFormAsync();

        // The stable client authenticates with the MD5 of the password, so the stored hash must
        // verify that digest; a hash of the plain text never matches it.
        Assert.Multiple(() =>
        {
            Assert.That(BCrypt.Net.BCrypt.Verify(PasswordMd5, _created!.PwBcrypt), Is.True);
            Assert.That(BCrypt.Net.BCrypt.Verify(Password, _created.PwBcrypt), Is.False);
        });
    }

    [Test]
    public async Task ReportsEveryInvalidFieldWithoutCreatingTheAccount()
    {
        DefaultHttpContext context = await PostFormAsync(username: "a", email: "not-an-email", password: "short");

        string payload = await ReadBodyAsync(context);
        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(422));
            Assert.That(payload, Does.Contain("Must be 2-15 characters in length."));
            Assert.That(payload, Does.Contain("Invalid email syntax."));
            Assert.That(payload, Does.Contain("Must be 8-32 characters in length."));
            Assert.That(_created, Is.Null);
        });
        _users.Verify(users => users.CreateAsync(It.IsAny<User>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task RejectsATakenUsernameAndATakenEmail()
    {
        _users.Setup(users => users.UsernameExistsAsync(username: Username)).ReturnsAsync(value: true);
        _users.Setup(users => users.GetByEmailAsync(email: Email)).ReturnsAsync(new User { Id = 7, Username = "other", PwBcrypt = "$2b$test" });

        DefaultHttpContext context = await PostFormAsync();

        string payload = await ReadBodyAsync(context);
        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(422));
            Assert.That(payload, Does.Contain("Username already taken by another player."));
            Assert.That(payload, Does.Contain("Email already taken by another player."));
            Assert.That(_created, Is.Null);
        });
    }

    [Test]
    public async Task RejectsAUsernameThatMixesSpacesAndUnderscores()
    {
        DefaultHttpContext context = await PostFormAsync(username: "two words_here");

        string payload = await ReadBodyAsync(context);
        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(422));
            Assert.That(payload, Does.Contain("not both"));
        });
    }

    [Test]
    public async Task RejectsAPasswordThatDoesNotMeetTheSharedStrengthPolicy()
    {
        // Eight characters, but only lowercase: the strength rule refuses it and the client shows
        // the message on the password field.
        DefaultHttpContext context = await PostFormAsync(password: "password");

        string payload = await ReadBodyAsync(context);
        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(422));
            Assert.That(payload, Does.Contain("Must mix at least two of"));
            Assert.That(_created, Is.Null);
        });
    }

    [Test]
    public async Task RefusesTheImpersonationNamesTheLazerServerRefusesByDefault()
    {
        // The default configuration carries the lazer API's banned_name list.
        DefaultHttpContext context = await PostFormAsync(username: "peppy");

        string payload = await ReadBodyAsync(context);
        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(422));
            Assert.That(payload, Does.Contain("Disallowed username"));
            Assert.That(_created, Is.Null);
        });
    }

    [Test]
    public async Task RefusesRegistrationWhenItIsDisabledInTheConfiguration()
    {
        // The configuration manager caches the instance, so mutating it drives the handler.
        _registration.AllowInGameRegistration = false;

        DefaultHttpContext context = await PostFormAsync();

        string payload = await ReadBodyAsync(context);
        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(400));
            Assert.That(payload, Does.Contain("In-game registration is disabled"));
            Assert.That(_created, Is.Null);
        });
    }

    [Test]
    public async Task OnlyValidatesTheSubmissionWhenTheClientSendsANonZeroCheckFlag()
    {
        DefaultHttpContext context = await PostFormAsync(check: "1");

        string payload = await ReadBodyAsync(context);
        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(200));
            Assert.That(payload, Is.EqualTo("ok"));
            Assert.That(_created, Is.Null);
        });
    }

    [Test]
    public async Task ReportsAUniqueIndexRaceAsAServerError()
    {
        _users.Setup(users => users.CreateAsync(It.IsAny<User>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Microsoft.EntityFrameworkCore.DbUpdateException("duplicate"));

        DefaultHttpContext context = await PostFormAsync();

        string payload = await ReadBodyAsync(context);
        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(500));
            Assert.That(payload, Does.Contain("An error occurred while creating your account"));
        });
    }

    [Test]
    public async Task DerivesTheCountryFromGeoIpAndFallsBackWhenTheLookupHasNone()
    {
        _geoip.Setup(geoip => geoip.Lookup(It.IsAny<string>())).Returns(new GeoIPLookupResult("1.2.3.4"));

        await PostFormAsync();

        Assert.That(_created!.CountryCode, Is.EqualTo("CN"));
    }

    [Test]
    public async Task RejectsANonFormRequest()
    {
        DefaultHttpContext context = new() { RequestServices = _services };
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";
        context.Response.Body = new MemoryStream();

        IResult result = await CreateHandler().HandleAsync(context);
        await result.ExecuteAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(415));
    }

    private RegisterRouteHandler CreateHandler()
        => new(_users.Object, _configs, _geoip.Object, TimeProvider.System, NullLogger<RegisterRouteHandler>.Instance);

    private async Task<DefaultHttpContext> PostFormAsync(
        string username = Username,
        string email = Email,
        string password = Password,
        string? check = null)
    {
        DefaultHttpContext context = new() { RequestServices = _services };
        context.Request.Method = "POST";
        context.Connection.RemoteIpAddress = IPAddress.Parse("81.2.69.142");
        context.Response.Body = new MemoryStream();
        Dictionary<string, string> fields = new(StringComparer.Ordinal)
        {
            ["user[username]"] = username,
            ["user[user_email]"] = email,
            ["user[password]"] = password
        };
        if (check != null)
        {
            fields["check"] = check;
        }

        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(fields.ToDictionary(
            pair => pair.Key,
            pair => new Microsoft.Extensions.Primitives.StringValues(pair.Value),
            StringComparer.Ordinal));
        IResult result = await CreateHandler().HandleAsync(context);
        await result.ExecuteAsync(context);
        return context;
    }

    private async Task<string> ReadBodyAsync(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        using StreamReader reader = new(context.Response.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}
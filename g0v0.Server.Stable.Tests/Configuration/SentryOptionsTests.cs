// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Configuration;

namespace g0v0.Server.Stable.Tests.Configuration;

/// <summary>
/// Tests for the Sentry settings managed by <see cref="StableConfig"/>.
/// </summary>
[TestFixture]
public class SentryOptionsTests
{
    private string _basePath = null!;

    [SetUp]
    public void SetUp()
    {
        _basePath = Path.Combine(Path.GetTempPath(), "g0v0-stable-sentry-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_basePath, "config"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_basePath))
        {
            Directory.Delete(_basePath, recursive: true);
        }
    }

    [Test]
    public void DefaultsKeepSentryDisabledUntilDsnIsProvided()
    {
        StableConfig config = new();

        Assert.That(config.Sentry.Enabled, Is.False);
        Assert.That(config.Sentry.Dsn, Is.Empty);
        Assert.That(config.Sentry.Environment, Is.Empty);
        Assert.That(config.Sentry.TracesSampleRate, Is.EqualTo(0.1));
        Assert.That(config.Sentry.SendDefaultPii, Is.False);
    }

    [Test]
    public void StableConfigLoadsSentrySectionFromJson()
    {
        string stableJson = """
            {
              "SessionTimeout": "00:01:30",
              "Sentry": {
                "Dsn": "https://key@o123.ingest.sentry.io/456",
                "Environment": "staging",
                "TracesSampleRate": 0.25,
                "SendDefaultPii": true
              }
            }
            """;
        File.WriteAllText(Path.Combine(_basePath, "config", "stable.json"), stableJson);

        StableConfig config = new ConfigManager(_basePath).Get<StableConfig>();

        Assert.That(config.Sentry.Enabled, Is.True);
        Assert.That(config.Sentry.Dsn, Is.EqualTo("https://key@o123.ingest.sentry.io/456"));
        Assert.That(config.Sentry.Environment, Is.EqualTo("staging"));
        Assert.That(config.Sentry.TracesSampleRate, Is.EqualTo(0.25));
        Assert.That(config.Sentry.SendDefaultPii, Is.True);
    }

    [Test]
    public void MissingSentrySectionFallsBackToDefaults()
    {
        File.WriteAllText(
            Path.Combine(_basePath, "config", "stable.json"),
            "{\"SessionTimeout\": \"00:01:30\"}");

        StableConfig config = new ConfigManager(_basePath).Get<StableConfig>();

        Assert.That(config.Sentry.Enabled, Is.False);
        Assert.That(config.Sentry.Dsn, Is.Empty);
        Assert.That(config.Sentry.TracesSampleRate, Is.EqualTo(0.1));
        Assert.That(config.Sentry.SendDefaultPii, Is.False);
    }
}
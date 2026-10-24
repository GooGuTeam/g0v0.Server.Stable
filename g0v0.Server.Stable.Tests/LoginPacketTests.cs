// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text;
using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Packets;

namespace g0v0.Server.Stable.Tests;

public class LoginPacketTests
{
    // Input and expected fields ported from bancho.py/tests/unit/api/domains/cho_test.py.
    private const string LoginBody = "cmyui\npassword-md5\nb20230814.2cuttingedge|-5|1|osu-path.adapt:1.2.3.:adapters:uninstall:disk:|1";

    [TestCase("")]
    [TestCase("\n")]
    [TestCase("\n\n")]
    public void MatchesBanchoPyLoginDataIncludingOptionalTrailingNewlines(string ending)
    {
        LoginPacket result = LoginPacket.Parse(Encoding.UTF8.GetBytes(LoginBody + ending));
        string[] expectedAdapters = ["1", "2", "3"];
        Assert.Multiple(() =>
        {
            Assert.That(result.Username, Is.EqualTo("cmyui"));
            Assert.That(result.PasswordMd5, Is.EqualTo("password-md5"));
            Assert.That(result.OsuVersionString, Is.EqualTo("b20230814.2cuttingedge"));
            Assert.That(result.Version, Is.EqualTo(new OsuVersion(new DateOnly(2023, 8, 14), 2, OsuReleaseChannel.CuttingEdge)));
            Assert.That(result.UtcOffset, Is.EqualTo(-5));
            Assert.That(result.DisplayCity, Is.True);
            Assert.That(result.PmPrivate, Is.True);
            Assert.That(result.OsuPathMd5, Is.EqualTo("osu-path.adapt"));
            Assert.That(result.AdaptersString, Is.EqualTo("1.2.3."));
            Assert.That(result.Adapters, Is.EqualTo(expectedAdapters));
            Assert.That(result.RunningUnderWine, Is.False);
            Assert.That(result.AdaptersMd5, Is.EqualTo("adapters"));
            Assert.That(result.UninstallMd5, Is.EqualTo("uninstall"));
            Assert.That(result.DiskSignatureMd5, Is.EqualTo("disk"));
        });
    }

    [Test]
    public void ParsesRemainingStreamAndLeavesItOpen()
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes("prefix" + LoginBody));
        stream.Position = 6;
        Assert.That(LoginPacket.Parse(stream).Username, Is.EqualTo("cmyui"));
        Assert.That(stream.CanRead, Is.True);
        Assert.That(stream.Position, Is.EqualTo(stream.Length));
    }

    [Test]
    public void PreservesUsernameAndPasswordTextWithoutTrimmingOrHashing()
    {
        string body = LoginBody.Replace("cmyui\npassword-md5", " 玩家 \nABCDEF0123456789ABCDEF0123456789", StringComparison.Ordinal);
        LoginPacket result = LoginPacket.Parse(body);
        Assert.That(result.Username, Is.EqualTo(" 玩家 "));
        Assert.That(result.PasswordMd5, Is.EqualTo("ABCDEF0123456789ABCDEF0123456789"));
    }

    [TestCase("1", true)]
    [TestCase("0", false)]
    [TestCase("true", false)]
    [TestCase("01", false)]
    [TestCase("1 ", false)]
    public void OnlyExactOneMeansTrue(string value, bool expected)
    {
        string body = $"user\nhash\nb20230814|8|{value}|path:1.:adapters:uninstall:disk:|{value}";
        LoginPacket result = LoginPacket.Parse(body);
        Assert.That(result.DisplayCity, Is.EqualTo(expected));
        Assert.That(result.PmPrivate, Is.EqualTo(expected));
    }

    [TestCase("8", 8)]
    [TestCase("+8", 8)]
    [TestCase("-5", -5)]
    [TestCase("0", 0)]
    public void ParsesDecimalTimezoneHours(string value, int expected)
    {
        string body = LoginBody.Replace("|-5|", $"|{value}|", StringComparison.Ordinal);
        Assert.That(LoginPacket.Parse(body).UtcOffset, Is.EqualTo(expected));
    }

    [TestCase("b20230814", null, OsuReleaseChannel.Stable)]
    [TestCase("b20230814.2cuttingedge", 2, OsuReleaseChannel.CuttingEdge)]
    [TestCase("b20230814beta", null, OsuReleaseChannel.Beta)]
    [TestCase("b20230814dev", null, OsuReleaseChannel.Dev)]
    [TestCase("b20230814tourney", null, OsuReleaseChannel.Tourney)]
    [TestCase("b20230814.0", 0, OsuReleaseChannel.Stable)]
    public void MatchesBanchoPyVersionFormat(string value, int? revision, OsuReleaseChannel stream)
    {
        Assert.That(OsuVersion.Parse(value), Is.EqualTo(new OsuVersion(new DateOnly(2023, 8, 14), revision, stream)));
    }

    [TestCase("definitely-not-osu")]
    [TestCase("b20230230")]
    [TestCase("b20231301")]
    [TestCase("b00000101")]
    [TestCase("b20230814.12")]
    [TestCase("b20230814stable")]
    [TestCase("b20230814beta-extra")]
    [TestCase("b20230814\n")]
    public void InvalidVersionReturnsNull(string value)
    {
        Assert.That(OsuVersion.Parse(value), Is.Null);
    }

    [Test]
    public void ParsesWineSentinelWithoutATrailingDot()
    {
        LoginPacket result = LoginPacket.Parse(LoginBody.Replace("1.2.3.", "runningunderwine", StringComparison.Ordinal));
        string[] expectedAdapters = ["runningunderwine"];
        Assert.That(result.Adapters, Is.EqualTo(expectedAdapters));
        Assert.That(result.RunningUnderWine, Is.True);
    }

    [Test]
    public void PreservesEmptyAdapterEntriesLikeBanchoPy()
    {
        (string[] adapters, bool wine) = LoginPacket.ParseAdaptersString("1..3.");
        string[] expected = ["1", string.Empty, "3"];
        Assert.That(adapters, Is.EqualTo(expected));
        Assert.That(wine, Is.False);
        (string[] emptyAdapters, _) = LoginPacket.ParseAdaptersString(".");
        Assert.That(emptyAdapters, Has.Length.EqualTo(1));
        Assert.That(emptyAdapters[0], Is.Empty);
    }

    [TestCase("1.2.3")]
    [TestCase("")]
    [TestCase("RunningUnderWine")]
    public void RejectsNonWineAdapterListsWithoutTheTrailingDot(string value)
    {
        Assert.Throws<FormatException>(() => LoginPacket.ParseAdaptersString(value));
    }

    [TestCase("")]
    [TestCase("user\nhash")]
    [TestCase("user\nhash\nb20230814|8|1|path:1.:adapters:uninstall:disk:")]
    [TestCase("user\nhash\nb20230814|8|1|path:1.:adapters:uninstall:disk|1")]
    [TestCase("user\nhash\nb20230814|8|1|path:1.:adapters:disk:|1")]
    [TestCase("user\nhash\nb20230814|8.5|1|path:1.:adapters:uninstall:disk:|1")]
    [TestCase("user\nhash\nb20230814|2147483648|1|path:1.:adapters:uninstall:disk:|1")]
    [TestCase("user\nhash\ninvalid|8|1|path:1.:adapters:uninstall:disk:|1")]
    [TestCase("user\nhash\nb20230814|8|1|path:1.2.3:adapters:uninstall:disk:|1")]
    public void RejectsMalformedLoginBodies(string value)
    {
        Assert.Throws<FormatException>(() => LoginPacket.Parse(value));
    }

    [Test]
    public void RejectsInvalidUtf8InsteadOfSilentlyReplacingBytes()
    {
        byte[] invalid = [255, 254];
        Assert.Throws<DecoderFallbackException>(() => LoginPacket.Parse(invalid));
        using MemoryStream stream = new(invalid);
        Assert.Throws<DecoderFallbackException>(() => LoginPacket.Parse(stream));
        Assert.That(stream.CanRead, Is.True);
    }

    [Test]
    public void LoginIsIndependentOfFramedPacketInterfacesAndRegistration()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(LoginPacket).GetInterfaces(), Is.Empty);
            Assert.That(typeof(LoginPacket).GetCustomAttributes(typeof(OsuPacketAttribute), inherit: false), Is.Empty);
            Assert.That(OsuPacketAttribute.Packets.Values.Select(registration => registration.Type), Does.Not.Contain(typeof(LoginPacket)));
        });
    }
}
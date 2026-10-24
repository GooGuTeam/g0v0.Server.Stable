// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Server;

using osu.Game.Beatmaps.Legacy;

namespace g0v0.Server.Stable.Tests;

public class PacketValueTests
{
    [TestCase((byte)0, (byte)5)]
    [TestCase((byte)1, (byte)37)]
    [TestCase((byte)2, (byte)69)]
    [TestCase((byte)3, (byte)101)]
    public void PresencePacksModeWithoutTreatingItAsTournamentPrivilege(byte mode, byte expected)
    {
        PacketUserPresence packet = new();
        packet.SetPrivilegesAndMode(ClientPrivileges.Player | ClientPrivileges.Supporter, mode);

        Assert.Multiple(() =>
        {
            Assert.That(packet.PackedPrivilegesMode, Is.EqualTo(expected));
            Assert.That(packet.GetPrivileges(), Is.EqualTo(ClientPrivileges.Player | ClientPrivileges.Supporter));
            Assert.That(packet.GetMode(), Is.EqualTo(mode));
        });
    }

    [TestCase((byte)4)]
    [TestCase((byte)8)]
    public void PresenceRejectsInternalModes(byte mode)
    {
        PacketUserPresence packet = new();
        Assert.Throws<ArgumentOutOfRangeException>(() => packet.SetPrivilegesAndMode(ClientPrivileges.Player, mode));
    }

    [Test]
    public void LoginFailuresKeepTheirSignedWireValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new PacketUserId(LoginFailureReason.AuthenticationFailed).UserId, Is.EqualTo(-1));
            Assert.That(new PacketUserId(LoginFailureReason.RequiresVerification).UserId, Is.EqualTo(-8));
            Assert.That(Enum.IsDefined(typeof(LoginFailureReason), -4), Is.False);
        });
    }

    [Test]
    public void WireEnumsKeepTheirWidthsAndHighBits()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Enum.GetUnderlyingType(typeof(UserAction)), Is.EqualTo(typeof(byte)));
            Assert.That(Enum.GetUnderlyingType(typeof(ReplayAction)), Is.EqualTo(typeof(byte)));
            Assert.That(Enum.GetUnderlyingType(typeof(SlotStatus)), Is.EqualTo(typeof(byte)));
            Assert.That(Enum.GetUnderlyingType(typeof(LegacyMods)), Is.EqualTo(typeof(int)));
            Assert.That(Enum.GetUnderlyingType(typeof(PresenceFilter)), Is.EqualTo(typeof(int)));
            Assert.That(Enum.GetUnderlyingType(typeof(ClientPrivileges)), Is.EqualTo(typeof(int)));
            Assert.That((byte)SlotStatus.Quit, Is.EqualTo(128));
            Assert.That((uint)LegacyMods.Mirror, Is.EqualTo(0x40000000u));
            Assert.That((uint)(LegacyMods.Hidden | LegacyMods.HardRock), Is.EqualTo(24u));
        });
    }
}
// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Objects.States;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Client;

namespace g0v0.Server.Stable.Tests.Objects.Players;

public class PlayerTests
{
    [Test]
    public void GeneratesIndependentTokensAndIdleActivities()
    {
        Player first = new(1);
        Player second = new(1);
        Assert.Multiple(() =>
        {
            Assert.That(first.Id, Is.EqualTo(1));
            Assert.That(first.Token, Is.Not.Empty);
            Assert.That(first.Token, Is.Not.EqualTo(second.Token));
            Assert.That(first.IsTourneyClient, Is.False);
            Assert.That(first.Activity.Action, Is.EqualTo(UserAction.Idle));
            Assert.That(first.Activity, Is.Not.SameAs(second.Activity));
        });
    }

    [Test]
    public void SharesTheActivityModelWithChangeActionPacket()
    {
        Activity activity = new()
        {
            Action = UserAction.Playing,
            InfoText = "playing",
            MapHash = "hash",
            Mode = 3,
            MapId = 1234
        };
        PacketChangeAction packet = new() { Activity = activity };
        Player player = new(42, "session", isTourneyClient: true) { Activity = packet.Activity };
        Assert.Multiple(() =>
        {
            Assert.That(player.Activity, Is.SameAs(activity));
            Assert.That(player.Activity.MapId, Is.EqualTo(1234));
            Assert.That(player.Token, Is.EqualTo("session"));
            Assert.That(player.IsTourneyClient, Is.True);
        });
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void RejectsInvalidIds(int id)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Player(id));
    }

    [TestCase("")]
    [TestCase(" ")]
    public void RejectsEmptyTokens(string token)
    {
        Assert.Throws<ArgumentException>(() => new Player(1, token));
    }

    [Test]
    public void RejectsNullActivity()
    {
        Player player = new(1);
        Assert.Throws<ArgumentNullException>(() => player.Activity = null!);
    }
}
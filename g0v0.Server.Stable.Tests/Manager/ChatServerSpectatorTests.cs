// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Stable.Objects.Chats;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Server;
using Moq;

namespace g0v0.Server.Stable.Tests.Manager;

public partial class ChatServerTests
{
    [Test]
    public async Task SpectatorChannelIsPrivateAndAutomaticallyJoinsHostAndWatchers()
    {
        Player watcher = SpectatorPlayer(3);
        Player outsider = SpectatorPlayer(4);
        DrainSpectatorPackets(_alice, _bob, watcher, outsider);
        _players.UserWatchingPlayer(_bob, _alice);
        _players.UserWatchingPlayer(watcher, _alice);
        StableChannel channel = _players.GetSpectatorChannel(_alice)!;
        Assert.That(channel.Name, Is.EqualTo("#spectator"));
        Assert.That(channel.ChannelId, Is.Null);
        Assert.That(channel.GetMembers(), Is.EquivalentTo(new IPlayer[] { _alice, _bob, watcher }));
        Assert.That(_chats.GetChannels(), Does.Not.Contain(channel));
        Assert.That((await _chats.ConnectAsync(outsider)).OfType<PacketChannelInfo>().Any(packet => string.Equals(packet.Channel.Name, "#spectator", StringComparison.Ordinal)), Is.False);
        Assert.That(ReadSpectatorPackets(_alice).OfType<PacketChannelJoinSuccess>().Single().Name, Is.EqualTo("#spectator"));
        Assert.That(ReadSpectatorPackets(watcher).OfType<PacketChannelInfo>().Last().Channel.PlayerCount, Is.EqualTo(3));
        Assert.That(outsider.Dequeue(), Is.Empty);
        DrainSpectatorPackets(_bob);

        await _chats.SendChannelMessageAsync(_bob, "#spectator", "hello spectators");
        Player[] recipients = [_alice, watcher];
        foreach (PacketSendMessage message in recipients.Select(recipient => ReadSpectatorPackets(recipient).OfType<PacketSendMessage>().Single()))
        {
            Assert.That(message.Message.Recipient, Is.EqualTo("#spectator"));
            Assert.That(message.Message.SenderId, Is.EqualTo(_bob.Id));
            Assert.That(message.Message.Text, Is.EqualTo("hello spectators"));
        }

        Assert.That(_bob.Dequeue(), Is.Empty);
        Assert.That(outsider.Dequeue(), Is.Empty);
        _messages.Verify(repository => repository.CreateMessageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<MessageType>(), It.IsAny<string?>()), Times.Never);
    }

    [Test]
    public async Task SpectatorChannelsDoNotLeakAcrossHostsOrAllowUnrelatedSessionsToJoin()
    {
        Player otherHost = SpectatorPlayer(3);
        Player otherWatcher = SpectatorPlayer(4);
        Player outsider = SpectatorPlayer(5);
        _players.UserWatchingPlayer(_bob, _alice);
        _players.UserWatchingPlayer(otherWatcher, otherHost);
        DrainSpectatorPackets(_alice, _bob, otherHost, otherWatcher, outsider);
        Assert.That(await _chats.JoinChannelAsync(outsider, "#spectator"), Is.Empty);
        await _chats.SendChannelMessageAsync(outsider, "#spectator", "intrusion");
        await _chats.SendChannelMessageAsync(_alice, "#spectator", "private group");
        Assert.That(ReadSpectatorPackets(_bob).OfType<PacketSendMessage>().Single().Message.Text, Is.EqualTo("private group"));
        Assert.That(otherHost.Dequeue(), Is.Empty);
        Assert.That(otherWatcher.Dequeue(), Is.Empty);
        Assert.That(outsider.Dequeue(), Is.Empty);
    }

    [Test]
    public async Task ClosingAndRejoiningSpectatorChatDoesNotStopPlayback()
    {
        _players.UserWatchingPlayer(_bob, _alice);
        DrainSpectatorPackets(_alice, _bob);
        await _chats.LeaveChannelAsync(_bob, "#spectator");
        Assert.That(_players.GetSpectatingPlayer(_bob), Is.SameAs(_alice));
        Assert.That(_chats.GetPlayerChannels(_bob), Is.Empty);
        Assert.That(ReadSpectatorPackets(_bob).OfType<PacketChannelKick>().Single().Name, Is.EqualTo("#spectator"));
        DrainSpectatorPackets(_alice);
        await _chats.SendChannelMessageAsync(_bob, "#spectator", "closed");
        Assert.That(_alice.Dequeue(), Is.Empty);
        await _chats.JoinChannelAsync(_bob, "#spectator");
        Assert.That(ReadSpectatorPackets(_bob).OfType<PacketChannelJoinSuccess>().Single().Name, Is.EqualTo("#spectator"));
        Assert.That(_chats.GetPlayerChannels(_bob).Single().Name, Is.EqualTo("#spectator"));
    }

    [Test]
    public async Task SwitchingHostsRemovesTheOldMembershipAndDoesNotDuplicateJoins()
    {
        Player nextHost = SpectatorPlayer(3);
        _players.UserWatchingPlayer(_bob, _alice);
        StableChannel old = _players.GetSpectatorChannel(_alice)!;
        DrainSpectatorPackets(_alice, _bob, nextHost);
        _players.UserWatchingPlayer(_bob, nextHost);
        Assert.That(old.Count, Is.Zero);
        Assert.That(_players.GetSpectatorChannel(_alice), Is.Null);
        IPacket[] switched = ReadSpectatorPackets(_bob);
        Assert.That(switched.OfType<PacketChannelKick>().ToArray(), Has.Length.EqualTo(1));
        Assert.That(switched.OfType<PacketChannelJoinSuccess>().ToArray(), Has.Length.EqualTo(1));
        DrainSpectatorPackets(_alice, nextHost);
        _players.UserWatchingPlayer(_bob, nextHost);
        Assert.That(_bob.Dequeue(), Is.Empty);
        await _chats.SendChannelMessageAsync(_bob, "#spectator", "new group");
        Assert.That(ReadSpectatorPackets(nextHost).OfType<PacketSendMessage>().ToArray(), Has.Length.EqualTo(1));
        Assert.That(_alice.Dequeue(), Is.Empty);
    }

    [Test]
    public void StoppingTheLastSpectatorClosesBothTabsAndDestroysTheChannel()
    {
        _players.UserWatchingPlayer(_bob, _alice);
        StableChannel old = _players.GetSpectatorChannel(_alice)!;
        DrainSpectatorPackets(_alice, _bob);
        _players.UserStoppedWatchingPlayer(_bob);
        Assert.That(old.Count, Is.Zero);
        Assert.That(_players.GetSpectatorChannel(_alice), Is.Null);
        Assert.That(_players.GetSpectatorChannel(_bob), Is.Null);
        Assert.That(ReadSpectatorPackets(_alice).OfType<PacketChannelKick>().ToArray(), Has.Length.EqualTo(1));
        Assert.That(ReadSpectatorPackets(_bob).OfType<PacketChannelKick>().ToArray(), Has.Length.EqualTo(1));
        _players.UserWatchingPlayer(_bob, _alice);
        Assert.That(_players.GetSpectatorChannel(_alice), Is.Not.SameAs(old));
    }

    [Test]
    public void DisconnectsAndReplacementSessionsCleanUpMembershipByIdentity()
    {
        Player watcher = SpectatorPlayer(3);
        _players.UserWatchingPlayer(_bob, _alice);
        _players.UserWatchingPlayer(watcher, _alice);
        StableChannel old = _players.GetSpectatorChannel(_alice)!;
        _players.RemovePlayer(_bob);
        Assert.That(old.GetMembers(), Is.EquivalentTo(new IPlayer[] { _alice, watcher }));
        Player replacement = new(_alice.Id, "replacement") { Name = "alice" };
        _players.AddPlayer(replacement);
        Assert.That(old.Count, Is.Zero);
        Assert.That(_players.GetSpectatorChannel(watcher), Is.Null);
        Assert.That(_players.GetSpectatorChannel(replacement), Is.Null);
        Assert.That(_players.GetSpectatorChannel(_alice), Is.Null);
    }

    [Test]
    public void HostingWhileWatchingAnotherPlayerUsesOneChatAndRestoresTheHostedChatOnStop()
    {
        Player third = SpectatorPlayer(3);
        _players.UserWatchingPlayer(_bob, _alice);
        StableChannel hosted = _players.GetSpectatorChannel(_alice)!;
        _players.UserWatchingPlayer(_alice, third);
        Assert.That(_players.GetSpectatorChannel(_alice), Is.SameAs(_players.GetSpectatorChannel(third)));
        Assert.That(hosted.Contains(_alice), Is.False);
        Assert.That(hosted.Contains(_bob), Is.True);
        _players.UserStoppedWatchingPlayer(_alice);
        Assert.That(_players.GetSpectatorChannel(_alice), Is.SameAs(hosted));
        Assert.That(hosted.GetMembers(), Is.EquivalentTo(new IPlayer[] { _alice, _bob }));
    }

    [Test]
    public async Task TournamentSessionsOfOneAccountHaveSeparateSpectatorMemberships()
    {
        Player first = new(3, "tourney-one", isTourneyClient: true);
        Player second = new(3, "tourney-two", isTourneyClient: true);
        _players.AddPlayer(first);
        _players.AddPlayer(second);
        _players.UserWatchingPlayer(first, _alice);
        _players.UserWatchingPlayer(second, _bob);
        DrainSpectatorPackets(_alice, _bob, first, second);
        await _chats.SendChannelMessageAsync(_alice, "#spectator", "one session");
        Assert.That(ReadSpectatorPackets(first).OfType<PacketSendMessage>().ToArray(), Has.Length.EqualTo(1));
        Assert.That(second.Dequeue(), Is.Empty);
        _players.RemovePlayer(first);
        Assert.That(_players.GetSpectatorChannel(second)!.Contains(second), Is.True);
    }

    private static IPacket[] ReadSpectatorPackets(Player player)
        => PacketConvert.DeserializeAll(new MemoryStream(player.Dequeue()), new PacketFacade());

    private static void DrainSpectatorPackets(params Player[] players)
    {
        foreach (Player player in players)
        {
            player.Dequeue();
        }
    }

    private Player SpectatorPlayer(int id)
    {
        Player player = new(id, "spectator-" + id) { Name = "player" + id };
        _players.AddPlayer(player);
        return player;
    }
}
// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Objects.Rooms;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Client;
using g0v0.Server.Stable.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Online.Multiplayer;
using ServerPackets = g0v0.Server.Stable.Packets.Server;

namespace g0v0.Server.Stable.Tests.Manager;

public partial class RoomManagerTests
{
    [Test]
    public async Task CommandsRequireRoomMembershipAndOnlyTheHostCanGrantRefereePrivileges()
    {
        (Player host, Player guest, IRoom room) = await PairAsync();
        Player outsider = Player(4);
        Assert.That(await CommandAsync(guest, room, "name", "forged"), Does.Contain("privilege"));
        Assert.That(await CommandAsync(outsider, room, "name", "forged"), Does.Contain("multiplayer channel"));
        Assert.That(await CommandAsync(host, room, "addref", "#2"), Does.StartWith("Added"));
        Assert.That(await CommandAsync(guest, room, "name", "Tournament", "Final"), Does.Contain("Tournament Final"));
        Assert.That(await CommandAsync(guest, room, "addref", "#4"), Does.Contain("privilege"));
        Assert.That(await CommandAsync(guest, room, "removeref", "#2"), Does.Contain("privilege"));
        await CommandAsync(host, room, "removeref", "#2");
        Assert.That(await CommandAsync(guest, room, "name", "forged"), Does.Contain("privilege"));
        Assert.That(room.GetMatch().Name, Is.EqualTo("Tournament Final"));
    }

    [Test]
    public async Task SupportsSpacedUsernamesHostTransferAndOneBasedMovesAndTeams()
    {
        (Player host, Player guest, IRoom room) = await PairAsync();
        guest.Name = "Spaced User";
        _users.Setup(repository => repository.GetByUsernameAsync("Spaced User")).ReturnsAsync(new User { Id = 2, Username = guest.Name });
        await CommandAsync(host, room, "move", "Spaced", "User", "5");
        Assert.That(room.Slots[4].Player, Is.SameAs(guest));
        await CommandAsync(host, room, "set", "2");
        await CommandAsync(host, room, "team", "Spaced", "User", "blue");
        Assert.That(room.Slots[4].Team, Is.EqualTo(MatchTeam.Blue));
        await CommandAsync(host, room, "host", "Spaced", "User");
        Assert.That(room.Host, Is.SameAs(guest));
        Assert.That(await CommandAsync(host, room, "name", "forged"), Does.Contain("privilege"));
        _repository.Verify(repository => repository.UpdateRoomHost(room.RoomId, guest.Id), Times.Once);
    }

    [Test]
    public async Task CapacityAndJoinLockCannotBeBypassedUsingNativeSlotPackets()
    {
        (Player host, Player guest, IRoom room) = await PairAsync();
        await CommandAsync(host, room, "size", "2");
        await SendAsync(host, new PacketMatchLock { SlotId = 15 });
        await SendAsync(guest, new PacketMatchChangeSlot { SlotId = 15 });
        Assert.That(room.Slots[15].Status, Is.EqualTo(SlotStatus.Locked));
        Assert.That(room.Slots[1].Player, Is.SameAs(guest));
        Player third = Player(4);
        await CommandAsync(host, room, "size", "3");
        await CommandAsync(host, room, "lock");
        await _rooms.JoinRoomAsync(third, room.MatchId, string.Empty);
        Assert.That(_rooms.GetPlayerRoom(third), Is.Null);
        await CommandAsync(host, room, "unlock");
        await _rooms.JoinRoomAsync(third, room.MatchId, string.Empty);
        Assert.That(_rooms.GetPlayerRoom(third), Is.SameAs(room));
        Assert.That(await CommandAsync(host, room, "size", "2"), Does.Contain("occupied"));
        _repository.Verify(repository => repository.UpdateRoomSettings(room.RoomId, It.Is<MultiplayerRoomSettings>(settings => settings.MaxParticipants == 3)), Times.Once);
    }

    [Test]
    public async Task SettingsNeverDisclosePasswordAndInvitationsRespectPrivateMessagePrivacy()
    {
        (Player host, Player guest, IRoom room) = await PairAsync();
        await CommandAsync(host, room, "password", "secret");
        Assert.That(await CommandAsync(guest, room, "settings"), Does.Not.Contain("secret"));
        Player target = Player(4);
        User targetUser = new() { Id = 4, Username = target.Name, PmFriendsOnly = true };
        _users.Setup(repository => repository.GetByIdAsync(4)).ReturnsAsync(targetUser);
        ClearQueues();
        Assert.That(await CommandAsync(host, room, "invite", "#4"), Does.Contain("cannot receive"));
        Assert.That(Packets(target).OfType<ServerPackets.PacketMatchInvite>(), Is.Empty);
        targetUser.PmFriendsOnly = false;
        await CommandAsync(host, room, "invite", "#4");
        Assert.That(Packets(target).OfType<ServerPackets.PacketMatchInvite>().Single().Message.Text, Does.Contain("secret"));
        await CommandAsync(host, room, "password");
        Assert.That(room.GetMatch(includePassword: true).Password, Is.Null.Or.Empty);
    }

    [Test]
    public async Task BanRemovesTheAccountAndRejectsRejoiningWithoutRemovingTheHostOrReferee()
    {
        (Player host, Player guest, IRoom room) = await PairAsync();
        Assert.That(await CommandAsync(host, room, "kick", "#1"), Does.Contain("cannot be kicked"));
        await CommandAsync(host, room, "addref", "#2");
        Assert.That(await CommandAsync(host, room, "ban", "#2"), Does.Contain("cannot be kicked"));
        await CommandAsync(host, room, "removeref", "#2");
        await CommandAsync(host, room, "ban", "#2");
        Assert.That(_rooms.GetPlayerRoom(guest), Is.Null);
        Assert.That(room.Channel.Contains(guest), Is.False);
        await _rooms.JoinRoomAsync(guest, room.MatchId, string.Empty);
        Assert.That(_rooms.GetPlayerRoom(guest), Is.Null);
    }

    [Test]
    public async Task MapAndModsCommandsUseVerifiedBeatmapsAndPersistTheCanonicalPlaylist()
    {
        Player host = Player(1);
        IRoom room = await CreateAsync(host);
        Mock<IBeatmapRepository> beatmaps = Mock.Get(_services.GetRequiredService<IBeatmapRepository>());
        beatmaps.Setup(repository => repository.GetByIdAsync(22)).ReturnsAsync(new Beatmap { Id = 22, Checksum = SecondChecksum });
        Assert.That(await CommandAsync(host, room, "map", "22", "5"), Does.Contain("ruleset"));
        beatmaps.Verify(repository => repository.GetByIdAsync(22), Times.Never);
        await CommandAsync(host, room, "map", "22", "1");
        Assert.That(room.CurrentItem!.BeatmapID, Is.EqualTo(22));
        Assert.That(room.CurrentItem.RulesetID, Is.EqualTo(1));
        Assert.That(_storedItems[room.CurrentItem.ID].BeatmapId, Is.EqualTo(22));
        await CommandAsync(host, room, "mods", "HD", "DT", "freemod");
        Assert.That(room.GetMatch().FreeMods, Is.True);
        Assert.That(room.GetMatch().Mods.HasFlag(LegacyMods.DoubleTime), Is.True);
        Assert.That(room.Slots[0].Mods.HasFlag(LegacyMods.Hidden), Is.True);
        Assert.That(await CommandAsync(host, room, "mods", "DT", "HT"), Does.Contain("incompatible"));
        Assert.That(await CommandAsync(host, room, "mods", "invalid"), Does.Contain("Unsupported"));
    }

    [Test]
    public async Task PlaylistCommandsKeepTheFirstItemCurrentAndSupportEditAndRemove()
    {
        Player host = Player(1);
        IRoom room = await CreateAsync(host);
        Mock<IBeatmapRepository> beatmaps = Mock.Get(_services.GetRequiredService<IBeatmapRepository>());
        beatmaps.Setup(repository => repository.GetByIdAsync(11)).ReturnsAsync(new Beatmap { Id = 11, Checksum = FirstChecksum, Version = "First" });
        beatmaps.Setup(repository => repository.GetByIdAsync(22)).ReturnsAsync(new Beatmap { Id = 22, Checksum = SecondChecksum, Version = "Second" });
        _repository.Setup(repository => repository.AnyScoreTokenExistsFor(It.IsAny<long>())).ReturnsAsync(value: false);
        _repository.Setup(repository => repository.RemoveItemToPlaylist(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<int>()))
            .ReturnsAsync((long removedId, long roomId, int ownerId) => _storedItems.GetValueOrDefault(removedId) ?? new Playlist { Id = removedId, RoomId = roomId, OwnerId = ownerId });

        Assert.That(await CommandAsync(host, room, "playlist", "add", "0", "22"), Does.Contain("Added playlist item"));
        long queuedId = room.Playlist.Single(item => item.BeatmapID == 22).ID;
        Assert.That(room.CurrentItem!.BeatmapID, Is.EqualTo(11));
        Assert.That(await CommandAsync(host, room, "playlist", "edit", queuedId.ToString(), "beatmap=11", "mods=HD"), Does.Contain("Edited"));
        Assert.That(room.Playlist.Single(item => item.ID == queuedId).RequiredMods.ToArray(), Has.Length.EqualTo(1));
        Assert.That(await CommandAsync(host, room, "playlist", "remove", queuedId.ToString()), Does.Contain("Removed"));
        Assert.That(room.Playlist.Any(item => item.ID == queuedId), Is.False);
    }

    [Test]
    public async Task DelayedStartCanBeCancelledAndSettingsAreBlockedDuringGameplay()
    {
        (Player host, Player guest, IRoom room) = await PairAsync();
        await SendAsync(guest, new PacketMatchReady());
        await CommandAsync(host, room, "start", "5");
        await CommandAsync(host, room, "aborttimer");
        _clock.UtcNow = _clock.UtcNow.AddSeconds(6);
        RoomCountdownService countdowns = _services.GetRequiredService<RoomCountdownService>();
        await countdowns.SweepAsync();
        Assert.That(room.InProgress, Is.False);
        await CommandAsync(host, room, "start", "2");
        _clock.UtcNow = _clock.UtcNow.AddSeconds(2);
        await countdowns.SweepAsync();
        Assert.That(room.InProgress, Is.True);
        Assert.That(await CommandAsync(host, room, "name", "forged"), Does.Contain("during gameplay"));
        await CommandAsync(host, room, "abort");
        Assert.That(room.InProgress, Is.False);
    }

    [Test]
    public async Task ClosingARoomCancelsItsTimerAndDoesNotAffectAReusedMatchIdentifier()
    {
        Player host = Player(1);
        IRoom first = await CreateAsync(host);
        await SendAsync(host, new PacketMatchReady());
        await CommandAsync(host, first, "start", "1");
        await CommandAsync(host, first, "close");
        Assert.That(_rooms.GetRoom(first.MatchId), Is.Null);
        IRoom replacement = await CreateAsync(host);
        Assert.That(replacement.MatchId, Is.EqualTo(first.MatchId));
        await SendAsync(host, new PacketMatchReady());
        _clock.UtcNow = _clock.UtcNow.AddSeconds(2);
        await _services.GetRequiredService<RoomCountdownService>().SweepAsync();
        Assert.That(replacement.InProgress, Is.False);
    }

    [Test]
    public async Task TournamentCreationRetainsEmptyRoomsAndEnforcesThePerCreatorLimit()
    {
        Player host = Player(1);
        for (int i = 0; i < 4; i++)
        {
            IRoom? room = await _rooms.CreateTournamentRoomAsync(host, $"Tournament {i}");
            Assert.That(room, Is.Not.Null);
            Assert.That(room!.Slots.Count(slot => slot.Status == SlotStatus.Open), Is.EqualTo(7));
            await _rooms.LeaveRoomAsync(host);
            Assert.That(_rooms.GetRoom(room.MatchId), Is.SameAs(room));
        }

        Assert.That(await _rooms.CreateTournamentRoomAsync(host, "Fifth room"), Is.Null);
        Assert.That(_rooms.GetAllRooms(), Has.Count.EqualTo(4));
        IRoom first = _rooms.GetAllRooms()[0];
        await _rooms.JoinRoomAsync(host, first.MatchId, string.Empty);
        await CommandAsync(host, first, "close");
        Assert.That(await _rooms.CreateTournamentRoomAsync(host, "Replacement"), Is.Not.Null);
    }

    [Test]
    public async Task ExpirationAwaitsRoomDepartureAndPreservesTheRemainingHostSession()
    {
        (Player host, Player guest, IRoom room) = await PairAsync();
        host.LastReceiveTime = _clock.UtcNow.AddSeconds(-91);
        guest.LastReceiveTime = _clock.UtcNow;
        using SessionCleanupService cleanup = new(
            _players,
            _chat,
            _services.GetRequiredService<IServiceScopeFactory>(),
            new g0v0.Server.Common.Configuration.ConfigManager(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))),
            _clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionCleanupService>.Instance);
        Assert.That(await cleanup.SweepAsync(), Is.EqualTo(1));
        Assert.That(_rooms.GetPlayerRoom(host), Is.Null);
        Assert.That(room.Channel.Contains(host), Is.False);
        Assert.That(room.Host, Is.SameAs(guest));
        _repository.Verify(repository => repository.RemoveUserFromRoom(room.RoomId, host.Id), Times.Once);
    }

    [Test]
    public async Task ReadinessIsCheckedAgainWhenADelayedStartExpires()
    {
        (Player host, Player guest, IRoom room) = await PairAsync();
        await SendAsync(guest, new PacketMatchReady());
        await CommandAsync(host, room, "start", "2");
        await SendAsync(guest, new PacketMatchNotReady());
        _clock.UtcNow = _clock.UtcNow.AddSeconds(2);
        await _services.GetRequiredService<RoomCountdownService>().SweepAsync();
        Assert.That(room.InProgress, Is.False);
    }

    [Test]
    public async Task ReminderReplacementAndCancellationAreIsolatedToTheirRoom()
    {
        Player firstHost = Player(1);
        Player secondHost = Player(2);
        IRoom first = await CreateAsync(firstHost);
        IRoom second = await CreateAsync(secondHost);
        await CommandAsync(firstHost, first, "timer", "1");
        await CommandAsync(firstHost, first, "timer", "5");
        await CommandAsync(secondHost, second, "timer", "1");
        ClearQueues();
        _clock.UtcNow = _clock.UtcNow.AddSeconds(2);
        RoomCountdownService countdowns = _services.GetRequiredService<RoomCountdownService>();
        await countdowns.SweepAsync();
        Assert.That(Packets(firstHost).OfType<ServerPackets.PacketSendMessage>(), Is.Empty);
        Assert.That(Packets(secondHost).OfType<ServerPackets.PacketSendMessage>().Single().Message.Text, Is.EqualTo("Countdown finished."));
        await CommandAsync(firstHost, first, "aborttimer");
        _clock.UtcNow = _clock.UtcNow.AddSeconds(5);
        await countdowns.SweepAsync();
        Assert.That(Packets(firstHost).OfType<ServerPackets.PacketSendMessage>(), Is.Empty);
    }

    [Test]
    public async Task CreatorCanManageAnEmptyTournamentRoomAfterItsTemporaryHostLeaves()
    {
        Player creator = Player(1);
        Player guest = Player(2);
        IRoom room = (await _rooms.CreateTournamentRoomAsync(creator, "Tournament"))!;
        await _rooms.JoinRoomAsync(guest, room.MatchId, string.Empty);
        await _rooms.LeaveRoomAsync(creator);
        Assert.That(room.Host, Is.SameAs(guest));
        await _rooms.LeaveRoomAsync(guest);
        await _rooms.JoinRoomAsync(creator, room.MatchId, string.Empty);
        Assert.That(room.Host, Is.SameAs(creator));
        Assert.That(await CommandAsync(creator, room, "close"), Is.EqualTo("Closed the room."));
        Assert.That(_rooms.GetRoom(room.MatchId), Is.Null);
    }

    private Task<string> CommandAsync(Player sender, IRoom room, string command, params string[] args)
        => _rooms.ExecuteBotCommandAsync(sender, room.Channel, command, args);
}
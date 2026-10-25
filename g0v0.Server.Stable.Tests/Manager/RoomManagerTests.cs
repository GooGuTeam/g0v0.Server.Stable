// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Buffers.Binary;
using g0v0.Server.Common.Communication;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Rulesets;
using g0v0.Server.Stable.Manager;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Objects.Rooms;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using DatabaseRoom = g0v0.Server.Common.Database.Models.Room;
using Match = g0v0.Server.Stable.Packets.Match;
using ServerPackets = g0v0.Server.Stable.Packets.Server;

namespace g0v0.Server.Stable.Tests.Manager;

public class RoomManagerTests
{
    private const string FirstChecksum = "11111111111111111111111111111111";
    private const string SecondChecksum = "22222222222222222222222222222222";
    private readonly Dictionary<long, Playlist> _storedItems = [];
    private readonly HashSet<(long Room, int User)> _participants = [];
    private Mock<IRoomRepository> _repository = null!;
    private Mock<IChatRepository> _chatRepository = null!;
    private ServiceProvider _services = null!;
    private PlayerManager _players = null!;
    private RoomManager _rooms = null!;
    private ChatServer _chat = null!;
    private PacketFacade _facade = null!;
    private long _nextRoomId;
    private long _nextItemId;

    [SetUp]
    public void SetUp()
    {
        _storedItems.Clear();
        _participants.Clear();
        _nextRoomId = 100000;
        _nextItemId = 0;
        _repository = new Mock<IRoomRepository>();
        _repository.Setup(repository => repository.CreateRoom(It.IsAny<MultiplayerRoom>(), It.IsAny<int>(), It.IsAny<ServerIdentify>(), It.IsAny<G0V0RoomCategory>(), It.IsAny<bool>()))
            .ReturnsAsync((MultiplayerRoom room, int host, ServerIdentify source, G0V0RoomCategory category, bool _) =>
            {
                DatabaseRoom stored = new() { Id = ++_nextRoomId, Name = room.Settings.Name, HostId = host, Password = room.Settings.Password, Category = category, Source = source };
                return stored;
            });
        _repository.Setup(repository => repository.AddUserToRoom(It.IsAny<long>(), It.IsAny<int>()))
            .Callback((long room, int user) => _participants.Add((room, user))).Returns(Task.CompletedTask);
        _repository.Setup(repository => repository.RemoveUserFromRoom(It.IsAny<long>(), It.IsAny<int>()))
            .Callback((long room, int user) => _participants.Remove((room, user))).Returns(Task.CompletedTask);
        _repository.Setup(repository => repository.AddItemToPlaylist(It.IsAny<MultiplayerPlaylistItem>(), It.IsAny<long>(), It.IsAny<int>()))
            .ReturnsAsync((MultiplayerPlaylistItem item, long room, int _) => StoreItem(item, room, ++_nextItemId));
        _repository.Setup(repository => repository.EditItemToPlaylist(It.IsAny<MultiplayerPlaylistItem>(), It.IsAny<long>(), It.IsAny<int>()))
            .ReturnsAsync((MultiplayerPlaylistItem item, long room, int _) => StoreItem(item, room, item.ID));
        _chatRepository = new Mock<IChatRepository>();
        _chatRepository.Setup(repository => repository.GetOrCreateMultiplayerChannelAsync(It.IsAny<long>()))
            .ReturnsAsync((long id) => new ChatChannel { ChannelId = checked((int)id + 1), Name = $"mp_{id}" });
        _chatRepository.Setup(repository => repository.GetPublicChannelsAsync()).ReturnsAsync([]);
        Mock<IBeatmapRepository> beatmaps = new();
        beatmaps.Setup(repository => repository.GetByChecksumAsync(FirstChecksum)).ReturnsAsync(new Beatmap { Id = 11, Checksum = FirstChecksum });
        beatmaps.Setup(repository => repository.GetByChecksumAsync(SecondChecksum)).ReturnsAsync(new Beatmap { Id = 22, Checksum = SecondChecksum });
        _services = new ServiceCollection().AddSingleton(_repository.Object).AddSingleton(_chatRepository.Object)
            .AddSingleton(beatmaps.Object).BuildServiceProvider();
        IServiceScopeFactory scopes = _services.GetRequiredService<IServiceScopeFactory>();
        _players = new PlayerManager();
        _chat = new ChatServer(NullLogger<ChatServer>.Instance, scopes, _players);
        RulesetManager rulesets = new(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        _rooms = new RoomManager(NullLogger<RoomManager>.Instance, scopes, _players, _chat, rulesets);
        _facade = new PacketFacade { PlayerManager = _players, RoomManager = _rooms, ChatServer = _chat, RulesetManager = rulesets, ScopeFactory = scopes };
    }

    [TearDown]
    public void TearDown()
    {
        _rooms.Dispose();
        _services.Dispose();
    }

    [Test]
    public async Task CreationUsesServerOwnedSlotsAndSeparateDatabaseIdentifier()
    {
        Player host = Player(1);
        Match forged = Settings();
        forged.Id = 123;
        forged.HostId = 999;
        forged.InProgress = true;
        forged.SlotStatuses = Enumerable.Repeat(SlotStatus.Playing, 16).ToArray();
        forged.SlotUserIds = Enumerable.Repeat<int?>(999, 16).ToArray();
        await SendAsync(host, new PacketCreateMatch { Match = forged });
        IRoom room = _rooms.GetPlayerRoom(host)!;
        Assert.Multiple(() =>
        {
            Assert.That(room.RoomId, Is.GreaterThan(short.MaxValue));
            Assert.That(room.MatchId, Is.EqualTo(0));
            Assert.That(room.Host, Is.SameAs(host));
            Assert.That(room.InProgress, Is.False);
            Assert.That(room.Slots.Count(slot => slot.Player != null), Is.EqualTo(1));
            Assert.That(room.Slots[0].Status, Is.EqualTo(SlotStatus.NotReady));
            Assert.That(_participants, Does.Contain((room.RoomId, host.Id)));
            Assert.That(room.CurrentItem!.BeatmapID, Is.EqualTo(11));
            Assert.That(_storedItems[room.CurrentItem.ID].RoomId, Is.EqualTo(room.RoomId));
        });
        _repository.Verify(repository => repository.CreateRoom(It.IsAny<MultiplayerRoom>(), host.Id, ServerIdentify.Stable, G0V0RoomCategory.Realtime, false), Times.Once);
        _repository.Verify(repository => repository.UpdateRoomChannelId(room.RoomId, room.Channel.ChannelId!.Value), Times.Once);
    }

    [Test]
    public async Task JoiningUnknownMatchDoesNotLoadRealtimeDatabaseRoom()
    {
        Player guest = Player(1);
        _repository.Setup(repository => repository.GetRoom(42, ServerIdentify.Realtime))
            .ReturnsAsync(new DatabaseRoom { Id = 42, Source = ServerIdentify.Realtime });

        await SendAsync(guest, new PacketJoinMatch { MatchId = 42 });

        Assert.That(_rooms.GetPlayerRoom(guest), Is.Null);
        Assert.That(_rooms.GetAllRooms(), Is.Empty);
        Assert.That(Packets(guest).OfType<ServerPackets.PacketMatchJoinFail>(), Has.Exactly(1).Items);
        _repository.Verify(repository => repository.GetRoom(It.IsAny<long>(), It.IsAny<ServerIdentify>()), Times.Never);
        _repository.Verify(repository => repository.AddUserToRoom(It.IsAny<long>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task LobbyHidesPasswordsAndReceivesDisposal()
    {
        Player host = Player(1);
        Player lobby = Player(2);
        await SendAsync(lobby, new PacketJoinLobby());
        IRoom room = await CreateAsync(host, password: "secret");
        ServerPackets.PacketNewMatch listing = Packets(lobby).OfType<ServerPackets.PacketNewMatch>().Single();
        ServerPackets.PacketMatchJoinSuccess joined = Packets(host).OfType<ServerPackets.PacketMatchJoinSuccess>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(listing.Match.Password, Is.Empty);
            Assert.That(joined.Match.Password, Is.EqualTo("secret"));
        });
        await SendAsync(host, new PacketPartMatch());
        Assert.That(_rooms.GetRoom(room.MatchId), Is.Null);
        Assert.That(Packets(lobby).OfType<ServerPackets.PacketDisposeMatch>().Single().Id, Is.EqualTo(room.MatchId));
        _repository.Verify(repository => repository.SetRoomEndDate(room.RoomId, It.Is<DateTimeOffset?>(date => date != null)), Times.Once);
    }

    [Test]
    public async Task LockedSlotsCannotBeJoinedAndOccupiedLocksImmediatelyKick()
    {
        Player host = Player(1);
        Player guest = Player(2);
        IRoom room = await CreateAsync(host, password: "secret");
        await SendAsync(guest, new PacketJoinMatch { MatchId = room.MatchId, Password = "wrong" });
        Assert.That(Packets(guest).OfType<ServerPackets.PacketMatchJoinFail>(), Has.Exactly(1).Items);
        await SendAsync(host, new PacketMatchLock { SlotId = 1 });
        await SendAsync(guest, new PacketJoinMatch { MatchId = room.MatchId, Password = "secret" });
        Assert.That(room.Slots[2].Player, Is.SameAs(guest));
        await SendAsync(guest, new PacketMatchChangeSlot { SlotId = 1 });
        await SendAsync(guest, new PacketMatchLock { SlotId = 0 });
        await SendAsync(host, new PacketMatchLock { SlotId = 0 });
        Assert.That(room.Slots[0].Player, Is.SameAs(host));
        await SendAsync(host, new PacketMatchLock { SlotId = 2 });
        Assert.That(Packets(guest).OfType<ServerPackets.PacketUpdateMatch>().Last().Match.SlotStatuses[2], Is.EqualTo(SlotStatus.Locked));
        Assert.Multiple(() =>
        {
            Assert.That(_rooms.GetPlayerRoom(guest), Is.Null);
            Assert.That(room.Slots[2].Player, Is.Null);
            Assert.That(room.Slots[2].Status, Is.EqualTo(SlotStatus.Locked));
            Assert.That(_participants, Does.Not.Contain((room.RoomId, guest.Id)));
            Assert.That(room.Channel.Contains(guest), Is.False);
        });
        await SendAsync(host, new PacketMatchLock { SlotId = 2 });
        Assert.That(room.Slots[2].Status, Is.EqualTo(SlotStatus.Open));
    }

    [Test]
    public async Task TeamChangesAndSlotMovesPreserveOnlyServerOwnedState()
    {
        (Player host, Player guest, IRoom room) = await PairAsync();
        Match settings = Settings();
        settings.TeamType = MatchTeamType.TeamVs;
        settings.InProgress = true;
        settings.SlotTeams = Enumerable.Repeat(MatchTeam.Blue, 16).ToArray();
        await SendAsync(guest, new PacketMatchChangeSettings { Match = settings });
        Assert.That(room.GetMatch().TeamType, Is.EqualTo(MatchTeamType.HeadToHead));
        await SendAsync(host, new PacketMatchChangeSettings { Match = settings });
        Assert.That(room.InProgress, Is.False);
        Assert.That(room.Slots[1].Team, Is.EqualTo(MatchTeam.Red));
        await SendAsync(guest, new PacketMatchChangeTeam());
        await SendAsync(guest, new PacketMatchReady());
        await SendAsync(guest, new PacketMatchChangeSlot { SlotId = 5 });
        Assert.Multiple(() =>
        {
            Assert.That(room.Slots[5].Team, Is.EqualTo(MatchTeam.Blue));
            Assert.That(room.Slots[5].Status, Is.EqualTo(SlotStatus.Ready));
            Assert.That(room.Slots[1].Status, Is.EqualTo(SlotStatus.Open));
            Assert.That(room.Slots[1].Team, Is.EqualTo(MatchTeam.Neutral));
        });
        settings.TeamType = MatchTeamType.HeadToHead;
        await SendAsync(host, new PacketMatchChangeSettings { Match = settings });
        await SendAsync(guest, new PacketMatchChangeTeam());
        Assert.That(room.Slots[5].Team, Is.EqualTo(MatchTeam.Neutral));
    }

    [Test]
    public async Task FreeModsKeepSpeedUnderHostControlAndRestoreHostModsWhenDisabled()
    {
        (Player host, Player guest, IRoom room) = await PairAsync();
        await SendAsync(host, new PacketMatchChangeMods { Mods = LegacyMods.Hidden | LegacyMods.DoubleTime });
        Match settings = Settings();
        settings.FreeMods = true;
        await SendAsync(host, new PacketMatchChangeSettings { Match = settings });
        Assert.That(room.GetMatch().Mods, Is.EqualTo(LegacyMods.DoubleTime));
        Assert.That(room.Slots[1].Mods, Is.EqualTo(LegacyMods.Hidden));
        await SendAsync(guest, new PacketMatchChangeMods { Mods = LegacyMods.HardRock | LegacyMods.HalfTime });
        Assert.Multiple(() =>
        {
            Assert.That(room.GetMatch().Mods, Is.EqualTo(LegacyMods.DoubleTime));
            Assert.That(room.Slots[1].Mods, Is.EqualTo(LegacyMods.HardRock));
            Assert.That(room.CurrentItem!.AllowedMods.Select(mod => mod.Acronym), Does.Contain("HR"));
            Assert.That(room.CurrentItem.RequiredMods.Select(mod => mod.Acronym), Does.Contain("DT"));
            Assert.That(room.CurrentItem.Freestyle, Is.False);
        });
        settings.FreeMods = false;
        await SendAsync(host, new PacketMatchChangeSettings { Match = settings });
        Assert.That(room.GetMatch().Mods, Is.EqualTo(LegacyMods.DoubleTime | LegacyMods.Hidden));
        Assert.That(room.Slots.All(slot => slot.Mods == LegacyMods.None), Is.True);
        await SendAsync(guest, new PacketMatchChangeMods { Mods = LegacyMods.Easy });
        Assert.That(room.GetMatch().Mods, Is.EqualTo(LegacyMods.DoubleTime | LegacyMods.Hidden));
    }

    [Test]
    public async Task MapSelectionsAndRepeatedRoundsKeepPersistentPlaylistHistory()
    {
        Player host = Player(1);
        IRoom room = await CreateAsync(host);
        long first = room.CurrentItem!.ID;
        await SendAsync(host, new PacketMatchReady());
        Match choosing = Settings();
        choosing.MapId = -1;
        await SendAsync(host, new PacketMatchChangeSettings { Match = choosing });
        Assert.That(room.CurrentItem, Is.Null);
        Assert.That(room.Slots[0].Status, Is.EqualTo(SlotStatus.NotReady));
        Assert.That(_storedItems[first].Expired, Is.True);
        await SendAsync(host, new PacketMatchStart());
        Assert.That(room.InProgress, Is.False);
        Match next = Settings();
        next.MapMd5 = SecondChecksum;
        next.MapId = 999;
        next.WinCondition = MatchWinCondition.Accuracy;
        await SendAsync(host, new PacketMatchChangeSettings { Match = next });
        long second = room.CurrentItem!.ID;
        Assert.That(room.GetMatch().MapId, Is.EqualTo(22));
        Assert.That(_storedItems[second].WinCondition, Is.EqualTo(WinCondition.Accuracy));
        await SendAsync(host, new PacketMatchStart());
        Assert.That(_storedItems[second].PlayedAt, Is.Not.Null);
        await SendAsync(host, new PacketMatchComplete());
        await SendAsync(host, new PacketMatchStart());
        Assert.Multiple(() =>
        {
            Assert.That(room.CurrentItem!.ID, Is.Not.EqualTo(second));
            Assert.That(_storedItems[first].BeatmapId, Is.EqualTo(11));
            Assert.That(_storedItems[second].BeatmapId, Is.EqualTo(22));
            Assert.That(room.Playlist, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public async Task LoadingScoringSkippingAndCompletionExcludeNonParticipantsAndLateJoins()
    {
        (Player host, Player guest, IRoom room) = await PairAsync();
        Player missing = Player(3);
        Player late = Player(4);
        await SendAsync(missing, new PacketJoinMatch { MatchId = room.MatchId });
        await SendAsync(missing, new PacketMatchNoBeatmap());
        ClearQueues();
        await SendAsync(host, new PacketMatchStart());
        Assert.That(Packets(missing).OfType<ServerPackets.PacketMatchStart>(), Is.Empty);
        await SendAsync(late, new PacketJoinMatch { MatchId = room.MatchId });
        Assert.That(Packets(late).OfType<ServerPackets.PacketMatchStart>(), Is.Empty);
        await SendAsync(host, new PacketMatchLoadComplete());
        Assert.That(Packets(host).OfType<ServerPackets.PacketMatchAllPlayersLoaded>(), Is.Empty);
        await SendAsync(guest, new PacketMatchLoadComplete());
        await SendAsync(guest, new PacketMatchLoadComplete());
        Assert.That(Packets(host).OfType<ServerPackets.PacketMatchAllPlayersLoaded>(), Has.Exactly(1).Items);
        await SendAsync(guest, new PacketMatchChangeSlot { SlotId = 8 });
        await SendAsync(guest, new PacketMatchReady());
        Assert.That(room.Slots[1].Status, Is.EqualTo(SlotStatus.Playing));
        ScoreFrame frame = new() { Id = 15, Time = 1000, TotalScore = 123456, Num300 = 25, ScoreV2 = true, ComboPortion = 0.75, BonusPortion = 0.25 };
        ClearQueues();
        await SendAsync(guest, new PacketMatchScoreUpdate { Frame = frame });
        ServerPackets.PacketMatchScoreUpdate update = Packets(host).OfType<ServerPackets.PacketMatchScoreUpdate>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(update.Frame.Id, Is.EqualTo(1));
            Assert.That(update.Frame.Num300, Is.EqualTo(25));
            Assert.That(update.Frame.ComboPortion, Is.EqualTo(0.75));
            Assert.That(room.Slots[1].Score!.TotalScore, Is.EqualTo(123456));
            Assert.That(Packets(late).OfType<ServerPackets.PacketMatchScoreUpdate>(), Is.Empty);
            Assert.That(Packets(missing).OfType<ServerPackets.PacketMatchScoreUpdate>(), Is.Empty);
        });
        await SendAsync(host, new PacketMatchSkipRequest());
        Assert.That(Packets(host).OfType<ServerPackets.PacketMatchSkip>(), Is.Empty);
        await SendAsync(guest, new PacketMatchSkipRequest());
        await SendAsync(guest, new PacketMatchSkipRequest());
        Assert.That(Packets(host).OfType<ServerPackets.PacketMatchSkip>(), Has.Exactly(1).Items);
        await SendAsync(guest, new PacketMatchFailed());
        await SendAsync(guest, new PacketMatchFailed());
        Assert.That(Packets(host).OfType<ServerPackets.PacketMatchPlayerFailed>(), Has.Exactly(1).Items);
        await SendAsync(host, new PacketMatchComplete());
        Assert.That(room.InProgress, Is.True);
        await SendAsync(guest, new PacketMatchComplete());
        Assert.Multiple(() =>
        {
            Assert.That(room.InProgress, Is.False);
            Assert.That(Packets(host).OfType<ServerPackets.PacketMatchComplete>(), Has.Exactly(1).Items);
            Assert.That(Packets(late).OfType<ServerPackets.PacketMatchComplete>(), Is.Empty);
            Assert.That(room.Slots[1].Loaded, Is.False);
            Assert.That(room.Slots[1].Skipped, Is.False);
        });
    }

    [Test]
    public async Task DeparturesReleaseBarriersAndTransferHost()
    {
        (Player host, Player guest, IRoom room) = await PairAsync();
        await SendAsync(host, new PacketMatchStart());
        await SendAsync(guest, new PacketMatchLoadComplete());
        await SendAsync(guest, new PacketMatchSkipRequest());
        ClearQueues();
        await host.Offline();
        IPacket[] packets = Packets(guest);
        Assert.Multiple(() =>
        {
            Assert.That(room.Host, Is.SameAs(guest));
            Assert.That(packets.OfType<ServerPackets.PacketMatchTransferHost>(), Has.Exactly(1).Items);
            Assert.That(packets.OfType<ServerPackets.PacketMatchAllPlayersLoaded>(), Has.Exactly(1).Items);
            Assert.That(packets.OfType<ServerPackets.PacketMatchSkip>(), Has.Exactly(1).Items);
            Assert.That(_rooms.GetPlayerRoom(host), Is.Null);
        });
        await guest.Offline();
        Assert.That(_rooms.GetAllRooms(), Is.Empty);
    }

    [Test]
    public async Task TournamentObserversDoNotOccupySlotsOrReceivePasswords()
    {
        Player host = Player(1);
        IRoom room = await CreateAsync(host, password: "secret");
        Player observer = Player(2, tournament: true);
        await SendAsync(observer, new PacketTournamentJoinMatchChannel { MatchId = room.MatchId });
        Assert.That(Packets(observer).OfType<ServerPackets.PacketUpdateMatch>().Single().Match.Password, Is.Empty);
        Assert.That(room.Slots.Count(slot => slot.Player != null), Is.EqualTo(1));
        await SendAsync(observer, new PacketJoinMatch { MatchId = room.MatchId, Password = "secret" });
        Assert.That(_rooms.GetPlayerRoom(observer), Is.Null);
        await SendAsync(host, new PacketMatchStart());
        Assert.That(Packets(observer).OfType<ServerPackets.PacketMatchStart>().Single().Match.Password, Is.Empty);
        await SendAsync(host, new PacketMatchLoadComplete());
        ClearQueues();
        await SendAsync(host, new PacketMatchScoreUpdate { Frame = new ScoreFrame { Time = 1 } });
        Assert.That(Packets(observer).OfType<ServerPackets.PacketMatchScoreUpdate>(), Has.Exactly(1).Items);
        await SendAsync(observer, new PacketTournamentLeaveMatchChannel { MatchId = room.MatchId });
        Assert.That(room.Channel.Contains(observer), Is.False);
    }

    [Test]
    public async Task RoomChatIsPrivateAndUsesTheStoredChannel()
    {
        (Player host, Player guest, IRoom room) = await PairAsync();
        Player outsider = Player(3);
        await _chat.JoinChannelAsync(outsider, "#multiplayer");
        ClearQueues();
        await SendAsync(outsider, new PacketSendPublicMessage { Message = new Message { Recipient = "#multiplayer", Text = "intrusion" } });
        await SendAsync(host, new PacketSendPublicMessage { Message = new Message { Recipient = "#multiplayer", Text = "hello" } });
        Assert.That(Packets(guest).OfType<ServerPackets.PacketSendMessage>().Single().Message.Text, Is.EqualTo("hello"));
        Assert.That(Packets(outsider).OfType<ServerPackets.PacketSendMessage>(), Is.Empty);
        Assert.That(_chat.GetChannels(), Is.Empty);
        _chatRepository.Verify(repository => repository.CreateMessageAsync(room.Channel.ChannelId!.Value, host.Id, "hello", MessageType.Plain, null), Times.Once);
    }

    [Test]
    public async Task ParallelJoinsCannotOccupyTwoRoomsOrTheSameLastSlot()
    {
        Player firstHost = Player(1);
        Player secondHost = Player(2);
        Player guest = Player(3);
        IRoom first = await CreateAsync(firstHost);
        IRoom second = await CreateAsync(secondHost);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _repository.Setup(repository => repository.AddUserToRoom(first.RoomId, guest.Id)).Returns(async () =>
        {
            entered.SetResult();
            await release.Task;
        });
        Task joining = _rooms.JoinRoomAsync(guest, first.MatchId, string.Empty);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task racing = _rooms.JoinRoomAsync(guest, second.MatchId, string.Empty);
        release.SetResult();
        await Task.WhenAll(joining, racing).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(_rooms.GetPlayerRoom(guest), Is.SameAs(first));
        Assert.That(second.Slots.Any(slot => ReferenceEquals(slot.Player, guest)), Is.False);
        for (int index = 2; index < 16; index++)
        {
            await _rooms.ExecuteAsync(secondHost, room => room.LockSlotAsync(secondHost, index));
        }

        Player left = Player(4);
        Player right = Player(5);
        await Task.WhenAll(_rooms.JoinRoomAsync(left, second.MatchId, string.Empty), _rooms.JoinRoomAsync(right, second.MatchId, string.Empty));
        Assert.That(second.Slots.Count(slot => slot.Player != null), Is.EqualTo(2));
        Assert.That(new[] { _rooms.GetPlayerRoom(left), _rooms.GetPlayerRoom(right) }.Count(room => room != null), Is.EqualTo(1));
    }

    [Test]
    public async Task ReplacedSessionCannotMutateItsOldRoom()
    {
        (Player host, Player guest, IRoom room) = await PairAsync();
        Player replacement = Player(host.Id);
        await SendAsync(host, new PacketMatchStart());
        Assert.Multiple(() =>
        {
            Assert.That(room.Host, Is.SameAs(guest));
            Assert.That(room.InProgress, Is.False);
            Assert.That(_rooms.GetPlayerRoom(replacement), Is.Null);
            Assert.That(room.Channel.Contains(host), Is.False);
        });
    }

    [Test]
    public async Task FailedPlaylistCreationDeletesTheUnpublishedRoom()
    {
        Player host = Player(1);
        _repository.Setup(repository => repository.AddItemToPlaylist(It.IsAny<MultiplayerPlaylistItem>(), It.IsAny<long>(), It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("storage unavailable"));
        await SendAsync(host, new PacketCreateMatch { Match = Settings() });
        Assert.That(_rooms.GetAllRooms(), Is.Empty);
        Assert.That(_rooms.GetPlayerRoom(host), Is.Null);
        Assert.That(Packets(host).OfType<ServerPackets.PacketMatchJoinFail>(), Has.Exactly(1).Items);
        _repository.Verify(repository => repository.DeleteRoom(100001), Times.Once);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ScoreUpdatesDecodeTheEntireFrameAndRejectTruncatedOrTrailingPayloads(bool scoreV2)
    {
        // Independent bancho.py scoreframe fixtures, with the incoming packet ID (47).
        byte[] bytes = Convert.FromHexString(scoreV2
            ? "2f00002d000000629500001c400130000200200008000300688307007a00270000f50001000000000000e03f000000000000d03f"
            : "2f00001d000000629500001c400130000200200008000300688307007a00270000f50000");
        using MemoryStream input = new(bytes);
        PacketMatchScoreUpdate packet = PacketConvert.Deserialize<PacketMatchScoreUpdate>(input, _facade)!;
        Assert.Multiple(() =>
        {
            Assert.That(packet.Frame.Time, Is.EqualTo(38242));
            Assert.That(packet.Frame.Num300, Is.EqualTo(320));
            Assert.That(packet.Frame.TotalScore, Is.EqualTo(492392));
            Assert.That(packet.Frame.ScoreV2, Is.EqualTo(scoreV2));
            Assert.That(packet.Frame.ComboPortion, Is.EqualTo(scoreV2 ? 0.5 : null));
        });
        byte[][] malformedPackets = [new byte[bytes.Length - 1], new byte[bytes.Length + 1]];
        foreach (byte[] malformed in malformedPackets)
        {
            bytes.AsSpan(0, Math.Min(bytes.Length, malformed.Length)).CopyTo(malformed);
            BinaryPrimitives.WriteInt32LittleEndian(malformed.AsSpan(3), malformed.Length - 7);
            using MemoryStream broken = new(malformed);
            Assert.That(PacketConvert.Deserialize<PacketMatchScoreUpdate>(broken, _facade), Is.Null);
        }
    }

    [Test]
    public async Task PasswordTransferAndInviteFollowHostAuthority()
    {
        (Player host, Player guest, IRoom room) = await PairAsync();
        Player invitee = Player(3);
        Match password = Settings();
        password.Password = "new secret";
        await SendAsync(guest, new PacketMatchChangePassword { Match = password });
        Assert.That(room.GetMatch(includePassword: true).Password, Is.Null);
        await SendAsync(host, new PacketMatchChangePassword { Match = password });
        _repository.Verify(
            repository => repository.UpdateRoomSettings(
                room.RoomId,
                It.Is<MultiplayerRoomSettings>(settings => settings.Password == "new secret")),
            Times.Once);
        await SendAsync(guest, new PacketMatchTransferHost { SlotId = 1 });
        Assert.That(room.Host, Is.SameAs(host));
        await SendAsync(host, new PacketMatchTransferHost { SlotId = 1 });
        await SendAsync(host, new PacketMatchStart());
        Assert.That(room.InProgress, Is.False);
        Assert.That(room.Host, Is.SameAs(guest));
        await SendAsync(guest, new PacketMatchInvite { UserId = invitee.Id });
        ServerPackets.PacketMatchInvite invite = Packets(invitee).OfType<ServerPackets.PacketMatchInvite>().Single();
        Assert.That(invite.Message.SenderId, Is.EqualTo(guest.Id));
        Assert.That(invite.Message.Text, Does.Contain("new%20secret"));
    }

    [Test]
    public async Task UnresolvedSelectionsKeepThePreviousMapAndPlaylist()
    {
        Player host = Player(1);
        IRoom room = await CreateAsync(host);
        long itemId = room.CurrentItem!.ID;
        Match unknown = Settings();
        unknown.MapMd5 = new string('a', 32);
        unknown.MapId = 123;
        ClearQueues();
        await SendAsync(host, new PacketMatchChangeSettings { Match = unknown });
        Assert.Multiple(() =>
        {
            Assert.That(room.CurrentItem!.ID, Is.EqualTo(itemId));
            Assert.That(room.GetMatch().MapId, Is.EqualTo(11));
            Assert.That(_storedItems[itemId].Expired, Is.False);
            Assert.That(Packets(host).OfType<ServerPackets.PacketNotification>(), Has.Exactly(1).Items);
        });
    }

    [Test]
    public async Task RenamingAfterAPlayedRoundDoesNotAppendAPlaylistItem()
    {
        Player host = Player(1);
        IRoom room = await CreateAsync(host);
        await SendAsync(host, new PacketMatchStart());
        await SendAsync(host, new PacketMatchComplete());
        long completedId = room.CurrentItem!.ID;
        Match rename = Settings();
        rename.Name = "renamed";
        await SendAsync(host, new PacketMatchChangeSettings { Match = rename });
        Assert.That(room.CurrentItem!.ID, Is.EqualTo(completedId));
        Assert.That(room.Playlist, Has.Count.EqualTo(1));
        rename.WinCondition = MatchWinCondition.ScoreV2;
        await SendAsync(host, new PacketMatchChangeSettings { Match = rename });
        Assert.That(room.CurrentItem.ID, Is.Not.EqualTo(completedId));
        Assert.That(room.CurrentItem.RequiredMods.Select(mod => mod.Acronym), Does.Contain("SV2"));
        Assert.That(_storedItems[completedId].WinCondition, Is.EqualTo(WinCondition.Score));
    }

    [Test]
    public async Task ScoreFramesRequireAnActiveLoadedParticipantAndFiniteScoreV2Values()
    {
        (Player host, Player guest, IRoom room) = await PairAsync();
        ScoreFrame score = new() { Time = 1, ScoreV2 = true, ComboPortion = 0.5, BonusPortion = 0.25 };
        await SendAsync(guest, new PacketMatchScoreUpdate { Frame = score });
        Assert.That(room.Slots[1].Score, Is.Null);
        await SendAsync(host, new PacketMatchStart());
        await SendAsync(guest, new PacketMatchScoreUpdate { Frame = score });
        Assert.That(room.Slots[1].Score, Is.Null);
        await SendAsync(guest, new PacketMatchLoadComplete());
        await SendAsync(guest, new PacketMatchScoreUpdate { Frame = score });
        score.ComboPortion = double.NaN;
        ClearQueues();
        await SendAsync(guest, new PacketMatchScoreUpdate { Frame = score });
        Assert.That(Packets(host).OfType<ServerPackets.PacketMatchScoreUpdate>(), Is.Empty);
        Assert.That(room.Slots[1].Score!.ComboPortion, Is.EqualTo(0.5));
        score.ComboPortion = 0.5;
        score.Time = 0;
        await SendAsync(guest, new PacketMatchScoreUpdate { Frame = score });
        Assert.That(room.Slots[1].Score!.Time, Is.EqualTo(1));
        await SendAsync(guest, new PacketMatchComplete());
        score.Time = 2;
        await SendAsync(guest, new PacketMatchScoreUpdate { Frame = score });
        Assert.That(room.Slots[1].Score!.Time, Is.EqualTo(1));
    }

    [Test]
    public async Task RepeatedBarrierPacketsRetryStorageFailuresWithoutDuplicateBroadcasts()
    {
        Player host = Player(1);
        await CreateAsync(host);
        IRoom room = _rooms.GetPlayerRoom(host)!;
        await SendAsync(host, new PacketMatchStart());
        _repository.SetupSequence(repository => repository.UpdateRoomStatus(room.RoomId, MultiplayerRoomState.Playing))
            .ThrowsAsync(new InvalidOperationException("transient"))
            .Returns(Task.CompletedTask);
        Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(host, new PacketMatchLoadComplete()));
        await SendAsync(host, new PacketMatchLoadComplete());
        await SendAsync(host, new PacketMatchLoadComplete());
        Assert.That(Packets(host).OfType<ServerPackets.PacketMatchAllPlayersLoaded>(), Has.Exactly(1).Items);
        _repository.SetupSequence(repository => repository.UpdateRoomStatus(room.RoomId, MultiplayerRoomState.Open))
            .ThrowsAsync(new InvalidOperationException("transient"))
            .Returns(Task.CompletedTask);
        Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(host, new PacketMatchComplete()));
        await SendAsync(host, new PacketMatchComplete());
        Assert.That(room.InProgress, Is.False);
        Assert.That(Packets(host).OfType<ServerPackets.PacketMatchComplete>(), Has.Exactly(1).Items);
    }

    [Test]
    public async Task RoomCommandsSerializeWhileOtherRoomsContinue()
    {
        (Player host, Player guest, IRoom room) = await PairAsync();
        Player otherHost = Player(3);
        IRoom otherRoom = await CreateAsync(otherHost);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _repository.Setup(repository => repository.UpdateRoomSettings(room.RoomId, It.IsAny<MultiplayerRoomSettings>())).Returns(async () =>
        {
            entered.SetResult();
            await release.Task;
        });
        Match changed = Settings();
        changed.Name = "changing";
        Task changing = SendAsync(host, new PacketMatchChangeSettings { Match = changed });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task ready = SendAsync(guest, new PacketMatchReady());
        try
        {
            await SendAsync(otherHost, new PacketMatchReady()).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(otherRoom.Slots[0].Status, Is.EqualTo(SlotStatus.Ready));
            Assert.That(ready.IsCompleted, Is.False);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(changing, ready).WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.That(room.Slots[1].Status, Is.EqualTo(SlotStatus.Ready));
    }

    [Test]
    public async Task LeavingTheLastPlayingSlotCompletesForTheFinishedParticipants()
    {
        (Player host, Player guest, IRoom room) = await PairAsync();
        await SendAsync(host, new PacketMatchStart());
        await SendAsync(host, new PacketMatchComplete());
        ClearQueues();
        await SendAsync(guest, new PacketPartMatch());
        Assert.That(room.InProgress, Is.False);
        Assert.That(room.Slots[0].Status, Is.EqualTo(SlotStatus.NotReady));
        Assert.That(Packets(host).OfType<ServerPackets.PacketMatchComplete>(), Has.Exactly(1).Items);
    }

    [Test]
    public async Task FailedSettingsWriteKeepsTheCurrentPlaylistConsistentWithTheWireMap()
    {
        Player host = Player(1);
        IRoom room = await CreateAsync(host);
        _repository.Setup(repository => repository.UpdateRoomSettings(room.RoomId, It.IsAny<MultiplayerRoomSettings>()))
            .ThrowsAsync(new InvalidOperationException("storage unavailable"));
        Match next = Settings();
        next.MapMd5 = SecondChecksum;
        next.MapId = 22;
        Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(host, new PacketMatchChangeSettings { Match = next }));
        Assert.That(room.GetMatch().MapId, Is.EqualTo(room.CurrentItem!.BeatmapID));
        Assert.That(room.CurrentItem.BeatmapID, Is.EqualTo(11));
        Assert.That(room.CurrentItem.Expired, Is.False);
    }

    [Test]
    public async Task FailedJoinReportsFailureWithoutOccupyingASlot()
    {
        Player host = Player(1);
        Player guest = Player(2);
        IRoom room = await CreateAsync(host);
        _repository.Setup(repository => repository.AddUserToRoom(room.RoomId, guest.Id))
            .ThrowsAsync(new InvalidOperationException("storage unavailable"));
        ClearQueues();
        await SendAsync(guest, new PacketJoinMatch { MatchId = room.MatchId });
        Assert.That(Packets(guest).OfType<ServerPackets.PacketMatchJoinFail>(), Has.Exactly(1).Items);
        Assert.That(_rooms.GetPlayerRoom(guest), Is.Null);
        Assert.That(room.Slots[1].Status, Is.EqualTo(SlotStatus.Open));
        Assert.That(room.Channel.Contains(guest), Is.False);
    }

    private static Match Settings() => new() { Name = "Stable room", MapId = 11, MapMd5 = FirstChecksum, MapName = "Artist - Song [Difficulty]", SlotMods = new LegacyMods[16] };

    private Player Player(int id, bool tournament = false)
    {
        Player player = new(id, _players, tournament) { Name = $"user{id}", Privileges = ClientPrivileges.Player | ClientPrivileges.Supporter };
        _players.AddPlayer(player);
        return player;
    }

    private async Task<IRoom> CreateAsync(Player host, string? password = null)
    {
        Match settings = Settings();
        settings.Password = password;
        await SendAsync(host, new PacketCreateMatch { Match = settings });
        return _rooms.GetPlayerRoom(host) ?? throw new AssertionException("Room creation failed.");
    }

    private async Task<(Player Host, Player Guest, IRoom Room)> PairAsync()
    {
        Player host = Player(1);
        Player guest = Player(2);
        IRoom room = await CreateAsync(host);
        await SendAsync(guest, new PacketJoinMatch { MatchId = room.MatchId });
        ClearQueues();
        return (host, guest, room);
    }

    private async Task SendAsync(Player player, IClientPacket packet)
    {
        using MemoryStream stream = new(PacketConvert.Serialize(packet));
        IClientPacket parsed = (IClientPacket)PacketConvert.Deserialize(stream, _facade)!;
        foreach (IPacket response in await parsed.Handle(player, _facade))
        {
            player.Enqueue(response);
        }
    }

    private IPacket[] Packets(Player player)
    {
        using MemoryStream stream = new(player.Dequeue());
        return PacketConvert.DeserializeAll(stream, _facade);
    }

    private void ClearQueues()
    {
        foreach (IPlayer player in _players.GetAllPlayers())
        {
            player.Dequeue();
        }
    }

    private Playlist StoreItem(MultiplayerPlaylistItem item, long room, long id)
    {
        Playlist stored = new()
        {
            Id = id,
            RoomId = room,
            OwnerId = item.OwnerID,
            BeatmapId = item.BeatmapID,
            RulesetId = item.RulesetID,
            RequiredMods = item.RequiredMods.ToList(),
            AllowedMods = item.AllowedMods.ToList(),
            Expired = item.Expired,
            PlayedAt = item.PlayedAt,
            WinCondition = item.WinCondition,
            PlaylistOrder = item.PlaylistOrder,
        };
        _storedItems[id] = stored;
        return stored;
    }
}
// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Manager;
using g0v0.Server.Stable.Objects.States;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Server;
using osu.Game.Beatmaps.Legacy;

namespace g0v0.Server.Stable.Objects.Players;

/// <summary>
/// Stores the identity and activity of a connected stable client.
/// </summary>
public class Player : IPlayer
{
    private readonly Lock _queueLock = new();
    private readonly Queue<byte[]> _outgoing = new();
    private IPlayerManager? _manager;

    /// <summary>
    /// Initializes a new instance of the <see cref="Player"/> class with its lifecycle manager.
    /// </summary>
    /// <param name="id">The account ID.</param>
    /// <param name="manager">The session registry.</param>
    /// <param name="isTourneyClient">Whether this is a tournament session.</param>
    public Player(int id, IPlayerManager manager, bool isTourneyClient = false)
        : this(id, isTourneyClient)
    {
        AttachManager(manager);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Player"/> class with a generated token.
    /// </summary>
    /// <param name="id">The account ID.</param>
    /// <param name="isTourneyClient">Whether the session belongs to a tournament client.</param>
    public Player(int id, bool isTourneyClient = false)
        : this(id, Guid.NewGuid().ToString("N"), isTourneyClient)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Player"/> class.
    /// </summary>
    /// <param name="id">The account ID.</param>
    /// <param name="token">The session token.</param>
    /// <param name="isTourneyClient">Whether the session belongs to a tournament client.</param>
    public Player(int id, string token, bool isTourneyClient = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        Id = id;
        Token = token;
        IsTourneyClient = isTourneyClient;
        Activity = new Activity();
    }

    /// <inheritdoc />
    public int Id { get; }

    /// <inheritdoc />
    public string Token { get; }

    /// <inheritdoc />
    public Activity Activity
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <inheritdoc />
    public bool IsTourneyClient { get; }

    /// <summary>
    /// Gets or sets a value indicating whether only restricted packet handlers are allowed.
    /// </summary>
    public bool IsRestricted { get; set; }

    /// <summary>
    /// Gets or sets the UTC time of the last successfully processed packet request.
    /// </summary>
    public DateTimeOffset LastReceiveTime { get; set; }

    /// <inheritdoc />
    public bool IsOnline => _manager != null && ReferenceEquals(_manager.GetPlayer(Token), this);

    /// <inheritdoc />
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc />
    public int UtcOffset { get; set; }

    /// <inheritdoc />
    public byte CountryCode { get; set; }

    /// <inheritdoc />
    public ClientPrivileges Privileges { get; set; } = ClientPrivileges.Player;

    /// <inheritdoc />
    public LegacyMods Mods { get; set; }

    /// <inheritdoc />
    public PresenceFilter PresenceFilter { get; set; }

    /// <inheritdoc />
    public PacketUserStats Statistics { get; } = new();

    /// <inheritdoc />
    public Task Online()
    {
        GetManager().AddPlayer(this);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task Offline(bool isKicked = false)
    {
        GetManager().RemovePlayer(this, isKicked);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ChangePlayerActivityAsync(Activity activity, LegacyMods mods = LegacyMods.None)
    {
        GetManager().ChangePlayerActivity(this, activity, mods);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task WatchPlayer(IPlayer target)
    {
        GetManager().UserWatchingPlayer(this, target);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopWatchPlayer(IPlayer? target = null)
    {
        GetManager().UserStoppedWatchingPlayer(this, target);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendFrames(ReplayFrameBundle frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        GetManager().BroadcastUserSentFrames(this, PacketConvert.SerializePayload(frames));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CannotSpectate()
    {
        GetManager().UserCannotSpectate(this);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Enqueue(IPacket packet)
    {
        byte[] data = PacketConvert.Serialize(packet);
        lock (_queueLock)
        {
            _outgoing.Enqueue(data);
        }
    }

    /// <inheritdoc />
    public byte[] Dequeue()
    {
        lock (_queueLock)
        {
            using MemoryStream stream = new();
            while (_outgoing.TryDequeue(out byte[]? data))
            {
                stream.Write(data);
            }

            return stream.ToArray();
        }
    }

    /// <inheritdoc />
    public PacketUserPresence GetPresence()
    {
        PacketUserPresence packet = new()
        {
            UserId = Id,
            Name = Name,
            UtcOffsetPlus24 = checked((byte)(Math.Clamp(UtcOffset, -24, 24) + 24)),
            CountryCode = CountryCode,
            Rank = Statistics.Rank
        };
        packet.SetPrivilegesAndMode(Privileges, Activity.Mode <= 3 ? Activity.Mode : (byte)0);
        return packet;
    }

    /// <inheritdoc />
    public PacketUserStats GetStats() => new()
    {
        UserId = Id,
        Action = Activity.Action,
        InfoText = Activity.InfoText,
        MapHash = Activity.MapHash,
        Mods = Mods,
        Mode = Activity.Mode,
        MapId = Activity.MapId,
        RankedScore = Statistics.RankedScore,
        Accuracy = Statistics.Accuracy,
        PlayCount = Statistics.PlayCount,
        TotalScore = Statistics.TotalScore,
        Rank = Statistics.Rank,
        Pp = Statistics.Pp
    };

    /// <inheritdoc />
    public void OnPlayerOnline(IPlayer player)
    {
        Enqueue(player.GetPresence());
        Enqueue(player.GetStats());
    }

    /// <inheritdoc />
    public void OnPlayerOffline(IPlayer player, bool isKicked = false)
    {
        if (ReferenceEquals(player, this) && isKicked)
        {
            Enqueue(new PacketNotification { Message = "Your session was replaced by another login." });
            Enqueue(new PacketRestart { Milliseconds = 0 });
        }
        else
        {
            Enqueue(new PacketUserLogout { UserId = player.Id });
        }
    }

    /// <inheritdoc />
    public void OnPlayerChangeActivity(IPlayer player) => Enqueue(player.GetStats());

    /// <inheritdoc />
    public void OnWatched(IPlayer source) => Enqueue(new PacketSpectatorJoined { UserId = source.Id });

    /// <inheritdoc />
    public void OnWatchedStopped(IPlayer source) => Enqueue(new PacketSpectatorLeft { UserId = source.Id });

    /// <inheritdoc />
    public void OnFellowSpectatorJoined(IPlayer source) => Enqueue(new PacketFellowSpectatorJoined { UserId = source.Id });

    /// <inheritdoc />
    public void OnFellowSpectatorLeft(IPlayer source) => Enqueue(new PacketFellowSpectatorLeft { UserId = source.Id });

    /// <inheritdoc />
    public void OnUserSentFrames(byte[] data) => Enqueue(new PacketSpectateFrames { Data = data });

    /// <inheritdoc />
    public void OnWatchedPlayerOffline(IPlayer player) => Enqueue(new PacketNotification { Message = $"{player.Name} stopped broadcasting spectator frames." });

    internal void AttachManager(IPlayerManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        if (_manager != null && !ReferenceEquals(_manager, manager))
        {
            throw new InvalidOperationException("A session cannot belong to two player managers.");
        }

        _manager = manager;
    }

    private IPlayerManager GetManager() => _manager ?? throw new InvalidOperationException("Provide a player manager or register the player before invoking actions.");
}
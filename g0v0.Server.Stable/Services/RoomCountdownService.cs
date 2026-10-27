// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Manager;
using g0v0.Server.Stable.Objects.Rooms;
using g0v0.Server.Stable.Packets;

namespace g0v0.Server.Stable.Services;

/// <summary>Runs cancellable room reminders and delayed starts without detached background tasks.</summary>
/// <param name="rooms">The live room registry.</param>
/// <param name="chats">The chat transport for countdown announcements.</param>
/// <param name="clock">The clock used by timers.</param>
/// <param name="logger">Countdown diagnostics.</param>
public sealed class RoomCountdownService(
    IRoomManager rooms,
    IChatServer chats,
    TimeProvider clock,
    ILogger<RoomCountdownService> logger) : BackgroundService
{
    private readonly Lock _lock = new();
    private readonly Dictionary<(IRoom Room, bool Start), DateTimeOffset> _timers = [];

    /// <summary>Starts or replaces a reminder or a delayed match start for one exact room.</summary>
    /// <param name="room">The exact room instance.</param>
    /// <param name="seconds">The positive delay in seconds.</param>
    /// <param name="startMatch">Whether expiry starts gameplay.</param>
    public void Schedule(IRoom room, int seconds, bool startMatch)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds);
        lock (_lock)
        {
            _timers[(room, startMatch)] = clock.GetUtcNow().AddSeconds(seconds);
        }
    }

    /// <summary>Cancels both reminders and delayed starts in the exact room.</summary>
    /// <param name="room">The exact room instance.</param>
    public void Cancel(IRoom room)
    {
        lock (_lock)
        {
            _timers.Remove((room, false));
            _timers.Remove((room, true));
        }
    }

    /// <summary>Processes due countdowns after rechecking room identity and cancellation under the room lock.</summary>
    /// <param name="cancellationToken">The shutdown token.</param>
    /// <returns>A task representing the sweep.</returns>
    public async Task SweepAsync(CancellationToken cancellationToken = default)
    {
        KeyValuePair<(IRoom Room, bool Start), DateTimeOffset>[] snapshot;
        lock (_lock)
        {
            snapshot = _timers.ToArray();
        }

        foreach (KeyValuePair<(IRoom Room, bool Start), DateTimeOffset> timer in snapshot)
        {
            IRoom room = timer.Key.Room;
            if (!ReferenceEquals(rooms.GetRoom(room.MatchId), room) || room.IsDisposed)
            {
                Cancel(room);
                continue;
            }

            if (timer.Value > clock.GetUtcNow())
            {
                continue;
            }

            await room.RoomLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!ReferenceEquals(rooms.GetRoom(room.MatchId), room) || room.IsDisposed)
                {
                    Cancel(room);
                    continue;
                }

                lock (_lock)
                {
                    if (!_timers.TryGetValue(timer.Key, out DateTimeOffset current) || current != timer.Value)
                    {
                        continue;
                    }

                    _timers.Remove(timer.Key);
                }

                if (timer.Key.Start && room is { Host: { } host, InProgress: false })
                {
                    if (room.Slots.Any(slot => slot.Status == SlotStatus.Ready))
                    {
                        await room.StartAsync(host).ConfigureAwait(false);
                    }

                    await chats.SendBotMessageAsync(room.Channel, room.InProgress ? "Started the match." : "Could not start the match; check the beatmap and player states.").ConfigureAwait(false);
                }
                else if (!timer.Key.Start)
                {
                    await chats.SendBotMessageAsync(room.Channel, "Countdown finished.").ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Countdown failed in Stable room {RoomId}.", room.RoomId);
            }
            finally
            {
                room.RoomLock.Release();
            }
        }
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(1), clock);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_lock)
            {
                _timers.Clear();
            }
        }
    }
}
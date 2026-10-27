// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Objects.Chats;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Objects.Rooms;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Services;
using StableRoom = g0v0.Server.Stable.Objects.Rooms.Room;

namespace g0v0.Server.Stable.Manager;

public sealed partial class RoomManager
{
    /// <inheritdoc/>
    public async Task<IRoom?> CreateTournamentRoomAsync(IPlayer player, string name)
    {
        return GetPlayerRoom(player) is not null
            ? null
            : await CreateRoomCoreAsync(player, new Match { Name = name, MapId = -1 }, tournamentMode: true).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string> ExecuteBotCommandAsync(IPlayer player, StableChannel channel, string command, string[] arguments)
    {
        await _membershipLock.WaitAsync().ConfigureAwait(false);
        try
        {
            StableRoom? room = _members.GetValueOrDefault(player) ?? _observers.GetValueOrDefault(player);
            if (room is null || !ReferenceEquals(room.Channel, channel))
            {
                return "The !mp command can only be used in your multiplayer channel.";
            }

            await room.RoomLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (room.IsDisposed || !player.IsOnline || player.IsRestricted || !channel.Contains(player))
                {
                    return "Could not find an active room for this session.";
                }

                using IServiceScope scope = _scopes.CreateScope();
                RoomCountdownService countdowns = scope.ServiceProvider.GetRequiredService<RoomCountdownService>();
                return await room.ExecuteBotCommandAsync(player, command, arguments, countdowns).ConfigureAwait(false);
            }
            finally
            {
                room.RoomLock.Release();
            }
        }
        finally
        {
            _membershipLock.Release();
        }
    }
}
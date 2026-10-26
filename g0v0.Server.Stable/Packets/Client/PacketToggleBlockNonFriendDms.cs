// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C99 TOGGLE_BLOCK_NON_FRIEND_DMS. Payload members are declared in wire order.
/// </summary>
[OsuPacket(99)]
public class PacketToggleBlockNonFriendDms : IClientPacket
{
    /// <summary>
    /// Gets or sets Value.
    /// </summary>
    public int Value { get; set; }

    /// <inheritdoc />
    public async Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        using IServiceScope scope = facade.ScopeFactory.CreateScope();
        IUserRepository users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        User? user = await users.GetByIdAsync(player.Id).ConfigureAwait(false);
        if (user is null)
        {
            return [];
        }

        user.PmFriendsOnly = Value == 1;
        await users.UpdateAsync(user).ConfigureAwait(false);
        return [];
    }
}
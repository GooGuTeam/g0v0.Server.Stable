// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C73 FRIEND_ADD. Payload members are declared in wire order.
/// </summary>
[OsuPacket(73)]
public class PacketFriendAdd : IClientPacket
{
    /// <summary>
    /// Gets or sets UserId.
    /// </summary>
    public int UserId { get; set; }

    /// <inheritdoc />
    public async Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        if (UserId <= 0 || UserId == player.Id)
        {
            return [];
        }

        using IServiceScope scope = facade.ScopeFactory.CreateScope();
        IUserRepository users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        if (await users.GetByIdAsync(UserId).ConfigureAwait(false) is null)
        {
            return [];
        }

        IRelationshipRepository relationships = scope.ServiceProvider.GetRequiredService<IRelationshipRepository>();
        Relationship? existing = await relationships.GetRelationshipAsync(player.Id, UserId).ConfigureAwait(false);
        if (existing?.Type == RelationshipType.Follow)
        {
            return [];
        }

        Relationship follow = new() { UserId = player.Id, TargetId = UserId, Type = RelationshipType.Follow };
        if (existing is null)
        {
            await relationships.CreateAsync(follow).ConfigureAwait(false);
        }
        else
        {
            // Type is part of the primary key. Replace it using a new, untracked instance.
            await relationships.UpdateAsync(follow).ConfigureAwait(false);
        }

        return [];
    }
}
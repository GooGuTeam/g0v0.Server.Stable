// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C74 FRIEND_REMOVE. Payload members are declared in wire order.
/// </summary>
[OsuPacket(74)]
public class PacketFriendRemove : IClientPacket
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
        IRelationshipRepository relationships = scope.ServiceProvider.GetRequiredService<IRelationshipRepository>();
        Relationship? relationship = await relationships.GetRelationshipAsync(player.Id, UserId).ConfigureAwait(false);
        if (relationship?.Type == RelationshipType.Follow)
        {
            await relationships.DeleteAsync(relationship).ConfigureAwait(false);
        }

        return [];
    }
}
// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Server;

namespace g0v0.Server.Stable.Services;

/// <summary>
/// Applies PM privacy rules to Stable messages and multiplayer invitations.
/// </summary>
internal static class PrivateMessagePolicy
{
    internal static async Task<IPacket?> CheckAsync(IPlayer sender, IPlayer target, IServiceScopeFactory scopes)
    {
        using IServiceScope scope = scopes.CreateScope();
        IUserRepository users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        IRelationshipRepository relationships = scope.ServiceProvider.GetRequiredService<IRelationshipRepository>();
        User? senderUser = await users.GetByIdAsync(sender.Id).ConfigureAwait(false);
        User? targetUser = await users.GetByIdAsync(target.Id).ConfigureAwait(false);
        if (senderUser is null || targetUser is null)
        {
            return Blocked(target);
        }

        // A sender's own friends-only setting also limits outgoing PMs.
        if (senderUser.PmFriendsOnly && !await relationships.IsFollowingAsync(sender.Id, target.Id).ConfigureAwait(false))
        {
            return new PacketNotification
            {
                Message = "The user may not be able to reply because you are only accepting messages from people on your friends list."
            };
        }

        if (senderUser.IsBot)
        {
            return null;
        }

        bool blocked = await relationships.IsBlockedAsync(sender.Id, target.Id).ConfigureAwait(false)
            || await relationships.IsBlockedAsync(target.Id, sender.Id).ConfigureAwait(false)
            || (targetUser.PmFriendsOnly && !await relationships.IsFollowingAsync(target.Id, sender.Id).ConfigureAwait(false));
        return blocked ? Blocked(target) : null;
    }

    private static PacketUserDmBlocked Blocked(IPlayer target) => new()
    {
        Message = new Message { Recipient = target.Name }
    };
}
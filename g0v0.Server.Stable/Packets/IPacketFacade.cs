// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Rulesets;
using g0v0.Server.Stable.Manager;

namespace g0v0.Server.Stable.Packets;

/// <summary>
/// Exposes the services a packet handler needs while it processes one poll.
/// </summary>
/// <remarks>
/// The facade is resolved per request, so a handler can reach the singleton session and channel
/// registries without the packet types depending on dependency injection.
/// </remarks>
public interface IPacketFacade
{
    /// <summary>
    /// Gets or sets the manager of connected player sessions.
    /// </summary>
    IPlayerManager PlayerManager { get; set; }

    /// <summary>
    /// Gets or sets the chat server that owns stable channels and messages.
    /// </summary>
    IChatServer ChatServer { get; set; }

    /// <summary>
    /// Gets or sets the ruleset manager used to convert the reported mods.
    /// </summary>
    RulesetManager RulesetManager { get; set; }

    /// <summary>
    /// Gets or sets the factory for request-independent service scopes.
    /// </summary>
    IServiceScopeFactory ScopeFactory { get; set; }

    /// <summary>
    /// Gets or sets the live Stable room registry.
    /// </summary>
    public IRoomManager RoomManager { get; set; }
}
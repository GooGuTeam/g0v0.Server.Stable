// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Rulesets;
using g0v0.Server.Stable.Manager;

namespace g0v0.Server.Stable.Packets;

/// <summary>
/// The request-scoped implementation of <see cref="IPacketFacade"/>.
/// </summary>
/// <remarks>
/// Every member is assigned by the object initializer in the composition root, which is why they are
/// declared settable instead of being constructor parameters.
/// </remarks>
public class PacketFacade : IPacketFacade
{
    /// <summary>
    /// Gets or sets the manager of connected player sessions.
    /// </summary>
    public IPlayerManager PlayerManager { get; set; } = null!;

    /// <summary>
    /// Gets or sets the chat server that owns stable channels and messages.
    /// </summary>
    public IChatServer ChatServer { get; set; } = null!;

    /// <summary>
    /// Gets or sets the ruleset manager used to convert the reported mods.
    /// </summary>
    public RulesetManager RulesetManager { get; set; } = null!;

    /// <summary>
    /// Gets or sets the factory for request-independent service scopes.
    /// </summary>
    public IServiceScopeFactory ScopeFactory { get; set; } = null!;
}
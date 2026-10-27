// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Configuration.Attributes;

namespace g0v0.Server.Stable;

/// <summary>Configures Stable session expiry and tournament room limits.</summary>
[ConfigFile("stable")]
public sealed class StableConfig
{
    /// <summary>Gets or sets the maximum time without a packet request before a session expires.</summary>
    [Reloadable]
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>Gets or sets the interval between scans for inactive sessions.</summary>
    [Reloadable]
    public TimeSpan SessionCleanupInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the maximum number of active tournament rooms created by one account.</summary>
    [Reloadable]
    public int MaxActiveTournamentRooms { get; set; } = 4;
}
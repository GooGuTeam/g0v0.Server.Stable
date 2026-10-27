// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Stable.Tests.Services;

internal sealed class ManualTimeProvider : TimeProvider
{
    internal DateTimeOffset UtcNow { get; set; } = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
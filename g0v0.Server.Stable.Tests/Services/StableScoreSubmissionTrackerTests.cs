// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Services;

namespace g0v0.Server.Stable.Tests.Services;

public class StableScoreSubmissionTrackerTests
{
    [Test]
    public async Task SerializesRetriesAndRetainsTheAllocatedIdWithinStable()
    {
        using StableScoreSubmissionTracker tracker = new();
        Task<StableScoreSubmissionTracker.Lease> second;
        using (StableScoreSubmissionTracker.Lease first = await tracker.AcquireAsync(1, "checksum", CancellationToken.None))
        {
            first.ScoreId = 42;
            second = tracker.AcquireAsync(1, "checksum", CancellationToken.None);
            Assert.That(second.IsCompleted, Is.False);
        }

        using StableScoreSubmissionTracker.Lease otherAccount = await tracker.AcquireAsync(2, "checksum", CancellationToken.None);
        Assert.That(otherAccount.ScoreId, Is.Null);
        using StableScoreSubmissionTracker.Lease retry = await second;
        Assert.That(retry.ScoreId, Is.EqualTo(42));
    }

    [Test]
    public async Task CancellingAWaitingRequestDoesNotReleaseTheActiveSubmission()
    {
        using StableScoreSubmissionTracker tracker = new();
        Task<StableScoreSubmissionTracker.Lease> retry;
        using (await tracker.AcquireAsync(1, "checksum", CancellationToken.None))
        {
            using CancellationTokenSource cancellation = new();
            Task<StableScoreSubmissionTracker.Lease> waiting = tracker.AcquireAsync(1, "checksum", cancellation.Token);
            await cancellation.CancelAsync();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await waiting);
            retry = tracker.AcquireAsync(1, "checksum", CancellationToken.None);
            Assert.That(retry.IsCompleted, Is.False);
        }

        using StableScoreSubmissionTracker.Lease lease = await retry;
    }
}
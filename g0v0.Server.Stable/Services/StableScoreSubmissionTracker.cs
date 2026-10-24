// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Caching.Memory;

namespace g0v0.Server.Stable.Services;

/// <summary>
/// Owns short-lived duplicate submission identities and locks within this Stable server instance.
/// </summary>
public sealed class StableScoreSubmissionTracker : IDisposable
{
    private readonly MemoryCache _entries = new(new MemoryCacheOptions());
    private readonly Lock _gate = new();

    /// <summary>
    /// Serializes concurrent retries of an account's submission and retains its allocated score ID for one day.
    /// </summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="checksum">The verified submission checksum.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>A lease that must be disposed after submission handling.</returns>
    public async Task<Lease> AcquireAsync(int userId, string checksum, CancellationToken cancellationToken)
    {
        Entry entry;
        lock (_gate)
        {
            entry = _entries.GetOrCreate((userId, checksum), cacheEntry =>
            {
                cacheEntry.SlidingExpiration = TimeSpan.FromDays(1);
                return new Entry();
            })!;
        }

        await entry.Mutex.WaitAsync(cancellationToken);
        return new Lease(entry);
    }

    /// <inheritdoc/>
    public void Dispose() => _entries.Dispose();

    /// <summary>
    /// Holds the per-submission lock and allocated score identity during an upload.
    /// </summary>
    public sealed class Lease : IDisposable
    {
        private readonly Entry _entry;
        private bool _disposed;

        internal Lease(Entry entry)
        {
            _entry = entry;
        }

        /// <summary>
        /// Gets or sets the ID allocated to this submission, retained across upload retries.
        /// </summary>
        public long? ScoreId
        {
            get => _entry.ScoreId;
            set => _entry.ScoreId = value;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _entry.Mutex.Release();
        }
    }

    internal sealed class Entry
    {
        internal SemaphoreSlim Mutex { get; } = new(1, 1);

        internal long? ScoreId { get; set; }
    }
}
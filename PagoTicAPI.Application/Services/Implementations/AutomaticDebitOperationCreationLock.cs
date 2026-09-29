namespace PagoTicAPI.Application.Services.Implementations;

public sealed class AutomaticDebitOperationCreationLock : IAutomaticDebitOperationCreationLock
{
    private static readonly ConcurrentDictionary<string, LockEntry> Locks = new(StringComparer.Ordinal);

    public async ValueTask<IAsyncDisposable> AcquireAsync(
        long jurisdictionId,
        string obligationId,
        CancellationToken cancellationToken = default)
    {
        var key = $"{jurisdictionId}:{obligationId}";
        while (true)
        {
            var entry = Locks.GetOrAdd(key, static _ => new LockEntry());
            lock (entry.SyncRoot)
            {
                if (!Locks.TryGetValue(key, out var current) || !ReferenceEquals(entry, current))
                    continue;
                entry.Users++;
            }

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken);
                return new Releaser(this, key, entry);
            }
            catch
            {
                RemoveUser(key, entry, false);
                throw;
            }
        }
    }

    private void RemoveUser(string key, LockEntry entry, bool releaseSemaphore)
    {
        if (releaseSemaphore) entry.Semaphore.Release();
        lock (entry.SyncRoot)
        {
            entry.Users--;
            if (entry.Users == 0 && Locks.TryRemove(key, out var removed))
                removed.Semaphore.Dispose();
        }
    }

    private sealed class LockEntry
    {
        public object SyncRoot { get; } = new();
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int Users { get; set; }
    }

    private sealed class Releaser(
        AutomaticDebitOperationCreationLock owner,
        string key,
        LockEntry entry) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                owner.RemoveUser(key, entry, true);
            return ValueTask.CompletedTask;
        }
    }
}

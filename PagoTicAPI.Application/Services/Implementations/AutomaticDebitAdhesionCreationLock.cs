namespace PagoTicAPI.Application.Services.Implementations;

public sealed class AutomaticDebitAdhesionCreationLock : IAutomaticDebitAdhesionCreationLock
{
    private static readonly ConcurrentDictionary<string, LockEntry> Locks = new(StringComparer.Ordinal);

    public async ValueTask<IAsyncDisposable> AcquireAsync(
        long jurisdictionId,
        string taxpayerAccountId,
        CancellationToken cancellationToken = default)
    {
        var key = $"{jurisdictionId}:{taxpayerAccountId}";
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
                RemoveUser(key, entry, releaseSemaphore: false);
                throw;
            }
        }
    }

    private void RemoveUser(string key, LockEntry entry, bool releaseSemaphore)
    {
        if (releaseSemaphore)
            entry.Semaphore.Release();

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
        AutomaticDebitAdhesionCreationLock owner,
        string key,
        LockEntry entry) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                owner.RemoveUser(key, entry, releaseSemaphore: true);

            return ValueTask.CompletedTask;
        }
    }
}

using System.Threading;

namespace NugetPackages.Infrastructure
{
    public class ConcurrentRateLimiter: IDisposable
    {
        private SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
        private Dictionary<string, StartedEnded> timeStamps = new ();
        private bool disposedValue;

        public int MaxItemsPerMinute { get; set; } = int.MaxValue;
        public int MaxItemsPerDay { get; set; } = int.MaxValue;

        public int ToleranceDeltaSeconds { get; set; } = 5;

        public async Task<bool> TryStart(string key, int weight = 1, CancellationToken cancellationToken = default)
        {
            await semaphore.WaitAsync(cancellationToken);

            try
            {
                var obsolete = timeStamps.Where(t => (DateTime.UtcNow - t.Value.UtcStarted).TotalSeconds > (60 + ToleranceDeltaSeconds) * 60 * 24 && t.Value.UtcEnded != null)
                    .ToList();

                foreach (var item in obsolete)
                {
                    timeStamps.Remove(item.Key);
                }

                if (timeStamps.Sum(item => item.Value.Weight) >= MaxItemsPerDay ||
                    timeStamps.Sum(t => (((DateTime.UtcNow - t.Value.UtcStarted).TotalSeconds < (60 + ToleranceDeltaSeconds) && t.Value.UtcEnded != null) || t.Value.UtcEnded == null) ? t.Value.Weight : 0) >= MaxItemsPerMinute)
                {
                    return false;
                }

                timeStamps.Add(key, new StartedEnded() { Weight = weight });
                return true;
            }
            finally
            {
                semaphore.Release();
            }
        }

        public async Task End(string key, CancellationToken cancellationToken = default)
        {
            await semaphore.WaitAsync(cancellationToken);

            if (timeStamps.TryGetValue(key, out var value))
            {
                value.UtcEnded = DateTime.UtcNow;
            }

            semaphore.Release();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    semaphore.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private sealed class StartedEnded
        {
            public DateTime UtcStarted { get; set; } = DateTime.UtcNow;
            public DateTime? UtcEnded { get; set; }

            public int Weight { get; set; } = 1;
        }
    }
}

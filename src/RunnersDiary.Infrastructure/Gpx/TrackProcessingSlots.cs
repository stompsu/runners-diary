using Microsoft.Extensions.Options;

namespace RunnersDiary.Infrastructure.Gpx;

/// <summary>Bounds how many uploads are buffered, parsed and processed at once per replica (memory cap, ADR 0020 §1.6).</summary>
public sealed class TrackProcessingSlots(IOptions<GpxLimitsOptions> options) : IDisposable
{
    private readonly SemaphoreSlim _slots = new(Math.Max(0, options.Value.MaxConcurrentProcessing));

    /// <summary>A lease to dispose when processing ends, or null when no slot became free within <paramref name="wait"/>.</summary>
    public async Task<IDisposable?> TryAcquireAsync(TimeSpan wait, CancellationToken cancellationToken) =>
        await _slots.WaitAsync(wait, cancellationToken) ? new Lease(_slots) : null;

    public void Dispose() => _slots.Dispose();

    private sealed class Lease(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                slots.Release();
            }
        }
    }
}

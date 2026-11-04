namespace RunnersDiary.Infrastructure.Guests;

/// <summary>One guest-cleanup pass; the seam between the schedulers (hosted service, job mode) and <see cref="GuestCleanup"/>.</summary>
public interface IGuestCleanupPass
{
    /// <summary>Removes up to one batch of expired guests; returns how many were fully removed.</summary>
    Task<int> RunOnceAsync(CancellationToken cancellationToken);
}

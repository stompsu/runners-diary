using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Tracks;
using RunnersDiary.Domain.Time;

namespace RunnersDiary.Domain.Runs;

/// <summary>A planned or completed run (ADR 0008 time model). Race details, rating and location are value objects inside the aggregate.</summary>
public sealed class Run : IUserOwned
{
    private readonly List<GpxTrack> _tracks = [];

    private Run()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>Local calendar date: source of truth for the calendar, ISO weeks and all metrics.</summary>
    public DateOnly Date { get; private set; }

    public TimeOnly? StartTime { get; private set; }

    public string TimeZoneId { get; private set; } = string.Empty;

    /// <summary>Derived from Date, StartTime and TimeZoneId; ordering and GPX matching only, never grouping.</summary>
    public DateTime? StartUtc { get; private set; }

    public RunStatus Status { get; private set; }

    public short RunTypeId { get; private set; }

    public string? Title { get; private set; }

    public string? Notes { get; private set; }

    public double? DistanceMeters { get; private set; }

    public int? DurationSeconds { get; private set; }

    public double? ElevationGainMeters { get; private set; }

    public double? ElevationLossMeters { get; private set; }

    public int? AvgHeartRate { get; private set; }

    public int? MaxHeartRate { get; private set; }

    public int? Rpe { get; private set; }

    public string? Gear { get; private set; }

    public string? Weather { get; private set; }

    public Surface? Surface { get; private set; }

    public RunLocation? Location { get; private set; }

    public RaceDetails? Race { get; private set; }

    public RunRating? Rating { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    /// <summary>Concurrency token (SQL rowversion); exposed to the API as <c>version</c>.</summary>
#pragma warning disable CA1819 // Justification: rowversion is a byte[] by EF/SQL Server contract.
    public byte[] RowVersion { get; private set; } = [];
#pragma warning restore CA1819

    public IReadOnlyCollection<GpxTrack> Tracks => _tracks;

    public static Run Create(Guid userId, RunValues values, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (userId == Guid.Empty)
        {
            throw new DomainRuleException("A run needs an owner.");
        }

        RunRules.EnsureValid(values);
        var run = new Run { Id = Guid.CreateVersion7(), UserId = userId, CreatedAt = Utc(now) };
        run.Apply(values, now);
        return run;
    }

    public void Update(RunValues values, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(values);
        RunRules.EnsureValid(values);
        Apply(values, now);
    }

    private void Apply(RunValues v, DateTimeOffset now)
    {
        Date = v.Date;
        StartTime = v.StartTime is { } t ? new TimeOnly(t.Hour, t.Minute, t.Second) : null;
        TimeZoneId = v.TimeZoneId;
        StartUtc = LocalStartResolver.ResolveUtc(Date, StartTime, TimeZoneId);
        Status = v.Status;
        RunTypeId = v.RunTypeId;
        Title = v.Title;
        Notes = v.Notes;
        DistanceMeters = v.DistanceMeters;
        DurationSeconds = v.DurationSeconds;
        ElevationGainMeters = v.ElevationGainMeters;
        ElevationLossMeters = v.ElevationLossMeters;
        AvgHeartRate = v.AvgHeartRate;
        MaxHeartRate = v.MaxHeartRate;
        Rpe = v.Rpe;
        Gear = v.Gear;
        Weather = v.Weather;
        Surface = v.Surface;
        Location = v.Location;
        Race = v.Race;
        Rating = v.Rating;
        UpdatedAt = Utc(now);
    }

    private static DateTime Utc(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        return new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
    }
}

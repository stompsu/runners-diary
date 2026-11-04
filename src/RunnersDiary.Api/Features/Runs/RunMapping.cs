using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Api.Features.Runs;

internal static class RunMapping
{
    public static RunDto ToDto(Run run) => new()
    {
        Id = run.Id,
        Date = run.Date,
        StartTime = run.StartTime,
        TimeZoneId = run.TimeZoneId,
        StartUtc = run.StartUtc,
        Status = run.Status,
        RunTypeId = run.RunTypeId,
        Title = run.Title,
        Notes = run.Notes,
        Location = run.Location is { } l ? new LocationDto { Latitude = l.Point.Latitude, Longitude = l.Point.Longitude, Name = l.Name } : null,
        DistanceMeters = run.DistanceMeters,
        DurationSeconds = run.DurationSeconds,
        ElevationGainMeters = run.ElevationGainMeters,
        ElevationLossMeters = run.ElevationLossMeters,
        AvgHeartRate = run.AvgHeartRate,
        MaxHeartRate = run.MaxHeartRate,
        Rpe = run.Rpe,
        Gear = run.Gear,
        Weather = run.Weather,
        Surface = run.Surface,
        Race = run.Race is { } r
            ? new RaceDto
            {
                Name = r.Name,
                DistanceCategory = r.DistanceCategory,
                OfficialDistanceMeters = r.OfficialDistanceMeters,
                BibNumber = r.BibNumber,
                TargetTimeSeconds = r.TargetTimeSeconds,
                ResultTimeSeconds = r.ResultTimeSeconds,
                OverallPlace = r.OverallPlace,
                CategoryPlace = r.CategoryPlace,
                CategoryName = r.CategoryName,
                ResultsUrl = r.ResultsUrl,
            }
            : null,
        Rating = run.Rating is { } g ? new RatingDto { Stars = g.Stars, Comment = g.Comment } : null,
        Version = Convert.ToBase64String(run.RowVersion),
        Tracks = [.. run.Tracks.OrderBy(t => t.Kind).Select(t => new RunTrackDto { Id = t.Id, Kind = t.Kind, Status = t.Status, OriginalFileName = t.OriginalFileName, CreatedAt = t.CreatedAt })],
        CreatedAt = run.CreatedAt,
        UpdatedAt = run.UpdatedAt,
    };

    public static bool TryDecodeVersion(string? version, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var buffer = new byte[version.Length];
        if (!Convert.TryFromBase64String(version, buffer, out var written) || written == 0)
        {
            return false;
        }

        bytes = buffer[..written];
        return true;
    }
}

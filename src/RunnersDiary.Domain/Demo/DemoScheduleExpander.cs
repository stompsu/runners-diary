using RunnersDiary.Domain.Calendar;
using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Domain.Demo;

/// <summary>Turns a week-relative template into dated runs for a given "today" (pure; today comes from <see cref="Time.UserToday"/>).</summary>
public static class DemoScheduleExpander
{
    public static IReadOnlyList<DemoRun> Expand(DemoTemplate template, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(template);
        var currentMonday = IsoWeek.MondayOf(today);
        var result = new List<DemoRun>(template.Runs.Count);

        foreach (var entry in template.Runs)
        {
            var type = RunTypeCatalog.FindByCode(entry.TypeCode)
                ?? throw new DomainRuleException($"Unknown run type code '{entry.TypeCode}' in demo template.");

            var dayOffset = ((int)entry.Day + 6) % 7;
            var date = currentMonday.AddDays((entry.Week * 7) + dayOffset);
            var future = date > today;
            var status = future ? RunStatus.Planned : entry.Status;

            var values = new RunValues
            {
                Date = date,
                StartTime = entry.StartTime,
                TimeZoneId = template.TimeZoneId,
                Status = status,
                RunTypeId = type.Id,
                Title = entry.Title,
                Notes = entry.Notes,
                Location = entry.Location,
                DistanceMeters = entry.DistanceMeters,
                DurationSeconds = entry.DurationSeconds,
                ElevationGainMeters = entry.ElevationGainMeters,
                ElevationLossMeters = entry.ElevationLossMeters,
                AvgHeartRate = entry.AvgHeartRate,
                MaxHeartRate = entry.MaxHeartRate,
                Rpe = entry.Rpe,
                Gear = entry.Gear,
                Weather = entry.Weather,
                Surface = entry.Surface,
                Race = entry.Race,
                Rating = entry.Rating,
            };

            // A completed entry that falls after today becomes a plan: actual-only data is dropped (including its recorded track).
            var gpx = entry.Gpx;
            if (future && entry.Status == RunStatus.Completed)
            {
                values = values with
                {
                    Rating = null,
                    AvgHeartRate = null,
                    MaxHeartRate = null,
                    Rpe = null,
                    ElevationLossMeters = null,
                    Weather = null,
                    Race = entry.Race is null ? null : entry.Race with { ResultTimeSeconds = null, OverallPlace = null, CategoryPlace = null },
                };
                gpx = null;
            }

            result.Add(new DemoRun(values, gpx));
        }

        return result;
    }
}

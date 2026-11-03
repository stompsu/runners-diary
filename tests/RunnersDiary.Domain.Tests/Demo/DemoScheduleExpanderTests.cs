using RunnersDiary.Domain.Demo;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.Tracks;

namespace RunnersDiary.Domain.Tests.Demo;

// The test over the real seed/demo/runs.json lives in DemoDatasetTests.
public sealed class DemoScheduleExpanderTests
{
    private static readonly DateOnly Wednesday = new(2026, 11, 4);

    private static DemoTemplate Template(params DemoRunTemplate[] runs) => new(1, "Europe/Warsaw", runs);

    [Fact]
    public void Week_zero_is_the_current_iso_week_and_sunday_is_its_last_day()
    {
        var runs = DemoScheduleExpander.Expand(
            Template(
                new DemoRunTemplate(0, DayOfWeek.Monday, "easy", DistanceMeters: 8000, DurationSeconds: 2700),
                new DemoRunTemplate(-1, DayOfWeek.Sunday, "long", DistanceMeters: 20000, DurationSeconds: 7000)),
            Wednesday);

        Assert.Equal(new DateOnly(2026, 11, 2), runs[0].Values.Date);
        Assert.Equal(new DateOnly(2026, 11, 1), runs[1].Values.Date);
        Assert.All(runs, r => Assert.Equal(RunStatus.Completed, r.Values.Status));
        Assert.All(runs, r => Assert.Empty(RunRules.Validate(r.Values)));
    }

    [Fact]
    public void Completed_entries_after_today_become_plans_without_actual_data()
    {
        var race = new RaceDetails("Silesia Half", RaceDistanceCategory.HalfMarathon, ResultTimeSeconds: 6000, OverallPlace: 40);
        var runs = DemoScheduleExpander.Expand(
            Template(new DemoRunTemplate(
                0, DayOfWeek.Sunday, "roadRace",
                DistanceMeters: 21097, DurationSeconds: 6000, AvgHeartRate: 160, Rpe: 8, Race: race, Rating: new RunRating(5),
                Gpx: new DemoGpxRef("gpx/half.gpx", GpxTrackKind.RecordedTrack))),
            Wednesday);

        var planned = Assert.Single(runs);
        Assert.Equal(new DateOnly(2026, 11, 8), planned.Values.Date);
        Assert.Equal(RunStatus.Planned, planned.Values.Status);
        Assert.Null(planned.Values.Rating);
        Assert.Null(planned.Values.AvgHeartRate);
        Assert.Null(planned.Values.Race!.ResultTimeSeconds);
        Assert.Null(planned.Values.Race.OverallPlace);
        Assert.Null(planned.Gpx);
        Assert.Empty(RunRules.Validate(planned.Values));
    }

    [Fact]
    public void Planned_entries_keep_their_course_gpx()
    {
        var gpx = new DemoGpxRef("gpx/course.gpx", GpxTrackKind.PlannedCourse);
        var runs = DemoScheduleExpander.Expand(
            Template(new DemoRunTemplate(3, DayOfWeek.Sunday, "trailRace", RunStatus.Planned, Race: new RaceDetails("Beskid Trail", RaceDistanceCategory.Other), Gpx: gpx)),
            Wednesday);

        Assert.Equal(gpx, runs[0].Gpx);
        Assert.Equal(new DateOnly(2026, 11, 29), runs[0].Values.Date);
        Assert.Equal("Europe/Warsaw", runs[0].Values.TimeZoneId);
    }

    [Fact]
    public void Unknown_type_code_is_rejected() =>
        Assert.ThrowsAny<Exception>(() => DemoScheduleExpander.Expand(Template(new DemoRunTemplate(0, DayOfWeek.Monday, "nope")), Wednesday));
}

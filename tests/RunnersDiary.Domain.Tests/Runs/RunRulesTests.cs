using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Tests.Runs;

public sealed class RunRulesTests
{
    private const short RoadRace = 1;
    private const short Easy = 20;
    private const short Rest = 41;

    private static RunValues Valid(Func<RunValues, RunValues>? change = null)
    {
        var v = new RunValues
        {
            Date = new DateOnly(2026, 11, 3),
            TimeZoneId = "Europe/Warsaw",
            Status = RunStatus.Completed,
            RunTypeId = Easy,
            DistanceMeters = 10000,
            DurationSeconds = 3000,
        };
        return change is null ? v : change(v);
    }

    private static RunValues Race(Func<RaceDetails, RaceDetails>? change = null, RunStatus status = RunStatus.Completed)
    {
        var race = new RaceDetails("Gliwice 10K", RaceDistanceCategory.TenK);
        return Valid(v => v with { RunTypeId = RoadRace, Status = status, Race = change is null ? race : change(race) });
    }

    private static void AssertError(RunValues values, string path) =>
        Assert.Contains(RunRules.Validate(values), e => e.Path == path);

    [Fact]
    public void Valid_baseline_has_no_errors() => Assert.Empty(RunRules.Validate(Valid()));

    [Fact]
    public void Valid_race_with_result_and_rating_has_no_errors()
    {
        var v = Race(r => r with { ResultTimeSeconds = 2790, OverallPlace = 87, ResultsUrl = "https://example.org/results" }) with
        {
            Rating = new RunRating(4, "Fun"),
        };

        Assert.Empty(RunRules.Validate(v));
    }

    [Fact]
    public void Rating_requires_completed_status() =>
        AssertError(Valid(v => v with { Status = RunStatus.Planned, Rating = new RunRating(3) }), "rating");

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Rating_stars_must_be_1_to_5(int stars) =>
        AssertError(Valid(v => v with { Rating = new RunRating(stars) }), "rating.stars");

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Rating_stars_boundaries_are_accepted(int stars) =>
        Assert.Empty(RunRules.Validate(Valid(v => v with { Rating = new RunRating(stars) })));

    [Fact]
    public void Rating_comment_is_limited_to_500() =>
        AssertError(Valid(v => v with { Rating = new RunRating(3, new string('x', 501)) }), "rating.comment");

    [Fact]
    public void Race_only_for_race_types() =>
        AssertError(Valid(v => v with { Race = new RaceDetails("x", RaceDistanceCategory.FiveK) }), "race");

    [Theory]
    [InlineData("race.resultTimeSeconds")]
    [InlineData("race.overallPlace")]
    [InlineData("race.categoryPlace")]
    public void Race_results_require_completed_status(string path)
    {
        var v = Race(
            r => r with { ResultTimeSeconds = path.EndsWith("resultTimeSeconds", StringComparison.Ordinal) ? 100 : null, OverallPlace = path.EndsWith("overallPlace", StringComparison.Ordinal) ? 1 : null, CategoryPlace = path.EndsWith("categoryPlace", StringComparison.Ordinal) ? 1 : null },
            RunStatus.Planned);

        AssertError(v, path);
    }

    [Fact]
    public void Planned_race_with_target_time_is_valid() =>
        Assert.Empty(RunRules.Validate(Race(r => r with { TargetTimeSeconds = 2700 }, RunStatus.Planned)));

    [Fact]
    public void Race_name_is_required_and_limited()
    {
        AssertError(Race(r => r with { Name = " " }), "race.name");
        AssertError(Race(r => r with { Name = new string('x', 151) }), "race.name");
    }

    [Theory]
    [InlineData("ftp://example.org/x")]
    [InlineData("/relative")]
    [InlineData("javascript:alert(1)")]
    public void Race_results_url_must_be_absolute_http(string url) =>
        AssertError(Race(r => r with { ResultsUrl = url }), "race.resultsUrl");

    [Fact]
    public void Race_field_limits()
    {
        AssertError(Race(r => r with { BibNumber = new string('1', 21) }), "race.bibNumber");
        AssertError(Race(r => r with { CategoryName = new string('x', 51) }), "race.categoryName");
        AssertError(Race(r => r with { OfficialDistanceMeters = 0 }), "race.officialDistanceMeters");
        AssertError(Race(r => r with { OverallPlace = 0 }), "race.overallPlace");
        AssertError(Race(r => r with { TargetTimeSeconds = 0 }), "race.targetTimeSeconds");
        AssertError(Race(r => r with { DistanceCategory = (RaceDistanceCategory)99 }), "race.distanceCategory");
    }

    [Theory]
    [InlineData(nameof(RunValues.DistanceMeters), "distanceMeters")]
    [InlineData(nameof(RunValues.DurationSeconds), "durationSeconds")]
    [InlineData(nameof(RunValues.ElevationGainMeters), "elevationGainMeters")]
    [InlineData(nameof(RunValues.ElevationLossMeters), "elevationLossMeters")]
    [InlineData(nameof(RunValues.AvgHeartRate), "avgHeartRate")]
    [InlineData(nameof(RunValues.MaxHeartRate), "maxHeartRate")]
    [InlineData(nameof(RunValues.Rpe), "rpe")]
    public void Rest_day_carries_no_metrics(string property, string path)
    {
        var v = Valid(x => x with { RunTypeId = Rest, DistanceMeters = null, DurationSeconds = null });
        v = property switch
        {
            nameof(RunValues.DistanceMeters) => v with { DistanceMeters = 1000 },
            nameof(RunValues.DurationSeconds) => v with { DurationSeconds = 60 },
            nameof(RunValues.ElevationGainMeters) => v with { ElevationGainMeters = 1 },
            nameof(RunValues.ElevationLossMeters) => v with { ElevationLossMeters = 1 },
            nameof(RunValues.AvgHeartRate) => v with { AvgHeartRate = 100 },
            nameof(RunValues.MaxHeartRate) => v with { MaxHeartRate = 100 },
            _ => v with { Rpe = 3 },
        };

        AssertError(v, path);
    }

    [Fact]
    public void Rest_day_with_only_a_note_is_valid() =>
        Assert.Empty(RunRules.Validate(Valid(v => v with { RunTypeId = Rest, DistanceMeters = null, DurationSeconds = null, Notes = "Sore calf" })));

    [Fact]
    public void Rest_day_rejects_a_rating() =>
        AssertError(Valid(v => v with { RunTypeId = Rest, DistanceMeters = null, DurationSeconds = null, Rating = new RunRating(3) }), "rating");

    [Fact]
    public void Max_heart_rate_must_not_be_below_average()
    {
        AssertError(Valid(v => v with { AvgHeartRate = 150, MaxHeartRate = 149 }), "maxHeartRate");
        Assert.Empty(RunRules.Validate(Valid(v => v with { AvgHeartRate = 150, MaxHeartRate = 150 })));
    }

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(250, true)]
    [InlineData(251, false)]
    public void Heart_rate_boundaries(int hr, bool ok) =>
        Assert.Equal(ok, !RunRules.Validate(Valid(v => v with { AvgHeartRate = hr })).Any(e => e.Path == "avgHeartRate"));

    [Theory]
    [InlineData(0, false)]
    [InlineData(0.5, true)]
    [InlineData(1_000_000, true)]
    [InlineData(1_000_001, false)]
    [InlineData(-5, false)]
    public void Distance_boundaries(double meters, bool ok) =>
        Assert.Equal(ok, !RunRules.Validate(Valid(v => v with { DistanceMeters = meters })).Any(e => e.Path == "distanceMeters"));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(604_800, true)]
    [InlineData(604_801, false)]
    public void Duration_boundaries(int seconds, bool ok) =>
        Assert.Equal(ok, !RunRules.Validate(Valid(v => v with { DurationSeconds = seconds })).Any(e => e.Path == "durationSeconds"));

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(100_000, true)]
    [InlineData(100_001, false)]
    public void Elevation_boundaries(double meters, bool ok) =>
        Assert.Equal(ok, !RunRules.Validate(Valid(v => v with { ElevationGainMeters = meters })).Any(e => e.Path == "elevationGainMeters"));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void Rpe_boundaries(int rpe, bool ok) =>
        Assert.Equal(ok, !RunRules.Validate(Valid(v => v with { Rpe = rpe })).Any(e => e.Path == "rpe"));

    [Fact]
    public void Time_zone_must_be_iana()
    {
        AssertError(Valid(v => v with { TimeZoneId = "Central European Standard Time" }), "timeZoneId");
        AssertError(Valid(v => v with { TimeZoneId = "" }), "timeZoneId");
    }

    [Fact]
    public void Date_must_be_within_supported_range()
    {
        AssertError(Valid(v => v with { Date = new DateOnly(1949, 12, 31) }), "date");
        AssertError(Valid(v => v with { Date = new DateOnly(2101, 1, 1) }), "date");
        Assert.Empty(RunRules.Validate(Valid(v => v with { Date = new DateOnly(1950, 1, 1) })));
        Assert.Empty(RunRules.Validate(Valid(v => v with { Date = new DateOnly(2100, 12, 31) })));
    }

    [Fact]
    public void Unknown_run_type_status_and_surface_are_rejected()
    {
        AssertError(Valid(v => v with { RunTypeId = 999 }), "runTypeId");
        AssertError(Valid(v => v with { Status = (RunStatus)9 }), "status");
        AssertError(Valid(v => v with { Surface = (Surface)9 }), "surface");
    }

    [Fact]
    public void Text_length_limits()
    {
        AssertError(Valid(v => v with { Title = new string('x', 121) }), "title");
        AssertError(Valid(v => v with { Notes = new string('x', 4001) }), "notes");
        AssertError(Valid(v => v with { Gear = new string('x', 101) }), "gear");
        AssertError(Valid(v => v with { Weather = new string('x', 201) }), "weather");
        AssertError(Valid(v => v with { Location = new RunLocation(new GeoPoint(50, 18), new string('x', 201)) }), "location.name");
    }

    [Fact]
    public void Several_violations_are_all_reported()
    {
        var errors = RunRules.Validate(Valid(v => v with { Status = RunStatus.Planned, Rating = new RunRating(9), TimeZoneId = "x", Rpe = 11 }));

        Assert.Equal(4, errors.Count);
    }

    [Fact]
    public void Geo_point_rejects_out_of_range_coordinates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeoPoint(91, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeoPoint(0, -181));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeoPoint(double.NaN, 0));
        Assert.Equal(50.29, new GeoPoint(50.29, 18.67).Latitude);
    }
}

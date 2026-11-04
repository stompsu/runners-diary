namespace RunnersDiary.Domain.Insights.Narrative;

/// <summary>
/// The unit of a fact (ADR 0026 §1). The SPA formats a resolved value by its unit with the existing helpers; the wire names are
/// the camelCase names below (<see cref="FactUnits.ToWire"/>).
/// </summary>
public enum FactUnit
{
    Meters,
    Seconds,
    Load,
    Ratio,
    Percent,
    Share,
    SecondsPerKm,
    SecondsPerKmPerWeek,
    Count,
    Days,
    Date,
    Band,
    RunType,
    RaceCategory,
    RaceStatus,
    Flag,
    Stars,
}

public static class FactUnits
{
    public static string ToWire(this FactUnit unit) => unit switch
    {
        FactUnit.Meters => "meters",
        FactUnit.Seconds => "seconds",
        FactUnit.Load => "load",
        FactUnit.Ratio => "ratio",
        FactUnit.Percent => "percent",
        FactUnit.Share => "share",
        FactUnit.SecondsPerKm => "secondsPerKm",
        FactUnit.SecondsPerKmPerWeek => "secondsPerKmPerWeek",
        FactUnit.Count => "count",
        FactUnit.Days => "days",
        FactUnit.Date => "date",
        FactUnit.Band => "band",
        FactUnit.RunType => "runType",
        FactUnit.RaceCategory => "raceCategory",
        FactUnit.RaceStatus => "raceStatus",
        FactUnit.Flag => "flag",
        FactUnit.Stars => "stars",
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown fact unit."),
    };

    public static FactUnit FromWire(string wire) => wire switch
    {
        "meters" => FactUnit.Meters,
        "seconds" => FactUnit.Seconds,
        "load" => FactUnit.Load,
        "ratio" => FactUnit.Ratio,
        "percent" => FactUnit.Percent,
        "share" => FactUnit.Share,
        "secondsPerKm" => FactUnit.SecondsPerKm,
        "secondsPerKmPerWeek" => FactUnit.SecondsPerKmPerWeek,
        "count" => FactUnit.Count,
        "days" => FactUnit.Days,
        "date" => FactUnit.Date,
        "band" => FactUnit.Band,
        "runType" => FactUnit.RunType,
        "raceCategory" => FactUnit.RaceCategory,
        "raceStatus" => FactUnit.RaceStatus,
        "flag" => FactUnit.Flag,
        "stars" => FactUnit.Stars,
        _ => throw new ArgumentOutOfRangeException(nameof(wire), wire, "Unknown fact unit."),
    };
}

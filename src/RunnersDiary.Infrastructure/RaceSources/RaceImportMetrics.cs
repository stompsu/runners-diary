using System.Diagnostics.Metrics;

namespace RunnersDiary.Infrastructure.RaceSources;

/// <summary>Meter <c>RunnersDiary.RaceImport</c> (ADR 0038 §5): requests by source and status class, and applied catalogue changes by source and kind.</summary>
internal static class RaceImportMetrics
{
    public const string MeterName = "RunnersDiary.RaceImport";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Requests = Meter.CreateCounter<long>("races.import.requests", description: "HTTP requests to a race source, by status class.");

    private static readonly Counter<long> Upserts = Meter.CreateCounter<long>("races.import.upserts", description: "Catalogue rows inserted, updated or removed by the import.");

    public static void RecordRequest(string source, string statusClass) =>
        Requests.Add(1, new KeyValuePair<string, object?>("source", source), new KeyValuePair<string, object?>("status_class", statusClass));

    public static void RecordUpserts(string source, string kind, int count)
    {
        if (count > 0)
        {
            Upserts.Add(count, new KeyValuePair<string, object?>("source", source), new KeyValuePair<string, object?>("kind", kind));
        }
    }
}

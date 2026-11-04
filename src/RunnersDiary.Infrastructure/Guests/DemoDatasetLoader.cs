using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Demo;
using RunnersDiary.Domain.Gpx;
using RunnersDiary.Infrastructure.Gpx;
using RunnersDiary.Infrastructure.Runs;

namespace RunnersDiary.Infrastructure.Guests;

/// <summary>
/// One demo GPX file loaded from disk, together with its pre-processed result: the track is parsed and processed once per process
/// and every guest reuses the summary and the GeoJSON bytes (ADR 0021 §8).
/// </summary>
public sealed class DemoFile(string fileName, byte[] content, ProcessedTrack processed, byte[] geoJson)
{
    public string FileName { get; } = fileName;

    public ReadOnlyMemory<byte> Content { get; } = content;

    public ReadOnlyMemory<byte> Sha256 { get; } = SHA256.HashData(content);

    public ProcessedTrack Processed { get; } = processed;

    /// <summary>The <c>track.json</c> document (<see cref="TrackGeoJsonWriter"/>).</summary>
    public ReadOnlyMemory<byte> GeoJson { get; } = geoJson;
}

/// <summary>The parsed template plus every GPX file it references, keyed by the path used in <c>runs.json</c>.</summary>
public sealed class DemoDataset(DemoTemplate template, IReadOnlyDictionary<string, DemoFile> files)
{
    public DemoTemplate Template { get; } = template;

    public IReadOnlyDictionary<string, DemoFile> Files { get; } = files;
}

/// <summary>
/// Loads <c>seed/demo/runs.json</c> and the GPX files it references (they are copied next to the API binaries) and keeps them in
/// memory, processed once: the files are small (&lt; 130 KB each) and every guest gets a copy. <see cref="DemoDatasetStartupCheck"/> loads and validates
/// it at startup so a broken dataset fails the host instead of every guest creation; a failed load is never cached.
/// </summary>
public sealed class DemoDatasetLoader(IOptions<GuestOptions> options, TimeProvider clock)
{
    private DemoDataset? _dataset;

    public DemoDataset Get()
    {
        var current = Volatile.Read(ref _dataset);
        if (current is not null)
        {
            return current;
        }

        var loaded = Load(options.Value.DemoDataPath, clock.GetUtcNow());
        return Interlocked.CompareExchange(ref _dataset, loaded, null) ?? loaded;
    }

    /// <summary>
    /// Loads the dataset and checks that it expands for <paramref name="today"/> within the guest run caps. Throws on any problem.
    /// </summary>
    public DemoDataset LoadAndValidate(RunLimitsOptions limits, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var dataset = Get();
        var runs = DemoScheduleExpander.Expand(dataset.Template, today);
        if (runs.Count > limits.GuestMaxRuns)
        {
            throw new InvalidOperationException($"The demo dataset has {runs.Count} runs, more than the guest cap of {limits.GuestMaxRuns}.");
        }

        var busiest = runs.GroupBy(r => r.Values.Date).Select(g => g.Count()).DefaultIfEmpty(0).Max();
        if (busiest > limits.MaxRunsPerDate)
        {
            throw new InvalidOperationException($"The demo dataset has {busiest} runs on one date, more than the per-date cap of {limits.MaxRunsPerDate}.");
        }

        return dataset;
    }

    public static DemoDataset Load(string directory, DateTimeOffset nowUtc)
    {
        var root = Path.GetFullPath(Path.IsPathRooted(directory) ? directory : Path.Combine(AppContext.BaseDirectory, directory));
        var runsPath = Path.Combine(root, "runs.json");
        if (!File.Exists(runsPath))
        {
            throw new FileNotFoundException($"Demo dataset not found at '{runsPath}'.", runsPath);
        }

        var template = DemoTemplateParser.Parse(File.ReadAllText(runsPath));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var files = new Dictionary<string, DemoFile>(StringComparer.Ordinal);

        foreach (var reference in template.Runs.Where(r => r.Gpx is not null).Select(r => r.Gpx!.File).Distinct(StringComparer.Ordinal))
        {
            var full = Path.GetFullPath(Path.Combine(root, reference));
            if (!full.StartsWith(rootWithSeparator, StringComparison.Ordinal))
            {
                throw new DomainRuleException($"Demo GPX reference '{reference}' points outside the dataset directory.");
            }

            files[reference] = ProcessFile(reference, full, nowUtc);
        }

        return new DemoDataset(template, files);
    }

    /// <summary>Parses and processes one demo file; a rejected file is a packaging error and fails the load (and so host startup).</summary>
    private static DemoFile ProcessFile(string reference, string fullPath, DateTimeOffset nowUtc)
    {
        var bytes = File.ReadAllBytes(fullPath);
        using var stream = new MemoryStream(bytes, writable: false);
        var result = GpxReader.Read(stream, GpxReadLimits.Default, nowUtc);
        if (result.Document is not { } document)
        {
            throw new InvalidOperationException($"Demo GPX '{reference}' was rejected: {result.Rejection?.CodeName ?? "unknown"}.");
        }

        var processed = TrackProcessor.Process(document);
        return new DemoFile(Path.GetFileName(fullPath), bytes, processed, TrackGeoJsonWriter.Write(processed));
    }
}

using System.Text.RegularExpressions;
using RunnersDiary.Domain.Gpx;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Races;

/// <summary>A race as the duplicate matcher sees it.</summary>
public sealed record RaceDuplicateCandidate(
    Guid Id,
    RaceSource Source,
    string Name,
    DateOnly Date,
    GeoPoint? Point,
    string? LocationName,
    DateTime ImportedUtc);

/// <summary>
/// Duplicate detection within and across sources (ADR 0036 §4): same date, at most 3 km apart (or the same folded locality when a point is missing),
/// and a name similarity of at least 0.8 (token-set Jaccard on the folded name without noise tokens).
/// </summary>
public static partial class RaceDuplicateMatcher
{
    public const double SimilarityThreshold = 0.8;
    public const double MaxDistanceMeters = 3_000;

    private static readonly HashSet<string> NoiseTokens = new(StringComparer.Ordinal) { "bieg", "run", "edycja", "edycji" };

    /// <summary>Folded name tokens without noise: edition numbers (roman, "12th", "12-ty", "12."), years, and the words bieg, run, edycja.</summary>
    public static IReadOnlyList<string> NameTokens(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        // Ordinals must go before folding removes the dot and the dash that mark them.
        var stripped = OrdinalMarker().Replace(name, " ");
        var tokens = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var token in TextFolding.Fold(stripped).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (NoiseTokens.Contains(token) || YearToken().IsMatch(token) || RomanToken().IsMatch(token))
            {
                continue;
            }

            tokens.Add(token);
        }

        return tokens.ToList();
    }

    public static double Similarity(string? a, string? b)
    {
        var ta = NameTokens(a);
        var tb = NameTokens(b);
        if (ta.Count == 0 && tb.Count == 0)
        {
            // Names made only of noise: equal only when the folded texts are equal.
            return TextFolding.Fold(a).Length > 0 && TextFolding.Fold(a) == TextFolding.Fold(b) ? 1 : 0;
        }

        var intersection = ta.Intersect(tb, StringComparer.Ordinal).Count();
        var union = ta.Count + tb.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    public static bool IsDuplicate(RaceDuplicateCandidate a, RaceDuplicateCandidate b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return a.Date == b.Date && NearEnough(a, b) && Similarity(a.Name, b.Name) >= SimilarityThreshold;
    }

    /// <summary>
    /// Clusters the candidates and returns, for every candidate, the id of its canonical race, or null when it is canonical itself.
    /// The canonical race of a cluster is the one with the smallest <see cref="RaceDuplicateCandidate.ImportedUtc"/>, then the lower source value, then the lower id.
    /// </summary>
    public static IReadOnlyDictionary<Guid, Guid?> Resolve(IReadOnlyList<RaceDuplicateCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var parent = Enumerable.Range(0, candidates.Count).ToArray();

        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        foreach (var group in Enumerable.Range(0, candidates.Count).GroupBy(i => candidates[i].Date))
        {
            var indexes = group.ToArray();
            for (var x = 0; x < indexes.Length; x++)
            {
                for (var y = x + 1; y < indexes.Length; y++)
                {
                    if (IsDuplicate(candidates[indexes[x]], candidates[indexes[y]]))
                    {
                        parent[Find(indexes[x])] = Find(indexes[y]);
                    }
                }
            }
        }

        var canonicalOfRoot = new Dictionary<int, RaceDuplicateCandidate>();
        for (var i = 0; i < candidates.Count; i++)
        {
            var root = Find(i);
            if (!canonicalOfRoot.TryGetValue(root, out var best) || Precedes(candidates[i], best))
            {
                canonicalOfRoot[root] = candidates[i];
            }
        }

        var result = new Dictionary<Guid, Guid?>();
        for (var i = 0; i < candidates.Count; i++)
        {
            var canonical = canonicalOfRoot[Find(i)];
            result[candidates[i].Id] = canonical.Id == candidates[i].Id ? null : canonical.Id;
        }

        return result;
    }

    private static bool Precedes(RaceDuplicateCandidate a, RaceDuplicateCandidate b)
    {
        if (a.ImportedUtc != b.ImportedUtc)
        {
            return a.ImportedUtc < b.ImportedUtc;
        }

        if (a.Source != b.Source)
        {
            return a.Source < b.Source;
        }

        return a.Id.CompareTo(b.Id) < 0;
    }

    private static bool NearEnough(RaceDuplicateCandidate a, RaceDuplicateCandidate b)
    {
        if (a.Point is { } pa && b.Point is { } pb)
        {
            return Haversine.Distance(pa.Latitude, pa.Longitude, pb.Latitude, pb.Longitude) <= MaxDistanceMeters;
        }

        var la = TextFolding.Fold(a.LocationName);
        return la.Length > 0 && la == TextFolding.Fold(b.LocationName);
    }

    // "12th", "1st", "12-ty", "12-ta", "12." : a number glued to an ordinal marker. A bare number ("10") is a distance, not an edition.
    [GeneratedRegex(@"(?<![\p{L}\d])\d{1,3}(?:\.(?!\d)|\s?-\s?(?:ty|ta|te|ci|gi|szy|sza|sze|ego|ej)|(?:st|nd|rd|th))(?![\p{L}\d])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex OrdinalMarker();

    [GeneratedRegex(@"^(?:19|20)\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex YearToken();

    [GeneratedRegex(@"^(?=[ivxlc])c{0,1}(?:xc|xl|l?x{0,3})(?:ix|iv|v?i{0,3})$", RegexOptions.CultureInvariant)]
    private static partial Regex RomanToken();
}

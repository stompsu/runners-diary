using System.Collections.Frozen;

namespace RunnersDiary.Domain.Races;

/// <summary>One Polish voivodeship: ISO 3166-2:PL code with its English and Polish names.</summary>
public sealed record PolishRegion(string Code, string EnglishName, string PolishName);

/// <summary>The 16 voivodeships (ISO 3166-2:PL, <c>PL-02</c> to <c>PL-32</c>). Lookup ignores case and diacritics and accepts "... Voivodeship" / "województwo ..." forms.</summary>
public static class PolishRegions
{
    public static IReadOnlyList<PolishRegion> All { get; } =
    [
        new("PL-02", "Lower Silesian", "dolnośląskie"),
        new("PL-04", "Kuyavian-Pomeranian", "kujawsko-pomorskie"),
        new("PL-06", "Lublin", "lubelskie"),
        new("PL-08", "Lubusz", "lubuskie"),
        new("PL-10", "Łódź", "łódzkie"),
        new("PL-12", "Lesser Poland", "małopolskie"),
        new("PL-14", "Masovian", "mazowieckie"),
        new("PL-16", "Opole", "opolskie"),
        new("PL-18", "Subcarpathian", "podkarpackie"),
        new("PL-20", "Podlaskie", "podlaskie"),
        new("PL-22", "Pomeranian", "pomorskie"),
        new("PL-24", "Silesian", "śląskie"),
        new("PL-26", "Świętokrzyskie", "świętokrzyskie"),
        new("PL-28", "Warmian-Masurian", "warmińsko-mazurskie"),
        new("PL-30", "Greater Poland", "wielkopolskie"),
        new("PL-32", "West Pomeranian", "zachodniopomorskie"),
    ];

    private static readonly FrozenDictionary<string, PolishRegion> ByCode =
        All.ToFrozenDictionary(r => r.Code, StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, PolishRegion> ByFoldedName = BuildNameIndex();

    private static readonly string[] Affixes = ["voivodeship", "voivodship", "wojewodztwo", "woj"];

    public static bool IsKnown(string? code) => code is not null && ByCode.ContainsKey(code.Trim());

    public static PolishRegion? Find(string? code) => code is not null && ByCode.TryGetValue(code.Trim(), out var r) ? r : null;

    /// <summary>Resolves a code ("PL-24", "pl24"), an English or Polish name, or a common alias ("Silesia", "Silesian Voivodeship") to the ISO code.</summary>
    public static bool TryGetCode(string? text, out string code)
    {
        code = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (ByCode.TryGetValue(text.Trim(), out var byCode))
        {
            code = byCode.Code;
            return true;
        }

        var folded = TextFolding.Fold(text);
        var squeezed = folded.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (squeezed.Length == 4 && squeezed.StartsWith("pl", StringComparison.Ordinal) && ByCode.TryGetValue($"PL-{squeezed[2..]}", out var compact))
        {
            code = compact.Code;
            return true;
        }

        var words = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !Affixes.Contains(w, StringComparer.Ordinal));
        var key = string.Join(' ', words);
        if (ByFoldedName.TryGetValue(key, out var region))
        {
            code = region.Code;
            return true;
        }

        return false;
    }

    private static FrozenDictionary<string, PolishRegion> BuildNameIndex()
    {
        var index = new Dictionary<string, PolishRegion>(StringComparer.Ordinal);
        foreach (var r in All)
        {
            index[TextFolding.Fold(r.EnglishName)] = r;
            index[TextFolding.Fold(r.PolishName)] = r;
        }

        // Common English aliases of the source sites.
        var aliases = new (string Alias, string Code)[]
        {
            ("Lower Silesia", "PL-02"), ("Kuyavia-Pomerania", "PL-04"), ("Kuyavian Pomeranian", "PL-04"), ("Lodz", "PL-10"), ("Lodzkie", "PL-10"),
            ("Lesser Poland", "PL-12"), ("Malopolska", "PL-12"), ("Masovia", "PL-14"), ("Mazovia", "PL-14"), ("Subcarpathia", "PL-18"),
            ("Podlasie", "PL-20"), ("Pomerania", "PL-22"), ("Silesia", "PL-24"), ("Holy Cross", "PL-26"), ("Swietokrzyskie", "PL-26"),
            ("Warmia-Masuria", "PL-28"), ("Warmia and Masuria", "PL-28"), ("Greater Poland", "PL-30"), ("Wielkopolska", "PL-30"),
            ("West Pomerania", "PL-32"),
        };
        foreach (var (alias, code) in aliases)
        {
            index[TextFolding.Fold(alias)] = All.First(r => r.Code == code);
        }

        return index.ToFrozenDictionary(StringComparer.Ordinal);
    }
}

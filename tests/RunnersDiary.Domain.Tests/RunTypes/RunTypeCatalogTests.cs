using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Domain.Tests.RunTypes;

public sealed class RunTypeCatalogTests
{
    [Fact]
    public void Has_twenty_one_types_with_unique_ids_codes_and_sort_orders()
    {
        var all = RunTypeCatalog.All;

        Assert.Equal(21, all.Count);
        Assert.Equal(21, all.Select(t => t.Id).Distinct().Count());
        Assert.Equal(21, all.Select(t => t.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(21, all.Select(t => t.SortOrder).Distinct().Count());
    }

    [Fact]
    public void Ids_codes_and_categories_match_the_data_model()
    {
        Assert.Equal<int>([1, 2, 3, 4, 5, 6], Ids(RunTypeCategory.Race));
        Assert.Equal<int>([20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31], Ids(RunTypeCategory.Training));
        Assert.Equal<int>([42, 40, 41], Ids(RunTypeCategory.Other)); // Body Bar is listed first
        Assert.Equal("roadRace", RunTypeCatalog.Get(1).Code);
        Assert.Equal("long", RunTypeCatalog.Get(22).Code);
        Assert.Equal("rest", RunTypeCatalog.Get(41).Code);
        Assert.Equal("bodyBar", RunTypeCatalog.Get(42).Code);
    }

    [Fact]
    public void Flags_match_the_data_model()
    {
        var cross = RunTypeCatalog.Get(40);
        var rest = RunTypeCatalog.Get(41);
        var bodyBar = RunTypeCatalog.Get(42);

        Assert.False(cross.CountsTowardRunningDistance);
        Assert.True(cross.CountsTowardLoad);
        Assert.False(cross.IsRestDay);
        Assert.False(rest.CountsTowardRunningDistance);
        Assert.False(rest.CountsTowardLoad);
        Assert.True(rest.IsRestDay);
        Assert.False(bodyBar.CountsTowardRunningDistance);
        Assert.True(bodyBar.CountsTowardLoad);
        Assert.False(bodyBar.IsRestDay);
        Assert.All(RunTypeCatalog.All.Where(t => t.Id < 40), t =>
        {
            Assert.True(t.CountsTowardRunningDistance);
            Assert.True(t.CountsTowardLoad);
            Assert.False(t.IsRestDay);
        });
    }

    [Fact]
    public void Lookup_by_id_and_code()
    {
        Assert.Same(RunTypeCatalog.Get(23), RunTypeCatalog.FindByCode("tempo"));
        Assert.Null(RunTypeCatalog.Find(999));
        Assert.Null(RunTypeCatalog.FindByCode("nope"));
        Assert.Throws<ArgumentOutOfRangeException>(() => RunTypeCatalog.Get(999));
    }

    /// <summary>Ids of a category in display (sort) order.</summary>
    private static int[] Ids(RunTypeCategory category) =>
        [.. RunTypeCatalog.All.Where(t => t.Category == category).OrderBy(t => t.SortOrder).Select(t => (int)t.Id)];
}

using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Domain.Tests.RunTypes;

public sealed class RunTypeCatalogTests
{
    [Fact]
    public void Has_twenty_types_with_unique_ids_codes_and_sort_orders()
    {
        var all = RunTypeCatalog.All;

        Assert.Equal(20, all.Count);
        Assert.Equal(20, all.Select(t => t.Id).Distinct().Count());
        Assert.Equal(20, all.Select(t => t.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(20, all.Select(t => t.SortOrder).Distinct().Count());
    }

    [Fact]
    public void Ids_codes_and_categories_match_the_data_model()
    {
        Assert.Equal<int>([1, 2, 3, 4, 5, 6], Ids(RunTypeCategory.Race));
        Assert.Equal<int>([20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31], Ids(RunTypeCategory.Training));
        Assert.Equal<int>([40, 41], Ids(RunTypeCategory.Other));
        Assert.Equal("roadRace", RunTypeCatalog.Get(1).Code);
        Assert.Equal("long", RunTypeCatalog.Get(22).Code);
        Assert.Equal("rest", RunTypeCatalog.Get(41).Code);
    }

    [Fact]
    public void Flags_match_the_data_model()
    {
        var cross = RunTypeCatalog.Get(40);
        var rest = RunTypeCatalog.Get(41);

        Assert.False(cross.CountsTowardRunningDistance);
        Assert.True(cross.CountsTowardLoad);
        Assert.False(cross.IsRestDay);
        Assert.False(rest.CountsTowardRunningDistance);
        Assert.False(rest.CountsTowardLoad);
        Assert.True(rest.IsRestDay);
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

    private static int[] Ids(RunTypeCategory category) =>
        [.. RunTypeCatalog.All.Where(t => t.Category == category).Select(t => (int)t.Id).Order()];
}

namespace RunnersDiary.Domain.RunTypes;

/// <summary>Catalog entry (reference data). Ids are contracts and are never reused; <see cref="Code"/> is the frontend i18n key.</summary>
public sealed class RunType
{
    public RunType(short id, string code, RunTypeCategory category, bool countsTowardRunningDistance, bool countsTowardLoad, bool isRestDay, short sortOrder)
    {
        Id = id;
        Code = code;
        Category = category;
        CountsTowardRunningDistance = countsTowardRunningDistance;
        CountsTowardLoad = countsTowardLoad;
        IsRestDay = isRestDay;
        SortOrder = sortOrder;
    }

    public short Id { get; private set; }

    public string Code { get; private set; }

    public RunTypeCategory Category { get; private set; }

    public bool CountsTowardRunningDistance { get; private set; }

    public bool CountsTowardLoad { get; private set; }

    public bool IsRestDay { get; private set; }

    public short SortOrder { get; private set; }
}

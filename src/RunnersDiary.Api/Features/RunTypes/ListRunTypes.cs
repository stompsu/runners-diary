using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Api.Features.RunTypes;

public sealed record RunTypeDto
{
    public required short Id { get; init; }

    /// <summary>Stable code; also the frontend i18n key (<c>runTypes.&lt;code&gt;</c>).</summary>
    public required string Code { get; init; }

    public required RunTypeCategory Category { get; init; }

    public required bool CountsTowardRunningDistance { get; init; }

    public required bool CountsTowardLoad { get; init; }

    public required bool IsRestDay { get; init; }

    public required short SortOrder { get; init; }
}

public static class ListRunTypes
{
    private static readonly RunTypeDto[] Items =
    [
        .. RunTypeCatalog.All.OrderBy(t => t.SortOrder).Select(t => new RunTypeDto
        {
            Id = t.Id,
            Code = t.Code,
            Category = t.Category,
            CountsTowardRunningDistance = t.CountsTowardRunningDistance,
            CountsTowardLoad = t.CountsTowardLoad,
            IsRestDay = t.IsRestDay,
            SortOrder = t.SortOrder,
        }),
    ];

    public static IEndpointRouteBuilder MapRunTypeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/run-types", () => TypedResults.Ok(Items))
            .WithName("listRunTypes")
            .WithTags("RunTypes")
            .WithSummary("The fixed run-type catalog.")
            .Produces<RunTypeDto[]>(StatusCodes.Status200OK);
        return endpoints;
    }
}

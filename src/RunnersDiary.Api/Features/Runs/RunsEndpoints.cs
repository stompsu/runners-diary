namespace RunnersDiary.Api.Features.Runs;

public static class RunsEndpoints
{
    public static IEndpointRouteBuilder MapRunEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var runs = endpoints.MapGroup("/runs").WithTags("Runs");
        ListRuns.Map(runs);
        GetRun.Map(runs);
        CreateRun.Map(runs);
        UpdateRun.Map(runs);
        DeleteRun.Map(runs);
        return endpoints;
    }

    internal static IResult RunNotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Run not found.");
}

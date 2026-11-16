using RunnersDiary.Api.Features.Runs;
using RunnersDiary.Domain.Goals.Planning;

namespace RunnersDiary.Api.Features.TrainingSuggestions;

/// <summary>
/// One suggested session of a goal's plan. Not a run: it is in no total, insight or summary until accepted. <c>description</c> is model-written
/// plain text (render it as text, never markup).
/// </summary>
public sealed record TrainingSuggestionDto
{
    public required Guid Id { get; init; }

    public required Guid GoalId { get; init; }

    /// <summary>The generation that wrote the suggestion (matches <c>GoalDto.plan.generationId</c>).</summary>
    public required Guid GenerationId { get; init; }

    public required DateOnly Date { get; init; }

    /// <summary>A training run type (20-31) or rest (41).</summary>
    public required short RunTypeId { get; init; }

    public required double? DistanceMeters { get; init; }

    public required int? DurationSeconds { get; init; }

    public required TrainingIntensity Intensity { get; init; }

    /// <summary>The faster bound of the target pace in seconds per km; both bounds or neither.</summary>
    public required int? TargetPaceMinSecondsPerKm { get; init; }

    public required int? TargetPaceMaxSecondsPerKm { get; init; }

    public required string Description { get; init; }

    public required TrainingPhase Phase { get; init; }

    public required SuggestionStatus Status { get; init; }
}

public sealed record TrainingSuggestionListResponse
{
    public required DateOnly From { get; init; }

    public required DateOnly To { get; init; }

    public required IReadOnlyList<TrainingSuggestionDto> Items { get; init; }
}

/// <summary>Body of <c>POST /api/training-suggestions/{suggestionId}/accept</c>; the body is optional.</summary>
public sealed record AcceptTrainingSuggestionRequest
{
    /// <summary>"Edit &amp; accept": the run exactly as the pre-filled form sends it (validated like createRun). Absent = the default mapping of the suggestion.</summary>
    public RunWriteRequest? Run { get; init; }
}

internal static class TrainingSuggestionMapping
{
    public static TrainingSuggestionDto ToDto(TrainingSuggestion s) => new()
    {
        Id = s.Id,
        GoalId = s.GoalId,
        GenerationId = s.GenerationId,
        Date = s.Date,
        RunTypeId = s.RunTypeId,
        DistanceMeters = s.DistanceMeters,
        DurationSeconds = s.DurationSeconds,
        Intensity = s.Intensity,
        TargetPaceMinSecondsPerKm = s.TargetPaceMinSecondsPerKm,
        TargetPaceMaxSecondsPerKm = s.TargetPaceMaxSecondsPerKm,
        Description = s.Description,
        Phase = s.Phase,
        Status = s.Status,
    };
}

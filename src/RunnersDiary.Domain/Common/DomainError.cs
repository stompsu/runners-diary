namespace RunnersDiary.Domain.Common;

/// <summary>A single validation failure. <paramref name="Path"/> is a camelCase JSON path such as <c>race.resultTimeSeconds</c>.</summary>
public sealed record DomainError(string Path, string Message);

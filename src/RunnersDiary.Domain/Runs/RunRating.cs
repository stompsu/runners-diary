namespace RunnersDiary.Domain.Runs;

/// <summary>Experience rating of a completed run (1..5 stars plus a comment).</summary>
public sealed record RunRating(int Stars, string? Comment = null);

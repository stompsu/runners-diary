namespace RunnersDiary.Domain.Common;

/// <summary>Thrown when an entity invariant is violated. A safety net behind request validation; it signals a bug when raised from the API.</summary>
public sealed class DomainRuleException : Exception
{
    public DomainRuleException(IReadOnlyList<DomainError> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    public DomainRuleException()
        : this([])
    {
    }

    public DomainRuleException(string message)
        : base(message)
    {
        Errors = [new DomainError(string.Empty, message)];
    }

    public DomainRuleException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = [new DomainError(string.Empty, message)];
    }

    public IReadOnlyList<DomainError> Errors { get; }

    private static string BuildMessage(IReadOnlyList<DomainError> errors) =>
        "Domain rules violated: " + string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}"));
}

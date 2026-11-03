namespace RunnersDiary.Infrastructure.Identity;

/// <summary>
/// The caller, as seen by data access (ADR 0010). Handlers never read claims; the DbContext's "Owner" query filter and
/// ownership guard use this. Implemented over the HTTP context in the API; background work runs with <see cref="AnonymousCurrentUser"/>.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>The internal user id. Throws <see cref="InvalidOperationException"/> when not authenticated.</summary>
    Guid UserId { get; }

    /// <summary>The internal user id or null; used by EF query filters (null matches nothing: fail closed).</summary>
    Guid? UserIdOrNull { get; }

    bool IsGuest { get; }
}

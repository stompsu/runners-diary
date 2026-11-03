namespace RunnersDiary.Infrastructure.Identity;

/// <summary>A caller with a fixed id. For tests and for code that acts on behalf of a known user (the HTTP implementation lives in the API).</summary>
public sealed class FixedCurrentUser(Guid userId, bool isGuest = false) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public Guid UserId => userId;

    public Guid? UserIdOrNull => userId;

    public bool IsGuest => isGuest;
}

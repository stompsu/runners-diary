namespace RunnersDiary.Infrastructure.Identity;

/// <summary>No caller. Queries on owned data return nothing; used by background work, design time and as the fail-closed default.</summary>
public sealed class AnonymousCurrentUser : ICurrentUser
{
    public static AnonymousCurrentUser Instance { get; } = new();

    private AnonymousCurrentUser()
    {
    }

    public bool IsAuthenticated => false;

    public Guid UserId => throw new InvalidOperationException("There is no authenticated user.");

    public Guid? UserIdOrNull => null;

    public bool IsGuest => false;
}

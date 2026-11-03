namespace RunnersDiary.Domain.Common;

/// <summary>Marks an aggregate that belongs to exactly one user; the persistence layer scopes and guards it by <see cref="UserId"/>.</summary>
public interface IUserOwned
{
    Guid UserId { get; }
}

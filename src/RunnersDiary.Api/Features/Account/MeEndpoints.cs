using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RunnersDiary.Domain.Time;
using RunnersDiary.Domain.Users;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Api.Features.Account;

public sealed record MeResponse
{
    public required Guid Id { get; init; }

    public required UserKind Kind { get; init; }

    public required string? DisplayName { get; init; }

    public required string TimeZoneId { get; init; }

    public required bool TimeZoneIsDefault { get; init; }

    public required UnitSystem UnitSystem { get; init; }

    /// <summary>Today's date in the user's zone (the calendar's default month).</summary>
    public required DateOnly Today { get; init; }

    /// <summary>Guests only: when the demo account expires (UTC).</summary>
    public required DateTime? GuestExpiresAt { get; init; }
}

public sealed record UpdateMeRequest : IValidatableObject
{
    [StringLength(User.DisplayNameMaxLength)]
    public string? DisplayName { get; init; }

    public required string TimeZoneId { get; init; }

    public required UnitSystem UnitSystem { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!IanaTimeZone.IsValid(TimeZoneId))
        {
            yield return new ValidationResult("Must be a valid IANA time zone id (for example Europe/Warsaw).", ["timeZoneId"]);
        }

        if (!Enum.IsDefined(UnitSystem))
        {
            yield return new ValidationResult("Unknown unit system.", ["unitSystem"]);
        }
    }
}

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/me", GetAsync)
            .WithName("getMe")
            .WithTags("Me")
            .WithSummary("The caller's profile.")
            .Produces<MeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapPut("/me", UpdateAsync)
            .WithName("updateMe")
            .WithTags("Me")
            .WithSummary("Updates the caller's profile (display name, time zone, units).")
            .Produces<MeResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        [FromServices] ICurrentUser currentUser,
        [FromServices] RunnersDiaryDbContext db,
        [FromServices] TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return user is null ? NotFound() : TypedResults.Ok(ToResponse(user, clock));
    }

    private static async Task<IResult> UpdateAsync(
        UpdateMeRequest request,
        [FromServices] ICurrentUser currentUser,
        [FromServices] RunnersDiaryDbContext db,
        [FromServices] TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        user.UpdateProfile(request.DisplayName, request.TimeZoneId, request.UnitSystem);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToResponse(user, clock));
    }

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult NotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "User not found.");

    private static MeResponse ToResponse(User user, TimeProvider clock) => new()
    {
        Id = user.Id,
        Kind = user.Kind,
        DisplayName = user.DisplayName,
        TimeZoneId = user.TimeZoneId,
        TimeZoneIsDefault = user.TimeZoneIsDefault,
        UnitSystem = user.UnitSystem,
        Today = UserToday.Get(clock, user.TimeZoneId),
        GuestExpiresAt = user.ExpiresAt,
    };
}

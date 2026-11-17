using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RunnersDiary.Domain.Localization;
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

    /// <summary>UI language tag (<c>en</c> or <c>pl</c>); null = never chosen (ADR 0042 §3). Set with <c>PUT /api/me/language</c>.</summary>
    public required string? Language { get; init; }

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

/// <summary><c>PUT /api/me/language</c> body: one of <c>en</c>, <c>pl</c>.</summary>
public sealed record UpdateMyLanguageRequest : IValidatableObject
{
    public required string Language { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!SupportedLanguages.IsSupported(Language))
        {
            yield return new ValidationResult($"Must be one of: {string.Join(", ", SupportedLanguages.All)}.", ["language"]);
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

        endpoints.MapPut("/me/language", UpdateLanguageAsync)
            .WithName("updateMyLanguage")
            .WithTags("Me")
            .WithSummary("Stores the caller's UI language (en or pl).")
            .Produces(StatusCodes.Status204NoContent)
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

    private static async Task<IResult> UpdateLanguageAsync(
        UpdateMyLanguageRequest request,
        [FromServices] ICurrentUser currentUser,
        [FromServices] RunnersDiaryDbContext db,
        CancellationToken cancellationToken)
    {
        // The id comes from the claims only; the query is also filtered by the user's own row (Users has no global filter).
        var userId = currentUser.UserId;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        user.SetLanguage(request.Language);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
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
        Language = user.Language,
        Today = UserToday.Get(clock, user.TimeZoneId),
        GuestExpiresAt = user.ExpiresAt,
    };
}

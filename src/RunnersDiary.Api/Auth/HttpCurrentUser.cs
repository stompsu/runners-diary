using RunnersDiary.Infrastructure.Identity;

namespace RunnersDiary.Api.Auth;

/// <summary>
/// The HTTP-backed <see cref="ICurrentUser"/>. Reads only the server-added <c>rd_uid</c>/<c>rd_kind</c> claims; without an
/// HttpContext (migrator, cleanup, seeder) there is no caller, which makes owner-filtered queries return nothing.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated => UserIdOrNull is not null;

    public Guid UserId => UserIdOrNull ?? throw new InvalidOperationException("There is no authenticated user.");

    public Guid? UserIdOrNull
    {
        get
        {
            var principal = accessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            return Guid.TryParse(principal.FindFirst(AppClaims.UserId)?.Value, out var id) && id != Guid.Empty ? id : null;
        }
    }

    public bool IsGuest =>
        UserIdOrNull is not null && accessor.HttpContext?.User.FindFirst(AppClaims.Kind)?.Value == AppClaims.KindGuest;
}

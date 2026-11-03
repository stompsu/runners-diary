namespace RunnersDiary.Api.Auth;

/// <summary>Claim types the API adds after token validation. Any claim of these types inside an incoming token is discarded.</summary>
public static class AppClaims
{
    /// <summary>The internal <c>Users.Id</c> of the caller.</summary>
    public const string UserId = "rd_uid";

    /// <summary>"guest" or "user"; derived from the database row, never from the token.</summary>
    public const string Kind = "rd_kind";

    public const string KindGuest = "guest";
    public const string KindUser = "user";
}

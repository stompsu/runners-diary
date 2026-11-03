namespace RunnersDiary.Api.Http;

internal static partial class StartupLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Entra sign-in is disabled ({Reason}); only guest sessions can authenticate.")]
    public static partial void EntraDisabled(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auth:Guest:SigningKey is not set; using an ephemeral key. Guest tokens stop working when the API restarts.")]
    public static partial void EphemeralGuestKey(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auth:Entra is enabled but Auth:Entra:SpaClientId is not set; /api/client-config returns no Entra section and the SPA hides Sign in.")]
    public static partial void EntraSpaClientIdMissing(ILogger logger);
}

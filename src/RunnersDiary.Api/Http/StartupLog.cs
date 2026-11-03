namespace RunnersDiary.Api.Http;

internal static partial class StartupLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Entra sign-in is disabled ({Reason}); only guest sessions can authenticate.")]
    public static partial void EntraDisabled(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auth:Guest:SigningKey is not set; using an ephemeral key. Guest tokens stop working when the API restarts.")]
    public static partial void EphemeralGuestKey(ILogger logger);
}

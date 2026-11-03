using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Diagnostics;
using RunnersDiary.Domain.Common;

namespace RunnersDiary.Api.Http;

/// <summary>
/// Turns client errors that surface as exceptions into RFC 9457 validation problems: malformed or unknown-member JSON and failed
/// parameter binding (<see cref="BadHttpRequestException"/>) and domain-rule violations (a safety net behind request validation).
/// Everything else falls through to the default handler (500 without details).
/// </summary>
public sealed partial class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        IResult? result = exception switch
        {
            DomainRuleException domain => Results.ValidationProblem(
                domain.Errors.GroupBy(e => e.Path).ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray())),
            BadHttpRequestException { StatusCode: StatusCodes.Status400BadRequest } bad => Results.ValidationProblem(
                new Dictionary<string, string[]> { [KeyFor(bad)] = [MessageFor(bad)] }),
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } => RequestBodyLimit.TooLarge(),
            _ => null,
        };

        if (result is null)
        {
            return false;
        }

        await result.ExecuteAsync(httpContext);
        return true;
    }

    private static string KeyFor(BadHttpRequestException exception)
    {
        if (exception.InnerException is JsonException { Path: { Length: > 0 } path })
        {
            var trimmed = path.TrimStart('$', '.');
            return trimmed.Length > 0 ? trimmed : "body";
        }

        if (exception.InnerException is JsonException)
        {
            return "body";
        }

        // Binding failures name the parameter in the framework text ('Required parameter "Guid id" was not provided from route.').
        // Only a plain identifier extracted from it is used as the key; the text itself is never echoed.
        return ParameterName().Match(exception.Message) is { Success: true } match ? match.Groups["name"].Value : "request";
    }

    // Framework and JSON error messages can name internal types and members; both are replaced by fixed texts.
    private static string MessageFor(BadHttpRequestException exception) =>
        exception.InnerException is JsonException
            ? "The request body is invalid, incomplete or contains an unknown or malformed member."
            : "The value could not be read from the request or is missing.";

    [GeneratedRegex(@"parameter ""(?:[^""\s]+\s+)*(?<name>[A-Za-z_][A-Za-z0-9_]{0,63})""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex ParameterName();
}

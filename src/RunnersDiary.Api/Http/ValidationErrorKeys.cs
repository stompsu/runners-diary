using Microsoft.AspNetCore.Http;

namespace RunnersDiary.Api.Http;

/// <summary>
/// Built-in validation reports DataAnnotations errors under C# member paths ("Rating.Stars"); the contract keys errors by camelCase
/// JSON paths ("rating.stars"), exactly like the domain rules do.
/// </summary>
internal static class ValidationErrorKeys
{
    public static void CamelCase(ProblemDetailsContext context)
    {
        if (context.ProblemDetails is not Microsoft.AspNetCore.Http.HttpValidationProblemDetails { Errors.Count: > 0 } validation)
        {
            return;
        }

        var rekeyed = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (key, messages) in validation.Errors)
        {
            var camel = ToCamelPath(key);
            rekeyed[camel] = rekeyed.TryGetValue(camel, out var existing) ? [.. existing, .. messages] : messages;
        }

        validation.Errors = rekeyed;
    }

    internal static string ToCamelPath(string path)
    {
        var chars = path.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (i == 0 || chars[i - 1] == '.')
            {
                chars[i] = char.ToLowerInvariant(chars[i]);
            }
        }

        return new string(chars);
    }
}

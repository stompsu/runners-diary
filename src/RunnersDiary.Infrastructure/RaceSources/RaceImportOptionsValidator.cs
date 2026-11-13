using Microsoft.Extensions.Options;

namespace RunnersDiary.Infrastructure.RaceSources;

/// <summary>Fails the job host at startup (exit code 1) when a <c>RaceImport</c> setting is out of range, for example <c>MinInterval</c> below one second.</summary>
internal sealed class RaceImportOptionsValidator : IValidateOptions<RaceImportOptions>
{
    public ValidateOptionsResult Validate(string? name, RaceImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var problems = options.Validate();
        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}

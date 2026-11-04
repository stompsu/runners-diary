namespace RunnersDiary.Ai.Tests.Support;

/// <summary>Locates repository files from the test binaries (the tests read the committed sources, not only the copied content).</summary>
internal static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string Prompts => Path.Combine(Root, "prompts", "training-insights");

    /// <summary>The copy next to the binaries (Infrastructure content items), as a host would read it.</summary>
    public static string PromptsInOutput => Path.Combine(AppContext.BaseDirectory, "prompts", "training-insights");

    public static string DemoSeed => Path.Combine(Root, "seed", "demo");

    public static string TestsDirectory => Path.Combine(Root, "tests", "RunnersDiary.Ai.Tests");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RunnersDiary.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("RunnersDiary.slnx was not found above " + AppContext.BaseDirectory);
    }
}

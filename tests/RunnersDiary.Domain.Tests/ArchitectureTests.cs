using System.Reflection;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Domain_references_only_framework_assemblies()
    {
        var referenced = typeof(Run).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        Assert.NotEmpty(referenced);
        Assert.All(referenced, name =>
            Assert.True(
                name.StartsWith("System", StringComparison.Ordinal) || name is "netstandard" or "mscorlib",
                $"Domain must not reference '{name}'."));
    }
}

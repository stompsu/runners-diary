using Microsoft.Extensions.Options;

namespace RunnersDiary.Ai.Tests.Support;

/// <summary>An <see cref="IOptionsMonitor{TOptions}"/> over one mutable instance: a test changes a setting and the next read sees it.</summary>
public sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue { get; set; } = value;

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

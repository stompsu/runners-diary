using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace RunnersDiary.Infrastructure.Persistence;

/// <summary>SQL Server returns <see cref="DateTimeKind.Unspecified"/>; every stored instant is UTC, so mark it as such on read.</summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}

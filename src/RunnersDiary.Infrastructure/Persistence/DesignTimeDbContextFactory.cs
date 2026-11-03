using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using RunnersDiary.Infrastructure.Identity;

namespace RunnersDiary.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c>; builds the model without Aspire or a database (the connection string is a placeholder).</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RunnersDiaryDbContext>
{
    public RunnersDiaryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RunnersDiaryDbContext>()
            .UseSqlServer("Server=localhost;Database=RunnersDiaryDesignTime;Integrated Security=true", sql => sql.UseNetTopologySuite())
            .Options;
        return new RunnersDiaryDbContext(options, AnonymousCurrentUser.Instance);
    }
}

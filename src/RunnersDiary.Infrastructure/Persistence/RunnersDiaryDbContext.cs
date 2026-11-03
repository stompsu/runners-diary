using Microsoft.EntityFrameworkCore;

namespace RunnersDiary.Infrastructure.Persistence;

/// <summary>Application database context. Entities and the first migration arrive in M2.</summary>
public sealed class RunnersDiaryDbContext(DbContextOptions<RunnersDiaryDbContext> options) : DbContext(options);

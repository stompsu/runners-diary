using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using RunnersDiary.Api.Jobs;
using RunnersDiary.Infrastructure.Guests;

namespace RunnersDiary.Api.Tests.Readiness;

/// <summary>ADR 0032 section 2: <c>--job guest-cleanup</c> is an Infrastructure-only host that exits 0 on success and 1 on failure.</summary>
public sealed class JobModeTests
{
    private static readonly string[] JobArgs =
    [
        "--job", "guest-cleanup",
        "--environment=Production",
        "--ConnectionStrings:runnersdiary=Server=127.0.0.1,1;Database=runnersdiary;User Id=test;Password=unused;Connect Timeout=2;TrustServerCertificate=true",
        "--ConnectionStrings:blobs=UseDevelopmentStorage=true",
        "--Aspire:Microsoft:EntityFrameworkCore:SqlServer:DisableRetry=true",
    ];

    private sealed class ScriptedPass(params int[] removedPerPass) : IGuestCleanupPass
    {
        public int Runs { get; private set; }

        public Task<int> RunOnceAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Runs < removedPerPass.Length ? removedPerPass[Runs++] : 0);
    }

    private sealed class ThrowingPass : IGuestCleanupPass
    {
        public Task<int> RunOnceAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("SQL is down");
    }

    [Theory]
    [InlineData("--job", "guest-cleanup", true, "guest-cleanup")]
    [InlineData("--job=guest-cleanup", "", true, "guest-cleanup")]
    [InlineData("--job", "", true, "")]
    [InlineData("--urls", "http://localhost", false, "")]
    public void The_job_argument_is_recognised_in_both_forms(string first, string second, bool expectedJobMode, string expectedJob)
    {
        var args = string.IsNullOrEmpty(second) ? new[] { first } : [first, second];

        Assert.Equal(expectedJobMode, JobMode.TryGetJob(args, out var job));
        Assert.Equal(expectedJob, job);
    }

    [Theory]
    [InlineData("")]
    [InlineData("migrate")]
    [InlineData("Guest-Cleanup")]
    public async Task An_unknown_job_exits_with_1(string job)
    {
        Assert.Equal(1, await JobMode.RunAsync(job, ["--job", job]));
    }

    [Fact]
    public async Task A_successful_cleanup_exits_with_0_after_passes_until_one_removes_nothing()
    {
        var pass = new ScriptedPass(3, 1, 0, 5);
        bool? hasServer = null;
        bool? hasPeriodicService = null;

        var exitCode = await JobMode.RunAsync("guest-cleanup", JobArgs, builder =>
        {
            builder.Services.AddSingleton<IGuestCleanupPass>(pass);
            hasServer = builder.Services.Any(d => d.ServiceType == typeof(IServer));
            hasPeriodicService = builder.Services.Any(d => d.ImplementationType == typeof(GuestCleanupService));
        });

        Assert.Equal(0, exitCode);
        Assert.Equal(3, pass.Runs); // 3, 1, then 0 stops the loop; the trailing 5 is never reached
        Assert.False(hasServer, "Job mode must not register Kestrel.");
        Assert.False(hasPeriodicService, "Job mode must not run the periodic cleanup service next to the explicit pass.");
    }

    [Fact]
    public async Task A_failing_cleanup_exits_with_1()
    {
        var exitCode = await JobMode.RunAsync("guest-cleanup", JobArgs, builder => builder.Services.AddSingleton<IGuestCleanupPass>(new ThrowingPass()));

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task The_real_wiring_resolves_and_fails_with_1_when_sql_is_unreachable()
    {
        // No fake pass: proves the job host can construct the real GuestCleanup (DbContext, blob store, options) and surfaces a SQL failure.
        Assert.Equal(1, await JobMode.RunAsync("guest-cleanup", JobArgs));
    }

    [Fact]
    public async Task Missing_connection_strings_in_production_exit_with_1()
    {
        string[] args = ["--job", "guest-cleanup", "--environment=Production"];

        Assert.Equal(1, await JobMode.RunAsync("guest-cleanup", args));
    }

    [Fact]
    public async Task The_pass_loop_is_bounded()
    {
        var always = new AlwaysRemovesPass();

        await JobMode.RunGuestCleanupAsync(always, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Equal(JobMode.MaxPasses, always.Runs);
    }

    private sealed class AlwaysRemovesPass : IGuestCleanupPass
    {
        public int Runs { get; private set; }

        public Task<int> RunOnceAsync(CancellationToken cancellationToken)
        {
            Runs++;
            return Task.FromResult(1);
        }
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RunnersDiary.Infrastructure.Ai;

/// <summary>
/// Registers the Training Insights services (M6). The host (step 3) must also register the EF implementations of
/// <see cref="IAiGenerationStore"/> and <see cref="IAiSummaryStore"/> (scoped) and call this from <c>AddInfrastructure</c>.
/// </summary>
public static class AiServiceCollectionExtensions
{
    public static IHostApplicationBuilder AddTrainingInsights(this IHostApplicationBuilder builder, bool runStartupChecks = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        services.AddOptions<AiOptions>().Bind(builder.Configuration.GetSection(AiOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<AiOptions>, AiOptionsValidator>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton(sp => PromptCatalog.Load(sp.GetRequiredService<IOptions<AiOptions>>().Value.PromptsPath));
        services.AddSingleton(sp => TemplateCatalog.Load(sp.GetRequiredService<PromptCatalog>()));
        services.AddSingleton(sp => DemoSummaryCatalog.Load(sp.GetRequiredService<PromptCatalog>()));

        // Tests replace this registration with a scripted client wrapped by ChatClientFactory.Wrap.
        services.TryAddSingleton(sp => ChatClientFactory.Create(sp.GetRequiredService<IOptions<AiOptions>>().Value, sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton<TrainingInsightsAgentProvider>();
        services.AddSingleton<GenerationSlots>();
        services.AddSingleton<GenerationInFlightGuard>();

        services.AddScoped<AiUsageLedger>();
        services.AddScoped<SummaryGenerator>();
        services.AddScoped<InsightsSummaryService>();

        if (runStartupChecks)
        {
            services.AddHostedService<AiStartupCheck>();
        }

        return builder;
    }
}

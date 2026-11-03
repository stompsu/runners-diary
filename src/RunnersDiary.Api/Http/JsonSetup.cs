using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;

namespace RunnersDiary.Api.Http;

public static class JsonSetup
{
    /// <summary>
    /// Strict request JSON: unknown members are rejected (no mass assignment, e.g. a smuggled <c>userId</c>), enums travel as
    /// their names only, <see cref="TimeOnly"/> is <c>HH:mm</c>.
    /// </summary>
    public static IServiceCollection AddApiJson(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
            options.SerializerOptions.Converters.Add(new HourMinuteTimeOnlyConverter());
        });
        return services;
    }
}

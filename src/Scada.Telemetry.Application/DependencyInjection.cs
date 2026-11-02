using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Scada.Telemetry.Application;

public static class DependencyInjection
{
    /// <summary>Регистрирует бизнес-логику. Параметры берутся из секции <see cref="TelemetryOptions.SectionName"/>.</summary>
    public static IServiceCollection AddTelemetryApplication(
        this IServiceCollection services,
        Action<TelemetryOptions> configure)
    {
        services.AddOptions<TelemetryOptions>()
            .Configure(configure)
            .Validate(o => o.IsValid(), "Invalid 'Telemetry' configuration section.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<TelemetryService>();

        return services;
    }
}

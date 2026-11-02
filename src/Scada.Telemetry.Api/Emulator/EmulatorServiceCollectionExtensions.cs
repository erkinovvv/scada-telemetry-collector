namespace Scada.Telemetry.Api.Emulator;

internal static class EmulatorServiceCollectionExtensions
{
    public static IServiceCollection AddTelemetryEmulator(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmulatorOptions>()
            .Bind(configuration.GetSection(EmulatorOptions.SectionName))
            .Validate(o => o.IsValid(), "Invalid 'Emulator' configuration section.")
            .ValidateOnStart();

        services.AddHostedService<TelemetryEmulatorWorker>();
        return services;
    }
}

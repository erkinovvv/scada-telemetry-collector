using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Scada.Telemetry.Application.Abstractions;
using Scada.Telemetry.Infrastructure.Health;
using Scada.Telemetry.Infrastructure.Persistence;

namespace Scada.Telemetry.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Telemetry";

    /// <summary>Регистрирует доступ к PostgreSQL: пул соединений, репозиторий, инициализацию схемы, health check.</summary>
    public static IServiceCollection AddTelemetryInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        // NpgsqlDataSource — один на приложение: внутри живёт пул соединений.
        // Параметры пула (Maximum Pool Size, Max Auto Prepare и т.д.) задаются в строке подключения.
        services.AddSingleton(sp => new NpgsqlDataSourceBuilder(connectionString)
            .UseLoggerFactory(sp.GetRequiredService<ILoggerFactory>())
            .Build());

        // Репозиторий без состояния → Singleton: ноль аллокаций на резолв из DI на каждый запрос.
        services.AddSingleton<ITelemetryRepository, NpgsqlTelemetryRepository>();

        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddHostedService<SchemaInitializer>();

        services.AddHealthChecks().AddCheck<PostgresHealthCheck>("postgres");

        return services;
    }
}

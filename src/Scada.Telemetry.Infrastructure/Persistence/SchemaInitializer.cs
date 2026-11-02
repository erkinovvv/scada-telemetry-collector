using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Scada.Telemetry.Infrastructure.Persistence;

/// <summary>
/// При старте приложения дожидается PostgreSQL и применяет SQL-скрипт схемы (db/init/*.sql).
/// Это IHostedService со всей работой в StartAsync: хост не начнёт принимать HTTP-запросы
/// и не запустит эмулятор, пока таблица и индекс не готовы.
/// </summary>
internal sealed partial class SchemaInitializer(
    NpgsqlDataSource dataSource,
    IOptions<DatabaseOptions> options,
    ILogger<SchemaInitializer> logger) : IHostedService
{
    private const string SchemaResourceName = "Schema.001_telemetry_records.sql";

    // Произвольный ключ advisory-lock: если поднять несколько экземпляров API,
    // схему применит только один, остальные подождут.
    private const long SchemaLockKey = 7_342_001;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.ApplySchemaOnStartup)
        {
            return;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await ApplySchemaAsync(dataSource, cancellationToken);
                LogSchemaApplied(logger);
                return;
            }
            catch (NpgsqlException ex) when (attempt < settings.StartupConnectAttempts && ex.IsTransient)
            {
                LogDatabaseUnavailable(logger, attempt, settings.StartupConnectAttempts, ex.Message);
                await Task.Delay(settings.StartupConnectDelay, cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Применяет схему в одной транзакции под advisory-lock. Используется также в интеграционных тестах.</summary>
    internal static async Task ApplySchemaAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        var script = await ReadSchemaScriptAsync(cancellationToken);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = new NpgsqlCommand($"SELECT pg_advisory_xact_lock({SchemaLockKey})", connection, transaction))
        {
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var schemaCommand = new NpgsqlCommand(script, connection, transaction))
        {
            await schemaCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<string> ReadSchemaScriptAsync(CancellationToken cancellationToken)
    {
        await using var stream = typeof(SchemaInitializer).Assembly.GetManifestResourceStream(SchemaResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{SchemaResourceName}' not found.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Database schema is up to date")]
    private static partial void LogSchemaApplied(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database is not available yet (attempt {Attempt}/{MaxAttempts}): {Reason}")]
    private static partial void LogDatabaseUnavailable(ILogger logger, int attempt, int maxAttempts, string reason);
}

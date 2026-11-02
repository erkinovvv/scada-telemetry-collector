namespace Scada.Telemetry.Infrastructure.Persistence;

/// <summary>Параметры БД (секция <c>Database</c> в appsettings.json).</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Применять идемпотентный SQL-скрипт схемы при старте API.</summary>
    public bool ApplySchemaOnStartup { get; set; } = true;

    /// <summary>Сколько раз пытаться подключиться к БД при старте (Postgres может подниматься дольше API).</summary>
    public int StartupConnectAttempts { get; set; } = 30;

    public TimeSpan StartupConnectDelay { get; set; } = TimeSpan.FromSeconds(2);
}

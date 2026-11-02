namespace Scada.Telemetry.Application;

/// <summary>Ограничения API телеметрии (секция <c>Telemetry</c> в appsettings.json).</summary>
public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    /// <summary>Максимум замеров в одном POST /api/telemetry/batch.</summary>
    public int MaxBatchSize { get; set; } = 1000;

    /// <summary>Лимит истории по умолчанию.</summary>
    public int DefaultHistoryLimit { get; set; } = 100;

    /// <summary>Верхняя граница limit: защищает БД и сеть от запроса «отдай всё».</summary>
    public int MaxHistoryLimit { get; set; } = 10_000;

    /// <summary>Окно истории, если параметр from не передан.</summary>
    public TimeSpan DefaultHistoryWindow { get; set; } = TimeSpan.FromHours(1);

    internal bool IsValid() =>
        MaxBatchSize > 0
        && DefaultHistoryLimit > 0
        && MaxHistoryLimit >= DefaultHistoryLimit
        && DefaultHistoryWindow > TimeSpan.Zero;
}

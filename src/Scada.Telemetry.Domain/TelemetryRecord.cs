namespace Scada.Telemetry.Domain;

/// <summary>Флаг достоверности замера (как в SCADA).</summary>
public enum Quality
{
    Bad = 0,
    Good = 1,
}

/// <summary>
/// Один замер датчика (тега). Неизменяемая структура: массив из 1000 замеров —
/// это один непрерывный блок памяти без 1000 отдельных объектов в куче.
/// </summary>
/// <param name="TagId">Идентификатор тега в SCADA.</param>
/// <param name="Timestamp">Время замера, всегда в UTC (offset = 0).</param>
/// <param name="Value">Физическое значение.</param>
/// <param name="Quality">Достоверность.</param>
public readonly record struct TelemetryRecord(int TagId, DateTimeOffset Timestamp, double Value, Quality Quality);

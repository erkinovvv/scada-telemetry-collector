namespace Scada.Telemetry.Application.Contracts;

/// <summary>
/// Замер во входящем пакете. Поля nullable, чтобы отличить «не передано» от нуля
/// и вернуть клиенту понятную ошибку валидации.
/// Структура, а не класс: массив из 1000 элементов десериализуется без 1000 аллокаций.
/// </summary>
public readonly record struct TelemetryRecordRequest(int? TagId, DateTimeOffset? Timestamp, double? Value, int? Quality);

/// <summary>Ответ на приём пачки.</summary>
public sealed record IngestResponse(long Inserted);

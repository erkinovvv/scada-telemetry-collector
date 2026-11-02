using Scada.Telemetry.Application.Contracts;

namespace Scada.Telemetry.Application.Validation;

/// <summary>
/// Валидация входящей пачки. Написана вручную, без рефлексии и сторонних библиотек:
/// на корректной пачке (основной сценарий) не делает ни одной аллокации.
/// </summary>
public static class TelemetryBatchValidator
{
    /// <summary>Больше ошибок клиенту не отдаём: на пачке из 1000 «плохих» строк ответ не должен раздуваться.</summary>
    public const int MaxReportedErrors = 20;

    /// <returns><c>null</c>, если пачка корректна; иначе словарь «поле → ошибки».</returns>
    public static Dictionary<string, string[]>? Validate(TelemetryRecordRequest[]? batch, int maxBatchSize)
    {
        if (batch is null || batch.Length == 0)
        {
            return Single("batch", "Batch must contain at least one record.");
        }

        if (batch.Length > maxBatchSize)
        {
            return Single("batch", $"Batch must contain at most {maxBatchSize} records, got {batch.Length}.");
        }

        Dictionary<string, string[]>? errors = null;

        for (var i = 0; i < batch.Length; i++)
        {
            ref readonly var record = ref batch[i];

            if (record.TagId is not > 0)
            {
                Add(ref errors, i, "tagId", "tagId is required and must be a positive integer.");
            }

            if (record.Timestamp is null)
            {
                Add(ref errors, i, "timestamp", "timestamp is required (ISO 8601, e.g. 2026-11-02T10:00:00Z).");
            }

            if (record.Value is not { } value || !double.IsFinite(value))
            {
                Add(ref errors, i, "value", "value is required and must be a finite number.");
            }

            if (record.Quality is not (0 or 1))
            {
                Add(ref errors, i, "quality", "quality is required and must be 0 (Bad) or 1 (Good).");
            }

            if (errors?.Count >= MaxReportedErrors)
            {
                break;
            }
        }

        return errors;
    }

    private static void Add(ref Dictionary<string, string[]>? errors, int index, string field, string message)
    {
        if (errors?.Count >= MaxReportedErrors)
        {
            return;
        }

        errors ??= [];
        errors.TryAdd($"[{index}].{field}", [message]);
    }

    private static Dictionary<string, string[]> Single(string key, string message) => new(1) { [key] = [message] };
}

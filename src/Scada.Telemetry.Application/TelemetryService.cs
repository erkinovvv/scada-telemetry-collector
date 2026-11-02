using Microsoft.Extensions.Options;
using Scada.Telemetry.Application.Abstractions;
using Scada.Telemetry.Application.Contracts;
using Scada.Telemetry.Application.Validation;
using Scada.Telemetry.Domain;

namespace Scada.Telemetry.Application;

/// <summary>
/// Бизнес-логика телеметрии: валидация, нормализация времени в UTC, значения по умолчанию.
/// Не хранит состояния, поэтому зарегистрирован как Singleton и безопасен для параллельных вызовов.
/// </summary>
public sealed class TelemetryService(
    ITelemetryRepository repository,
    IOptions<TelemetryOptions> options,
    TimeProvider timeProvider)
{
    private readonly TelemetryOptions _options = options.Value;

    /// <summary>Принимает пачку замеров (из API или из эмулятора) и сохраняет её целиком.</summary>
    public async Task<ServiceResult<long>> IngestAsync(
        TelemetryRecordRequest[]? batch,
        CancellationToken cancellationToken)
    {
        var errors = TelemetryBatchValidator.Validate(batch, _options.MaxBatchSize);
        if (errors is not null)
        {
            return ServiceResult<long>.Invalid(errors);
        }

        var records = new TelemetryRecord[batch!.Length];
        for (var i = 0; i < batch.Length; i++)
        {
            ref readonly var r = ref batch[i];

            // Все времена храним в UTC: PostgreSQL timestamptz и Npgsql ожидают offset = 0.
            records[i] = new TelemetryRecord(
                r.TagId!.Value,
                r.Timestamp!.Value.ToUniversalTime(),
                r.Value!.Value,
                (Quality)r.Quality!.Value);
        }

        var inserted = await repository.InsertBatchAsync(records, cancellationToken);
        return ServiceResult<long>.Success(inserted);
    }

    /// <summary>Последний замер тега. <paramref name="goodOnly"/> — пропускать замеры с quality = Bad.</summary>
    public async Task<ServiceResult<TelemetryRecord?>> GetLatestAsync(
        int tagId,
        bool goodOnly,
        CancellationToken cancellationToken)
    {
        if (tagId <= 0)
        {
            return ServiceResult<TelemetryRecord?>.Invalid("tagId", "tagId must be a positive integer.");
        }

        var latest = await repository.GetLatestAsync(tagId, goodOnly, cancellationToken);
        return ServiceResult<TelemetryRecord?>.Success(latest);
    }

    /// <summary>
    /// История тега за [from, to). Если to не задан — «сейчас», если from не задан — to минус окно по умолчанию.
    /// </summary>
    public async Task<ServiceResult<IReadOnlyList<TelemetryRecord>>> GetHistoryAsync(
        int tagId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (tagId <= 0)
        {
            return ServiceResult<IReadOnlyList<TelemetryRecord>>.Invalid("tagId", "tagId must be a positive integer.");
        }

        var effectiveLimit = limit ?? _options.DefaultHistoryLimit;
        if (effectiveLimit < 1 || effectiveLimit > _options.MaxHistoryLimit)
        {
            return ServiceResult<IReadOnlyList<TelemetryRecord>>.Invalid(
                "limit", $"limit must be between 1 and {_options.MaxHistoryLimit}.");
        }

        var effectiveTo = (to ?? timeProvider.GetUtcNow()).ToUniversalTime();
        var effectiveFrom = (from ?? effectiveTo - _options.DefaultHistoryWindow).ToUniversalTime();
        if (effectiveFrom >= effectiveTo)
        {
            return ServiceResult<IReadOnlyList<TelemetryRecord>>.Invalid("from", "from must be earlier than to.");
        }

        var history = await repository.GetHistoryAsync(
            tagId, effectiveFrom, effectiveTo, effectiveLimit, cancellationToken);

        return ServiceResult<IReadOnlyList<TelemetryRecord>>.Success(history);
    }
}

using Scada.Telemetry.Domain;

namespace Scada.Telemetry.Application.Abstractions;

/// <summary>Доступ к хранилищу телеметрии. Реализация — в слое Infrastructure.</summary>
public interface ITelemetryRepository
{
    /// <summary>Сохраняет пачку замеров одной атомарной операцией. Возвращает число записанных строк.</summary>
    Task<long> InsertBatchAsync(IReadOnlyList<TelemetryRecord> records, CancellationToken cancellationToken);

    /// <summary>Последний замер тега или <c>null</c>, если замеров нет.</summary>
    Task<TelemetryRecord?> GetLatestAsync(int tagId, bool goodOnly, CancellationToken cancellationToken);

    /// <summary>Замеры тега в полуинтервале [from, to), от новых к старым, не больше <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<TelemetryRecord>> GetHistoryAsync(
        int tagId,
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken cancellationToken);
}

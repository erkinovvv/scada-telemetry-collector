using System.Data;
using Npgsql;
using NpgsqlTypes;
using Scada.Telemetry.Application.Abstractions;
using Scada.Telemetry.Domain;

namespace Scada.Telemetry.Infrastructure.Persistence;

/// <summary>
/// Доступ к telemetry_records на «голом» Npgsql — самый быстрый путь в .NET:
/// запись через бинарный COPY, чтение через DataReader с позиционными параметрами ($1, $2…).
/// </summary>
internal sealed class NpgsqlTelemetryRepository(NpgsqlDataSource dataSource) : ITelemetryRepository
{
    // Бинарный COPY: Postgres принимает поток строк одной командой, без разбора SQL на каждую строку.
    // Это в разы быстрее, чем INSERT ... VALUES (...), (...), а одиночные INSERT в цикле — на порядки.
    private const string CopySql =
        """COPY telemetry_records (tag_id, "timestamp", value, quality) FROM STDIN (FORMAT BINARY)""";

    // Покрывающий индекс допускает Index Only Scan; обращения к heap зависят от visibility map.
    private const string LatestSql =
        """
        SELECT "timestamp", value, quality
        FROM telemetry_records
        WHERE tag_id = $1
        ORDER BY "timestamp" DESC
        LIMIT 1
        """;

    private const string LatestGoodSql =
        """
        SELECT "timestamp", value, quality
        FROM telemetry_records
        WHERE tag_id = $1 AND quality = 1
        ORDER BY "timestamp" DESC
        LIMIT 1
        """;

    // Полуинтервал [from, to): соседние окна не пересекаются и не теряют точки на границе.
    private const string HistorySql =
        """
        SELECT "timestamp", value, quality
        FROM telemetry_records
        WHERE tag_id = $1 AND "timestamp" >= $2 AND "timestamp" < $3
        ORDER BY "timestamp" DESC
        LIMIT $4
        """;

    public async Task<long> InsertBatchAsync(IReadOnlyList<TelemetryRecord> records, CancellationToken cancellationToken)
    {
        if (records.Count == 0)
        {
            return 0;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        // COPY — одна SQL-команда, поэтому атомарна: либо записываются все строки, либо ни одной.
        // Если до CompleteAsync случится ошибка или отмена, DisposeAsync отменит COPY целиком.
        await using var importer = await connection.BeginBinaryImportAsync(CopySql, cancellationToken);

        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];

            await importer.StartRowAsync(cancellationToken);
            await importer.WriteAsync(record.TagId, NpgsqlDbType.Integer, cancellationToken);
            await importer.WriteAsync(record.Timestamp, NpgsqlDbType.TimestampTz, cancellationToken);
            await importer.WriteAsync(record.Value, NpgsqlDbType.Double, cancellationToken);
            await importer.WriteAsync((int)record.Quality, NpgsqlDbType.Integer, cancellationToken);
        }

        return (long)await importer.CompleteAsync(cancellationToken);
    }

    public async Task<TelemetryRecord?> GetLatestAsync(int tagId, bool goodOnly, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(goodOnly ? LatestGoodSql : LatestSql);
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = tagId });

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader, tagId) : null;
    }

    public async Task<IReadOnlyList<TelemetryRecord>> GetHistoryAsync(
        int tagId,
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(HistorySql);
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = tagId });
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = from, NpgsqlDbType = NpgsqlDbType.TimestampTz });
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = to, NpgsqlDbType = NpgsqlDbType.TimestampTz });
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = limit });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        // Ёмкость заранее: список не перевыделяет внутренний массив по мере роста.
        var result = new List<TelemetryRecord>(Math.Min(limit, 1024));
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadRecord(reader, tagId));
        }

        return result;
    }

    // Чтение по номеру колонки и с явным типом — без рефлексии и упаковки (boxing).
    private static TelemetryRecord ReadRecord(NpgsqlDataReader reader, int tagId) => new(
        tagId,
        reader.GetFieldValue<DateTimeOffset>(0),
        reader.GetDouble(1),
        (Quality)reader.GetInt32(2));
}

using Npgsql;
using Scada.Telemetry.Domain;
using Scada.Telemetry.Infrastructure.Persistence;

namespace Scada.Telemetry.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class TelemetryRepositoryTests(PostgresFixture db)
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    private readonly NpgsqlTelemetryRepository _repository = new(db.DataSource);

    [Fact]
    public async Task Batch_of_1000_records_is_stored_in_one_operation()
    {
        var tagId = PostgresFixture.NewTagId();
        var batch = Enumerable.Range(0, 1000)
            .Select(i => new TelemetryRecord(tagId, T0.AddSeconds(i), i * 0.5, Quality.Good))
            .ToArray();

        var inserted = await _repository.InsertBatchAsync(batch, CancellationToken.None);

        Assert.Equal(1000, inserted);
        Assert.Equal(1000, await CountAsync(tagId));
    }

    [Fact]
    public async Task Batch_is_atomic_when_one_row_violates_constraint()
    {
        var tagId = PostgresFixture.NewTagId();
        TelemetryRecord[] batch =
        [
            new(tagId, T0, 1, Quality.Good),
            new(tagId, T0.AddSeconds(1), 2, (Quality)7), // нарушает CHECK (quality IN (0, 1))
            new(tagId, T0.AddSeconds(2), 3, Quality.Good),
        ];

        await Assert.ThrowsAsync<PostgresException>(() => _repository.InsertBatchAsync(batch, CancellationToken.None));

        Assert.Equal(0, await CountAsync(tagId));
    }

    [Fact]
    public async Task Latest_returns_newest_record_and_can_skip_bad_quality()
    {
        var tagId = PostgresFixture.NewTagId();
        await _repository.InsertBatchAsync(
        [
            new(tagId, T0, 10, Quality.Good),
            new(tagId, T0.AddSeconds(2), 30, Quality.Bad),
            new(tagId, T0.AddSeconds(1), 20, Quality.Good),
        ], CancellationToken.None);

        var latest = await _repository.GetLatestAsync(tagId, goodOnly: false, CancellationToken.None);
        var latestGood = await _repository.GetLatestAsync(tagId, goodOnly: true, CancellationToken.None);

        Assert.Equal(new TelemetryRecord(tagId, T0.AddSeconds(2), 30, Quality.Bad), latest);
        Assert.Equal(new TelemetryRecord(tagId, T0.AddSeconds(1), 20, Quality.Good), latestGood);
    }

    [Fact]
    public async Task Latest_is_null_for_unknown_tag()
    {
        Assert.Null(await _repository.GetLatestAsync(PostgresFixture.NewTagId(), false, CancellationToken.None));
    }

    [Fact]
    public async Task History_is_half_open_range_newest_first_with_limit()
    {
        var tagId = PostgresFixture.NewTagId();
        var batch = Enumerable.Range(0, 10)
            .Select(i => new TelemetryRecord(tagId, T0.AddMinutes(i), i, Quality.Good))
            .ToArray();
        await _repository.InsertBatchAsync(batch, CancellationToken.None);

        // [T0+2, T0+8) → минуты 2..7, от новых к старым, первые 4 → 7, 6, 5, 4
        var history = await _repository.GetHistoryAsync(
            tagId, T0.AddMinutes(2), T0.AddMinutes(8), limit: 4, CancellationToken.None);

        Assert.Equal([7d, 6d, 5d, 4d], history.Select(r => r.Value));
        Assert.All(history, r => Assert.Equal(TimeSpan.Zero, r.Timestamp.Offset));
    }

    [Fact]
    public async Task History_query_uses_covering_index()
    {
        await using var connection = await db.DataSource.OpenConnectionAsync();

        // В тестовой таблице мало строк, и планировщик может предпочесть seq scan.
        // Запрещаем его в этой сессии, чтобы проверить, что индекс вообще подходит под запрос.
        await using (var setup = new NpgsqlCommand("SET enable_seqscan = off", connection))
        {
            await setup.ExecuteNonQueryAsync();
        }

        await using var explain = new NpgsqlCommand(
            """
            EXPLAIN SELECT "timestamp", value, quality FROM telemetry_records
            WHERE tag_id = 1 AND "timestamp" >= now() - interval '1 hour' AND "timestamp" < now()
            ORDER BY "timestamp" DESC LIMIT 100
            """,
            connection);

        var plan = string.Join('\n', await ReadAllAsync(explain));

        Assert.Contains("Index Only Scan using ix_telemetry_records_tag_id_timestamp", plan, StringComparison.Ordinal);
    }

    private async Task<long> CountAsync(int tagId)
    {
        await using var command = db.DataSource.CreateCommand("SELECT count(*) FROM telemetry_records WHERE tag_id = $1");
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = tagId });
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<List<string>> ReadAllAsync(NpgsqlCommand command)
    {
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        return lines;
    }
}

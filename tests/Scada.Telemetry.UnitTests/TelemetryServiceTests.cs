using Microsoft.Extensions.Options;
using Scada.Telemetry.Application;
using Scada.Telemetry.Application.Abstractions;
using Scada.Telemetry.Application.Contracts;
using Scada.Telemetry.Domain;

namespace Scada.Telemetry.UnitTests;

public sealed class TelemetryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    private readonly RecordingRepository _repository = new();
    private readonly TelemetryService _service;

    public TelemetryServiceTests()
    {
        _service = new TelemetryService(_repository, Options.Create(new TelemetryOptions()), new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task Ingest_normalizes_timestamps_to_utc_and_maps_quality()
    {
        var local = new DateTimeOffset(2026, 11, 2, 15, 0, 0, TimeSpan.FromHours(5));

        var result = await _service.IngestAsync([new TelemetryRecordRequest(7, local, 12.5, 0)], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);
        var saved = Assert.Single(_repository.Inserted!);
        Assert.Equal(TimeSpan.Zero, saved.Timestamp.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero), saved.Timestamp);
        Assert.Equal(Quality.Bad, saved.Quality);
    }

    [Fact]
    public async Task Invalid_batch_never_reaches_the_database()
    {
        var result = await _service.IngestAsync([new TelemetryRecordRequest(1, Now, 1, 5)], CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(_repository.Inserted);
    }

    [Fact]
    public async Task History_uses_default_window_and_limit()
    {
        var result = await _service.GetHistoryAsync(1, from: null, to: null, limit: null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((Now - TimeSpan.FromHours(1), Now, 100), _repository.LastHistoryQuery);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10_001)]
    public async Task History_rejects_limit_out_of_range(int limit)
    {
        var result = await _service.GetHistoryAsync(1, null, null, limit, CancellationToken.None);

        Assert.Contains("limit", result.Errors!.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(59)]
    public async Task History_rejects_to_when_default_window_would_underflow(int minutes)
    {
        var to = DateTimeOffset.MinValue.AddMinutes(minutes).ToOffset(TimeSpan.FromHours(5));

        var result = await _service.GetHistoryAsync(1, null, to, 10, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("to", result.Errors!.Keys);
        Assert.Null(_repository.LastHistoryQuery);
    }

    [Fact]
    public async Task History_allows_default_window_starting_at_minimum_timestamp()
    {
        var to = DateTimeOffset.MinValue.AddHours(1);

        var result = await _service.GetHistoryAsync(1, null, to, 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((DateTimeOffset.MinValue, to, 10), _repository.LastHistoryQuery);
    }

    [Fact]
    public async Task History_allows_early_to_when_from_is_explicit()
    {
        var from = DateTimeOffset.MinValue.AddMinutes(5);
        var to = DateTimeOffset.MinValue.AddMinutes(30);

        var result = await _service.GetHistoryAsync(1, from, to, 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((from, to, 10), _repository.LastHistoryQuery);
    }

    [Fact]
    public async Task History_rejects_inverted_range()
    {
        var result = await _service.GetHistoryAsync(1, Now, Now.AddMinutes(-1), 10, CancellationToken.None);

        Assert.Contains("from", result.Errors!.Keys);
    }

    [Fact]
    public async Task Latest_rejects_non_positive_tag()
    {
        var result = await _service.GetLatestAsync(0, goodOnly: false, CancellationToken.None);

        Assert.Contains("tagId", result.Errors!.Keys);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingRepository : ITelemetryRepository
    {
        public IReadOnlyList<TelemetryRecord>? Inserted { get; private set; }

        public (DateTimeOffset From, DateTimeOffset To, int Limit)? LastHistoryQuery { get; private set; }

        public Task<long> InsertBatchAsync(IReadOnlyList<TelemetryRecord> records, CancellationToken cancellationToken)
        {
            Inserted = records;
            return Task.FromResult((long)records.Count);
        }

        public Task<TelemetryRecord?> GetLatestAsync(int tagId, bool goodOnly, CancellationToken cancellationToken) =>
            Task.FromResult<TelemetryRecord?>(null);

        public Task<IReadOnlyList<TelemetryRecord>> GetHistoryAsync(
            int tagId, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken)
        {
            LastHistoryQuery = (from, to, limit);
            return Task.FromResult<IReadOnlyList<TelemetryRecord>>([]);
        }
    }
}

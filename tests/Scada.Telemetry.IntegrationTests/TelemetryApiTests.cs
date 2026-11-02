using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Scada.Telemetry.IntegrationTests;

/// <summary>Сквозные тесты HTTP API: настоящий хост ASP.NET Core + настоящий PostgreSQL.</summary>
[Collection(PostgresCollection.Name)]
public sealed class TelemetryApiTests(PostgresFixture db) : IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Telemetry", db.ConnectionString);
            builder.UseSetting("Emulator:Enabled", "false");
        });
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Posted_batch_is_returned_by_latest_and_history()
    {
        var tagId = PostgresFixture.NewTagId();
        var batch = Enumerable.Range(0, 5)
            .Select(i => new { tagId, timestamp = T0.AddSeconds(i), value = 100.0 + i, quality = 1 })
            .ToArray();

        var post = await _client.PostAsJsonAsync("/api/telemetry/batch", batch);
        post.EnsureSuccessStatusCode();
        Assert.Equal(5, (await post.Content.ReadFromJsonAsync<IngestDto>())!.Inserted);

        var latest = await _client.GetFromJsonAsync<RecordDto>($"/api/tags/{tagId}/latest");
        Assert.Equal(new RecordDto(tagId, T0.AddSeconds(4), 104, 1), latest);

        var from = Uri.EscapeDataString(T0.ToString("O"));
        var to = Uri.EscapeDataString(T0.AddSeconds(5).ToString("O"));
        var history = await _client.GetFromJsonAsync<RecordDto[]>($"/api/tags/{tagId}/history?from={from}&to={to}&limit=3");
        Assert.Equal([104d, 103d, 102d], history!.Select(r => r.Value));
    }

    [Fact]
    public async Task Batch_over_1000_records_is_rejected_with_validation_problem()
    {
        var batch = Enumerable.Range(0, 1001)
            .Select(_ => new { tagId = 1, timestamp = T0, value = 1.0, quality = 1 })
            .ToArray();

        var response = await _client.PostAsJsonAsync("/api/telemetry/batch", batch);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("batch", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Invalid_record_is_reported_by_index_and_nothing_is_saved()
    {
        var tagId = PostgresFixture.NewTagId();
        object[] batch =
        [
            new { tagId, timestamp = T0, value = 1.0, quality = 1 },
            new { tagId, timestamp = T0, value = 1.0, quality = 9 },
        ];

        var response = await _client.PostAsJsonAsync("/api/telemetry/batch", batch);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Equal(["[1].quality"], problem!.Errors.Keys);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/tags/{tagId}/latest")).StatusCode);
    }

    [Fact]
    public async Task Malformed_json_returns_bad_request()
    {
        using var content = new StringContent("[{\"tagId\": \"oops\"}]", System.Text.Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/telemetry/batch", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Latest_for_unknown_tag_is_not_found()
    {
        var response = await _client.GetAsync($"/api/tags/{PostgresFixture.NewTagId()}/latest");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task History_with_inverted_range_is_bad_request()
    {
        var response = await _client.GetAsync("/api/tags/1/history?from=2026-11-02T10:00:00Z&to=2026-11-02T09:00:00Z");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record IngestDto(long Inserted);

    private sealed record RecordDto(int TagId, DateTimeOffset Timestamp, double Value, int Quality);
}

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Scada.Telemetry.IntegrationTests;

/// <summary>Эмулятор пишет пачки в БД и корректно останавливается вместе с хостом.</summary>
[Collection(PostgresCollection.Name)]
public sealed class TelemetryEmulatorTests(PostgresFixture db)
{
    [Fact]
    public async Task Emulator_writes_batches_and_stops_gracefully()
    {
        // Эмулятор всегда пишет теги 1..TagCount, поэтому меряем прирост строк по тегу 1.
        var before = await CountTagAsync(1);

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Telemetry", db.ConnectionString);
            builder.UseSetting("Emulator:Enabled", "true");
            builder.UseSetting("Emulator:TagCount", "100");
            builder.UseSetting("Emulator:Interval", "00:00:00.200");
        });

        using (factory.CreateClient())
        {
            await Task.Delay(TimeSpan.FromSeconds(1.5));
        }

        // Dispose останавливает хост: BackgroundService получает отменённый токен и должен завершиться без ошибок.
        await factory.DisposeAsync();

        var after = await CountTagAsync(1);
        Assert.True(after - before >= 3, $"Expected at least 3 emulator ticks, got {after - before}.");

        var afterStop = await CountTagAsync(1);
        await Task.Delay(TimeSpan.FromMilliseconds(600));
        Assert.Equal(afterStop, await CountTagAsync(1));
    }

    private async Task<long> CountTagAsync(int tagId)
    {
        await using var command = db.DataSource.CreateCommand("SELECT count(*) FROM telemetry_records WHERE tag_id = $1");
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = tagId });
        return (long)(await command.ExecuteScalarAsync())!;
    }
}

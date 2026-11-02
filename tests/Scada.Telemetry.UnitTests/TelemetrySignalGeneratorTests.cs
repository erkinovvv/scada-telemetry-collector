using Scada.Telemetry.Api.Emulator;
using Scada.Telemetry.Application.Validation;

namespace Scada.Telemetry.UnitTests;

public sealed class TelemetrySignalGeneratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Produces_one_valid_record_per_tag()
    {
        var generator = new TelemetrySignalGenerator(tagCount: 100, badQualityRatio: 0.01, seed: 1);

        var batch = generator.Next(Now);

        Assert.Equal(Enumerable.Range(1, 100), batch.Select(r => r.TagId!.Value));
        Assert.All(batch, r => Assert.Equal(Now, r.Timestamp));
        Assert.Null(TelemetryBatchValidator.Validate(batch, maxBatchSize: 1000));
    }

    [Fact]
    public void Same_seed_gives_same_signal()
    {
        var a = new TelemetrySignalGenerator(10, 0.1, seed: 42);
        var b = new TelemetrySignalGenerator(10, 0.1, seed: 42);

        for (var tick = 0; tick < 50; tick++)
        {
            Assert.Equal(a.Next(Now).ToArray(), b.Next(Now).ToArray());
        }
    }

    [Fact]
    public void Values_stay_near_baseline_over_time()
    {
        var generator = new TelemetrySignalGenerator(5, 0, seed: 7);
        var first = generator.Next(Now).Select(r => r.Value!.Value).ToArray();

        double[] last = [];
        for (var tick = 0; tick < 10_000; tick++)
        {
            last = generator.Next(Now).Select(r => r.Value!.Value).ToArray();
        }

        // Возврат к среднему не даёт сигналу «уплыть»: отклонение остаётся в пределах нескольких процентов.
        for (var i = 0; i < first.Length; i++)
        {
            Assert.InRange(last[i], first[i] - Math.Max(5, first[i] * 0.1), first[i] + Math.Max(5, first[i] * 0.1));
        }
    }
}

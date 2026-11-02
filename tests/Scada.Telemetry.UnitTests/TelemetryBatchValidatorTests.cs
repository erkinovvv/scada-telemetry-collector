using Scada.Telemetry.Application.Contracts;
using Scada.Telemetry.Application.Validation;

namespace Scada.Telemetry.UnitTests;

public sealed class TelemetryBatchValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    private static TelemetryRecordRequest Valid(int tagId = 1) => new(tagId, Now, 42.5, 1);

    [Fact]
    public void Valid_batch_has_no_errors()
    {
        var batch = Enumerable.Range(1, 1000).Select(Valid).ToArray();

        Assert.Null(TelemetryBatchValidator.Validate(batch, maxBatchSize: 1000));
    }

    [Fact]
    public void Null_or_empty_batch_is_rejected()
    {
        Assert.Contains("batch", TelemetryBatchValidator.Validate(null, 1000)!.Keys);
        Assert.Contains("batch", TelemetryBatchValidator.Validate([], 1000)!.Keys);
    }

    [Fact]
    public void Batch_over_limit_is_rejected()
    {
        var batch = Enumerable.Range(1, 1001).Select(Valid).ToArray();

        var errors = TelemetryBatchValidator.Validate(batch, maxBatchSize: 1000);

        Assert.NotNull(errors);
        Assert.Contains("1001", errors["batch"][0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "[0].tagId")]
    [InlineData(0, "[0].tagId")]
    [InlineData(-5, "[0].tagId")]
    public void Invalid_tag_id_is_reported(int? tagId, string expectedKey)
    {
        var errors = TelemetryBatchValidator.Validate([new(tagId, Now, 1, 1)], 1000);

        Assert.Equal([expectedKey], errors!.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(2)]
    [InlineData(-1)]
    public void Quality_must_be_zero_or_one(int? quality)
    {
        var errors = TelemetryBatchValidator.Validate([new(1, Now, 1, quality)], 1000);

        Assert.Equal(["[0].quality"], errors!.Keys);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Value_must_be_finite(double value)
    {
        var errors = TelemetryBatchValidator.Validate([new(1, Now, value, 1)], 1000);

        Assert.Equal(["[0].value"], errors!.Keys);
    }

    [Fact]
    public void Missing_timestamp_and_value_are_reported_with_record_index()
    {
        TelemetryRecordRequest[] batch = [Valid(), new(2, null, null, 1)];

        var errors = TelemetryBatchValidator.Validate(batch, 1000);

        Assert.Equal(["[1].timestamp", "[1].value"], errors!.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Number_of_reported_errors_is_capped()
    {
        var batch = Enumerable.Range(0, 1000).Select(_ => new TelemetryRecordRequest(0, Now, 1, 1)).ToArray();

        var errors = TelemetryBatchValidator.Validate(batch, 1000);

        Assert.Equal(TelemetryBatchValidator.MaxReportedErrors, errors!.Count);
    }

    [Theory]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    public void Multiple_errors_in_the_last_record_do_not_exceed_the_cap(int precedingErrors)
    {
        var batch = Enumerable.Repeat(new TelemetryRecordRequest(0, Now, 1, 1), precedingErrors)
            .Append(new TelemetryRecordRequest(null, null, null, null))
            .ToArray();

        var errors = TelemetryBatchValidator.Validate(batch, 1000);

        Assert.Equal(TelemetryBatchValidator.MaxReportedErrors, errors!.Count);
        Assert.Contains($"[{precedingErrors}].tagId", errors.Keys);
    }
}

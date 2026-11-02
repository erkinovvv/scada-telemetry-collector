using Microsoft.Extensions.Options;
using Scada.Telemetry.Application;

namespace Scada.Telemetry.Api.Emulator;

/// <summary>
/// Фоновый эмулятор SCADA: раз в <see cref="EmulatorOptions.Interval"/> формирует пачку
/// из <see cref="EmulatorOptions.TagCount"/> замеров и сохраняет её через тот же сервис, что и API.
/// </summary>
internal sealed partial class TelemetryEmulatorWorker(
    TelemetryService telemetryService,
    IOptions<EmulatorOptions> options,
    TimeProvider timeProvider,
    ILogger<TelemetryEmulatorWorker> logger) : BackgroundService
{
    private readonly EmulatorOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            LogDisabled(logger);
            return;
        }

        var generator = new TelemetrySignalGenerator(_options.TagCount, _options.BadQualityRatio, _options.Seed);

        // PeriodicTimer не накладывает тики друг на друга: если сохранение заняло дольше периода,
        // следующая итерация начнётся сразу после него, а не параллельно — нагрузка на БД не копится.
        using var timer = new PeriodicTimer(_options.Interval, timeProvider);

        LogStarted(logger, _options.TagCount, _options.Interval);

        try
        {
            do
            {
                await PublishBatchAsync(generator, stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Штатная остановка сервиса (Ctrl+C, docker stop): токен отменён — просто выходим.
        }

        LogStopped(logger);
    }

    private async Task PublishBatchAsync(TelemetrySignalGenerator generator, CancellationToken stoppingToken)
    {
        try
        {
            var batch = generator.Next(timeProvider.GetUtcNow());
            var result = await telemetryService.IngestAsync(batch, stoppingToken);

            if (!result.IsSuccess)
            {
                LogBatchRejected(logger, string.Join("; ", result.Errors!.Keys));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            // Ошибка одной итерации (например, БД недоступна) не должна ронять весь хост:
            // с .NET 6 необработанное исключение в BackgroundService останавливает приложение.
            LogBatchFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Telemetry emulator is disabled")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Telemetry emulator started: {TagCount} tags every {Interval}")]
    private static partial void LogStarted(ILogger logger, int tagCount, TimeSpan interval);

    [LoggerMessage(Level = LogLevel.Information, Message = "Telemetry emulator stopped")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Emulator batch rejected by validation: {Errors}")]
    private static partial void LogBatchRejected(ILogger logger, string errors);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to save emulator batch, will retry on next tick")]
    private static partial void LogBatchFailed(ILogger logger, Exception exception);
}

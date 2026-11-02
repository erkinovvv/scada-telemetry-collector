using Scada.Telemetry.Application.Contracts;

namespace Scada.Telemetry.Api.Emulator;

/// <summary>
/// Генерирует правдоподобные сигналы: у каждого тега свой базовый уровень,
/// значение «гуляет» вокруг него (случайное блуждание с возвратом к среднему), иногда quality = Bad.
/// Буфер пачки переиспользуется между тиками — эмулятор не нагружает GC.
/// Не потокобезопасен: используется одним фоновым сервисом.
/// </summary>
internal sealed class TelemetrySignalGenerator
{
    private const double MeanReversion = 0.05;

    private readonly Random _random;
    private readonly double _badQualityRatio;
    private readonly double[] _baselines;
    private readonly double[] _noise;
    private readonly double[] _values;
    private readonly TelemetryRecordRequest[] _buffer;

    public TelemetrySignalGenerator(int tagCount, double badQualityRatio, int? seed = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tagCount);

        _random = seed is { } s ? new Random(s) : new Random();
        _badQualityRatio = badQualityRatio;
        _baselines = new double[tagCount];
        _noise = new double[tagCount];
        _values = new double[tagCount];
        _buffer = new TelemetryRecordRequest[tagCount];

        for (var i = 0; i < tagCount; i++)
        {
            // Разные «датчики»: уровни от 0 до 1000, амплитуда шума — ~1% от уровня.
            _baselines[i] = Math.Round(_random.NextDouble() * 1000, 1);
            _noise[i] = Math.Max(0.1, _baselines[i] * 0.01);
            _values[i] = _baselines[i];
        }
    }

    /// <summary>Формирует следующую пачку: по одному замеру на каждый тег (tagId = 1..TagCount).</summary>
    /// <remarks>Возвращает внутренний буфер: он валиден до следующего вызова <see cref="Next"/>.</remarks>
    public TelemetryRecordRequest[] Next(DateTimeOffset timestamp)
    {
        for (var i = 0; i < _buffer.Length; i++)
        {
            var step = (_random.NextDouble() * 2 - 1) * _noise[i];
            _values[i] += step + (_baselines[i] - _values[i]) * MeanReversion;

            var quality = _random.NextDouble() < _badQualityRatio ? 0 : 1;
            _buffer[i] = new TelemetryRecordRequest(i + 1, timestamp, Math.Round(_values[i], 3), quality);
        }

        return _buffer;
    }
}

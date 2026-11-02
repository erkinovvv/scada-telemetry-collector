namespace Scada.Telemetry.Api.Emulator;

/// <summary>Параметры эмулятора датчиков (секция <c>Emulator</c> в appsettings.json).</summary>
public sealed class EmulatorOptions
{
    public const string SectionName = "Emulator";

    public bool Enabled { get; set; } = true;

    /// <summary>Количество тегов: по ТЗ — 100, один замер на тег за тик.</summary>
    public int TagCount { get; set; } = 100;

    /// <summary>Период генерации пачки: по ТЗ — 1 секунда.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Доля замеров с quality = Bad (имитация сбоев датчиков).</summary>
    public double BadQualityRatio { get; set; } = 0.01;

    /// <summary>Seed генератора; null — случайный. Фиксированный seed даёт воспроизводимые данные.</summary>
    public int? Seed { get; set; }

    internal bool IsValid() =>
        TagCount > 0
        && Interval > TimeSpan.Zero
        && BadQualityRatio is >= 0 and <= 1;
}

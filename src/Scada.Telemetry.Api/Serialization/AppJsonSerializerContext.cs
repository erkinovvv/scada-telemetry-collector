using System.Text.Json;
using System.Text.Json.Serialization;
using Scada.Telemetry.Application.Contracts;
using Scada.Telemetry.Domain;

namespace Scada.Telemetry.Api.Serialization;

/// <summary>
/// Source-generated System.Text.Json: код сериализации генерируется при компиляции,
/// поэтому в рантайме нет рефлексии — быстрее старт, меньше CPU и аллокаций на каждый запрос.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(TelemetryRecordRequest[]))]
[JsonSerializable(typeof(IngestResponse))]
[JsonSerializable(typeof(TelemetryRecord))]
[JsonSerializable(typeof(IReadOnlyList<TelemetryRecord>))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext;

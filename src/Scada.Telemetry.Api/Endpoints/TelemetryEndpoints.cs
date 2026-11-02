using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Scada.Telemetry.Application;
using Scada.Telemetry.Application.Contracts;
using Scada.Telemetry.Domain;

namespace Scada.Telemetry.Api.Endpoints;

/// <summary>
/// HTTP-слой: только привязка параметров и преобразование результата сервиса в HTTP-ответ.
/// Вся логика — в <see cref="TelemetryService"/>.
/// </summary>
internal static class TelemetryEndpoints
{
    public static IEndpointRouteBuilder MapTelemetryEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/telemetry/batch", IngestBatchAsync)
            .WithName("IngestTelemetryBatch")
            .WithSummary("Пакетный приём замеров (до 1000 в одном запросе), сохраняются одной операцией.");

        api.MapGet("/tags/{tagId:int}/latest", GetLatestAsync)
            .WithName("GetLatestMeasurement")
            .WithSummary("Последний замер по тегу.");

        api.MapGet("/tags/{tagId:int}/history", GetHistoryAsync)
            .WithName("GetMeasurementHistory")
            .WithSummary("История замеров тега за [from, to), от новых к старым.");

        return app;
    }

    private static async Task<Results<Ok<IngestResponse>, ValidationProblem>> IngestBatchAsync(
        [FromBody] TelemetryRecordRequest[]? batch,
        TelemetryService service,
        CancellationToken cancellationToken)
    {
        var result = await service.IngestAsync(batch, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(new IngestResponse(result.Value))
            : TypedResults.ValidationProblem(result.Errors!);
    }

    private static async Task<Results<Ok<TelemetryRecord>, NotFound, ValidationProblem>> GetLatestAsync(
        int tagId,
        TelemetryService service,
        CancellationToken cancellationToken,
        bool goodOnly = false)
    {
        var result = await service.GetLatestAsync(tagId, goodOnly, cancellationToken);

        if (!result.IsSuccess)
        {
            return TypedResults.ValidationProblem(result.Errors!);
        }

        return result.Value is { } latest ? TypedResults.Ok(latest) : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<IReadOnlyList<TelemetryRecord>>, ValidationProblem>> GetHistoryAsync(
        int tagId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? limit,
        TelemetryService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetHistoryAsync(tagId, from, to, limit, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value!)
            : TypedResults.ValidationProblem(result.Errors!);
    }
}

using Scada.Telemetry.Api.Emulator;
using Scada.Telemetry.Api.Endpoints;
using Scada.Telemetry.Api.Serialization;
using Scada.Telemetry.Application;
using Scada.Telemetry.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Сериализация JSON через source generator (без рефлексии).
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default));

// Битый JSON или неверный параметр — это ошибка клиента (400) в любом окружении.
// По умолчанию в Development minimal API бросает исключение, и его превращает в 500 обработчик ошибок.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Слои: бизнес-логика → доступ к БД → фоновый эмулятор. Порядок регистрации hosted services важен:
// сначала SchemaInitializer (создаёт таблицу), затем эмулятор, который в неё пишет.
builder.Services.AddTelemetryApplication(options =>
    builder.Configuration.GetSection(TelemetryOptions.SectionName).Bind(options));
builder.Services.AddTelemetryInfrastructure(builder.Configuration);
builder.Services.AddTelemetryEmulator(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapTelemetryEndpoints();

app.Run();

/// <summary>Точка входа; partial-класс открыт для WebApplicationFactory в интеграционных тестах.</summary>
public partial class Program;

# syntax=docker/dockerfile:1
# Сборка из корня репозитория: docker build -f src/Scada.Telemetry.Api/Dockerfile .

# SDK запускается на архитектуре машины сборки (быстро и на Apple Silicon), а публикуем под целевую.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Сначала только файлы проектов: слой restore кешируется, пока не меняются зависимости.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Scada.Telemetry.Domain/Scada.Telemetry.Domain.csproj src/Scada.Telemetry.Domain/
COPY src/Scada.Telemetry.Application/Scada.Telemetry.Application.csproj src/Scada.Telemetry.Application/
COPY src/Scada.Telemetry.Infrastructure/Scada.Telemetry.Infrastructure.csproj src/Scada.Telemetry.Infrastructure/
COPY src/Scada.Telemetry.Api/Scada.Telemetry.Api.csproj src/Scada.Telemetry.Api/
RUN dotnet restore src/Scada.Telemetry.Api/Scada.Telemetry.Api.csproj -a $TARGETARCH

COPY db/ db/
COPY src/ src/
RUN dotnet publish src/Scada.Telemetry.Api/Scada.Telemetry.Api.csproj \
    -c Release -a $TARGETARCH --no-restore -o /app /p:UseAppHost=false

# Chiseled-образ: только рантайм .NET, без shell и пакетного менеджера, запуск не от root.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Scada.Telemetry.Api.dll"]

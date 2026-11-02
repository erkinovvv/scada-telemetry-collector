using Npgsql;
using Scada.Telemetry.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Scada.Telemetry.IntegrationTests;

/// <summary>
/// Настоящий PostgreSQL для тестов. По умолчанию поднимается в Docker через Testcontainers.
/// Если задана переменная TEST_POSTGRES_CONNECTION — используется существующая БД (удобно в CI).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string ExternalConnectionVariable = "TEST_POSTGRES_CONNECTION";

    private PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable(ExternalConnectionVariable);
        if (!string.IsNullOrWhiteSpace(external))
        {
            ConnectionString = external;
        }
        else
        {
            _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }

        DataSource = NpgsqlDataSource.Create(ConnectionString);
        await SchemaInitializer.ApplySchemaAsync(DataSource, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>Уникальный tagId для теста: тесты не мешают друг другу и не требуют очистки таблицы.</summary>
    public static int NewTagId() => Random.Shared.Next(1_000_000, int.MaxValue);
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}

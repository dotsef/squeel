using Npgsql;

using Testcontainers.PostgreSql;

namespace Squeel.Tests.Postgres;

public sealed class PostgresContainer : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;

    public string ConnectionString => _container.GetConnectionString();

    public PostgresContainer()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16")
            .WithPassword("Squeel123!")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE users(
                id uuid primary key default gen_random_uuid(),
                email varchar(312) not null,
                date_of_birth date not null,
                created timestamp with time zone not null default now(),
                bio text null
            )
            """;
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.StopAsync();
    }
}
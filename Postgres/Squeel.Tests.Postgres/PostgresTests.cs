﻿using Xunit.Abstractions;

namespace Squeel.Tests.Postgres;

[Trait("Category", "Postgres")]
public sealed class PostgresTests : IClassFixture<PostgresContainer>
{
    private readonly PostgresContainer _postgres;
    private readonly ITestOutputHelper _output;

    public PostgresTests(PostgresContainer postgres, ITestOutputHelper output)
    {
        _postgres = postgres;
        _output = output;
    }

    [Fact(DisplayName = "Silent when unused")]
    public async Task SqueelShouldNotErrorWhenUnused()
    {
        var (result, c) = await SqueelTestContext.Run(_postgres.ConnectionString, _output, """
            // No queries
            Console.WriteLine("Hello World");
            """);

        Assert.Empty(result.Diagnostics);
    }

    [Fact(DisplayName = "SELECT on simple type")]
    public async Task CoreTypeConversion()
    {
        var (result, c) = await SqueelTestContext.Run(_postgres.ConnectionString, _output, $$"""
            var users = connection.QueryAsync<string>($"SELECT email FROM users");
            """);

        Assert.Empty(result.Diagnostics);
    }

    [Fact(DisplayName = "SELECT on complex type")]
    public async Task SqueelShouldWorkForSingleQueryWithNormalInterpolation()
    {
        var (result, c) = await SqueelTestContext.Run(_postgres.ConnectionString, _output, $$"""
            var email = "test@test.com";
            var users = connection.QueryAsync<User>($"SELECT email, date_of_birth, created FROM Users WHERE email = {email}");
            """);

        Assert.Empty(result.Diagnostics);
    }

    [Fact(DisplayName = "INSERT")]
    public async Task SqueelShouldWorkForSingleInsertWithNormalInterpolation()
    {
        var (result, c) = await SqueelTestContext.Run(_postgres.ConnectionString, _output, $$"""
            var email = "test@test.com";
            var dateOfBirth = DateTime.UtcNow;
            var affected = connection.ExecuteAsync($"INSERT INTO Users (email, date_of_birth) VALUES ({email}, {dateOfBirth})");
            """);

        Assert.Empty(result.Diagnostics);
    }

    [Fact(DisplayName = "UPDATE")]
    public async Task SqueelShouldWorkForSingleUpdateWithNormalInterpolation()
    {
        var (result, c) = await SqueelTestContext.Run(_postgres.ConnectionString, _output, $$"""
            var email = "test@test.com";
            var dateOfBirth = DateTime.UtcNow;
            var affected = connection.ExecuteAsync($"UPDATE users SET email = {email}, date_of_birth = {dateOfBirth}");
            """);

        Assert.Empty(result.Diagnostics);
    }

    [Fact(DisplayName = "DELETE")]
    public async Task SqueelShouldWorkForSingleDeleteWithNormalInterpolation()
    {
        var (result, c) = await SqueelTestContext.Run(_postgres.ConnectionString, _output, $$"""
            var email = "test@test.com";
            var affected = connection.ExecuteAsync($"DELETE FROM users WHERE email = {email}");
            """);

        Assert.Empty(result.Diagnostics);
    }

    [Fact(DisplayName = "SELECT invalid query generates one diagnostic")]
    public async Task FaultySqlShouldRaiseASingleGeneratorDiagnostic()
    {
        var (result, c) = await SqueelTestContext.Run(_postgres.ConnectionString, _output, $$""""
            var _ = await connection.QueryAsync<Faulty>($"SELECT non_existent_column FROM non_existent_table");
            """");

        Assert.Single(result.Diagnostics);
    }
}
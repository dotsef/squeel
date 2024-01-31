using Npgsql;

using Squeel;

using var connection = new NpgsqlConnection("");

var test = await connection.QueryAsync<User>($"SELECT email, date_of_birth FROM users").ConfigureAwait(false);

Console.WriteLine(test.Count());
using System.Text.RegularExpressions;
using Npgsql;

// Opt-in integration fixture. Never accepts an app/production connection string.
// Every fixture owns a newly created database; only that database is dropped.
internal sealed class TestPostgres : IDisposable
{
    public static bool Enabled => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LEXIFLOW_TEST_POSTGRES"));
    private readonly string _admin;
    private readonly string _database = "lexiflow_recovery_test_" + Guid.NewGuid().ToString("N");
    public string ConnectionString { get; }
    private bool _created;

    public TestPostgres()
    {
        var config = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("LEXIFLOW_TEST_POSTGRES"));
        if (config.Host != "127.0.0.1" || config.Database != "postgres"
            || config.Username != "lexiflow_test" || config.Port is < 1024 or 5432)
            throw new InvalidOperationException("Tests require 127.0.0.1, non-default port, postgres database and lexiflow_test role on a disposable cluster.");
        config.Pooling = false;
        config.Timeout = 10;
        config.CommandTimeout = 15;
        _admin = config.ConnectionString;
        using var connection = new NpgsqlConnection(_admin);
        connection.Open();
        using var command = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", connection);
        command.ExecuteNonQuery();
        _created = true;
        config.Database = _database;
        ConnectionString = config.ConnectionString;
    }

    public void Dispose()
    {
        if (!_created) return;
        if (!Regex.IsMatch(_database, "^lexiflow_recovery_test_[a-f0-9]{32}$"))
            throw new InvalidOperationException("Unsafe test cleanup target.");
        using var connection = new NpgsqlConnection(_admin);
        connection.Open();
        using var command = new NpgsqlCommand($"DROP DATABASE \"{_database}\" WITH (FORCE)", connection);
        command.ExecuteNonQuery();
        _created = false;
    }
}

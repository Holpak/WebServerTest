using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.Sqlite;
using System.Collections.Generic;
using System.Threading.Tasks;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton<DatabaseService>();
        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        var dbService = app.Services.GetRequiredService<DatabaseService>();
        dbService.InitializeAsync().GetAwaiter().GetResult();

        app.MapPost("/create", async (DatabaseService db) =>
        {
            var id = await db.CreateObjectAsync();
            return Results.Ok(id);
        });

        app.MapPut("/edit/{id}", async (int id, string newState, DatabaseService db) =>
        {
            await db.UpdateObjectStateAsync(id, newState);
            return Results.Ok();
        });

        app.MapGet("/history/{id}", async (int id, DatabaseService db) =>
        {
            var history = await db.GetObjectHistoryAsync(id);
            return Results.Ok(history);
        });

        app.Run();
    }
}


public class DatabaseService
{
    private const string ConnectionString = "Data Source=simpleweb.db";
    private static readonly object DbLock = new object();

    public async Task InitializeAsync()
    {
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText =
        @"
            CREATE TABLE IF NOT EXISTS objects (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                state TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS state_history (
                change_id INTEGER PRIMARY KEY AUTOINCREMENT,
                object_id INTEGER NOT NULL,
                state TEXT NOT NULL,
                timestamp TEXT NOT NULL,
                FOREIGN KEY (object_id) REFERENCES objects(id)
            );
        ";
        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> CreateObjectAsync()
    {
        lock (DbLock)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText =
            @"
                INSERT INTO objects (state) VALUES ('created');
                SELECT last_insert_rowid();
            ";
            var id = (long)command.ExecuteScalar();

            AddHistoryEntry((int)id, "created", connection);

            return (int)id;
        }
    }

    public async Task UpdateObjectStateAsync(int id, string newState)
    {
        lock (DbLock)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText =
            @"
                UPDATE objects SET state = $newState WHERE id = $id;
            ";
            command.Parameters.AddWithValue("$newState", newState);
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();

            AddHistoryEntry(id, newState, connection);
        }
    }

    public async Task<List<StateHistoryEntry>> GetObjectHistoryAsync(int id)
    {
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText =
        @"
            SELECT change_id, state, timestamp FROM state_history WHERE object_id = $id ORDER BY change_id;
        ";
        command.Parameters.AddWithValue("$id", id);

        var history = new List<StateHistoryEntry>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            history.Add(new StateHistoryEntry(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        }

        return history;
    }

    private void AddHistoryEntry(int objectId, string state, SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText =
        @"
            INSERT INTO state_history (object_id, state, timestamp) VALUES ($object_id, $state, $timestamp);
        ";
        command.Parameters.AddWithValue("$object_id", objectId);
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$timestamp", System.DateTime.UtcNow.ToString("o"));
        command.ExecuteNonQuery();
    }
}

public record ObjectState(int Id, string State);
public record StateHistoryEntry(int ChangeId, string State, string Timestamp);

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
            if (string.IsNullOrEmpty(newState))
            {
                return Results.BadRequest("newState is required");
            }
            var success = await db.UpdateObjectStateAsync(id, newState);
            return success ? Results.Ok() : Results.NotFound();
        });

        app.MapGet("/history/{id}", async (int id, DatabaseService db) =>
        {
            var history = await db.GetObjectHistoryAsync(id);
            return history != null ? Results.Ok(history) : Results.NotFound();
        });

        app.Run();
    }
}


public class DatabaseService
{
    private const string ConnectionString = "Data Source=simpleweb.db";

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
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText =
        @"
            INSERT INTO objects (state) VALUES ('created');
            SELECT last_insert_rowid();
        ";
        var id = (long)await command.ExecuteScalarAsync();

        await AddHistoryEntryAsync((int)id, "created", connection);

        return (int)id;
    }

    public async Task<bool> UpdateObjectStateAsync(int id, string newState)
    {
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText =
        @"
            UPDATE objects SET state = $newState WHERE id = $id;
        ";
        command.Parameters.AddWithValue("$newState", newState);
        command.Parameters.AddWithValue("$id", id);
        var rowsAffected = await command.ExecuteNonQueryAsync();

        if (rowsAffected == 0)
        {
            return false;
        }

        await AddHistoryEntryAsync(id, newState, connection);
        return true;
    }

    public async Task<List<StateHistoryEntry>> GetObjectHistoryAsync(int id)
    {
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText =
        @"
            SELECT 1 FROM objects WHERE id = $id;
        ";
        command.Parameters.AddWithValue("$id", id);
        var exists = await command.ExecuteScalarAsync();
        if (exists == null)
        {
            return null;
        }

        command.CommandText =
        @"
            SELECT change_id, state, timestamp FROM state_history WHERE object_id = $id ORDER BY change_id;
        ";
        command.Parameters.Clear();
        command.Parameters.AddWithValue("$id", id);

        var history = new List<StateHistoryEntry>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            history.Add(new StateHistoryEntry(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        }
        return history;
    }

    private async Task AddHistoryEntryAsync(int objectId, string state, SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText =
        @"
            INSERT INTO state_history (object_id, state, timestamp) VALUES ($object_id, $state, $timestamp);
        ";
        command.Parameters.AddWithValue("$object_id", objectId);
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$timestamp", System.DateTime.UtcNow.ToString("o"));
        await command.ExecuteNonQueryAsync();
    }
}

public record ObjectState(int Id, string State);
public record StateHistoryEntry(int ChangeId, string State, string Timestamp);

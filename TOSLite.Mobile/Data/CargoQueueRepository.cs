using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Maui.Storage;
using TOSLite.Mobile.Models;

namespace TOSLite.Mobile.Data;

public sealed class CargoQueueRepository
{
    private readonly RegistrationRepository _registrations;
    private readonly PersonRepository _persons;
    private readonly string _connectionString;

    public CargoQueueRepository(RegistrationRepository registrations, PersonRepository persons)
    {
        _registrations = registrations;
        _persons = persons;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(FileSystem.AppDataDirectory, "toslite-mobile.db3"), Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
    }

    public async Task InitializeAsync()
    {
        await _registrations.InitializeAsync();
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS CargoQueue (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, SyncId TEXT NOT NULL UNIQUE,
                DesktopQueueId INTEGER NULL, RegistrationId INTEGER NOT NULL,
                DesktopRegistrationId INTEGER NULL, FullName TEXT NOT NULL, Tc TEXT NOT NULL,
                Phone TEXT NOT NULL, Address TEXT NOT NULL, TourType TEXT NOT NULL,
                Period TEXT NOT NULL, GroupType TEXT NOT NULL, DaysCount TEXT NOT NULL,
                Quantity INTEGER NOT NULL, AddedAt TEXT NOT NULL, Printed INTEGER NOT NULL,
                LastModifiedUtc TEXT NOT NULL, IsDeleted INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS IX_CargoQueue_RegistrationId ON CargoQueue(RegistrationId);
            """;
        await command.ExecuteNonQueryAsync();
        await EnsureColumnAsync(connection, "CargoQueue", "SyncId", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "CargoQueue", "DesktopQueueId", "INTEGER NULL");
        await EnsureColumnAsync(connection, "CargoQueue", "DesktopRegistrationId", "INTEGER NULL");
        await EnsureColumnAsync(connection, "CargoQueue", "LastModifiedUtc", "TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.0000000Z'");
        await EnsureColumnAsync(connection, "CargoQueue", "IsDeleted", "INTEGER NOT NULL DEFAULT 0");
        await using var update = connection.CreateCommand();
        update.CommandText = "UPDATE CargoQueue SET SyncId = lower(hex(randomblob(16))) WHERE SyncId = ''; CREATE UNIQUE INDEX IF NOT EXISTS IX_CargoQueue_SyncId ON CargoQueue(SyncId);";
        await update.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<CargoQueueRecord>> GetQueueAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, SyncId, DesktopQueueId, RegistrationId, DesktopRegistrationId, FullName, Tc, Phone, Address, TourType, Period, GroupType, DaysCount, Quantity, AddedAt, Printed, LastModifiedUtc, IsDeleted FROM CargoQueue WHERE IsDeleted=0 ORDER BY AddedAt DESC;";
        var result = new List<CargoQueueRecord>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(Read(reader));
        return result;
    }

    public async Task<IReadOnlyList<CargoQueueRecord>> GetAllForSyncAsync(IReadOnlyDictionary<int, int> desktopRegistrationIds)
    {
        var records = await GetAllForSyncAsync();
        foreach (var record in records)
        {
            if (!desktopRegistrationIds.TryGetValue(record.RegistrationId, out var desktopId)) continue;
            record.DesktopRegistrationId = desktopId;
            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE CargoQueue SET DesktopRegistrationId=$desktopId WHERE Id=$id;";
            command.Parameters.AddWithValue("$desktopId", desktopId);
            command.Parameters.AddWithValue("$id", record.Id);
            await command.ExecuteNonQueryAsync();
        }
        return records;
    }

    public async Task ApplyDesktopStateAsync(IEnumerable<CargoQueueRecord> items, IReadOnlyDictionary<int, int> localRegistrationIds)
    {
        var mapped = items.Where(x => x.IsDeleted || localRegistrationIds.ContainsKey(x.RegistrationId))
            .Select(x =>
            {
                if (!x.IsDeleted) x.RegistrationId = localRegistrationIds[x.RegistrationId];
                return x;
            }).ToList();
        await ApplyDesktopStateAsync(mapped);
    }

    public async Task AddAsync(CargoQueueRecord item)
    {
        await InitializeAsync();
        item.SyncId = string.IsNullOrWhiteSpace(item.SyncId) ? Guid.NewGuid().ToString("N") : item.SyncId;
        item.Quantity = Math.Max(1, item.Quantity);
        item.AddedAt = DateTime.UtcNow;
        item.LastModifiedUtc = DateTime.UtcNow;
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO CargoQueue (SyncId, DesktopQueueId, RegistrationId, DesktopRegistrationId, FullName, Tc, Phone, Address, TourType, Period, GroupType, DaysCount, Quantity, AddedAt, Printed, LastModifiedUtc, IsDeleted) VALUES ($syncId, $queueId, $registrationId, $desktopRegistrationId, $fullName, $tc, $phone, $address, $tourType, $period, $groupType, $daysCount, $quantity, $addedAt, 0, $modified, 0);";
        AddParameters(command, item);
        await command.ExecuteNonQueryAsync();
    }

    public async Task RemoveAsync(int id)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE CargoQueue SET IsDeleted=1, LastModifiedUtc=$modified WHERE Id=$id;";
        command.Parameters.AddWithValue("$modified", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<CargoQueueRecord>> GetAllForSyncAsync()
    {
        await InitializeAsync();
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, SyncId, DesktopQueueId, RegistrationId, DesktopRegistrationId, FullName, Tc, Phone, Address, TourType, Period, GroupType, DaysCount, Quantity, AddedAt, Printed, LastModifiedUtc, IsDeleted FROM CargoQueue ORDER BY Id;";
        var result = new List<CargoQueueRecord>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(Read(reader));
        return result;
    }

    public async Task ApplyDesktopStateAsync(IEnumerable<CargoQueueRecord> items)
    {
        await InitializeAsync();
        var existing = await GetAllForSyncAsync();
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = (SqliteTransaction)transaction;
            clear.CommandText = "DELETE FROM CargoQueue;";
            await clear.ExecuteNonQueryAsync();
        }
        var merged = existing.ToDictionary(x => x.SyncId, StringComparer.OrdinalIgnoreCase);
        foreach (var item in items) merged[item.SyncId] = item;
        foreach (var item in merged.Values)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "INSERT INTO CargoQueue (SyncId, DesktopQueueId, RegistrationId, DesktopRegistrationId, FullName, Tc, Phone, Address, TourType, Period, GroupType, DaysCount, Quantity, AddedAt, Printed, LastModifiedUtc, IsDeleted) VALUES ($syncId, $queueId, $registrationId, $desktopRegistrationId, $fullName, $tc, $phone, $address, $tourType, $period, $groupType, $daysCount, $quantity, $addedAt, $printed, $modified, $deleted);";
            AddParameters(command, item);
            command.Parameters.AddWithValue("$deleted", item.IsDeleted ? 1 : 0);
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    private static CargoQueueRecord Read(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0), SyncId = reader.GetString(1), DesktopQueueId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
        RegistrationId = reader.GetInt32(3), DesktopRegistrationId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
        FullName = reader.GetString(5), Tc = reader.GetString(6), Phone = reader.GetString(7), Address = reader.GetString(8),
        TourType = reader.GetString(9), Period = reader.GetString(10), GroupType = reader.GetString(11), DaysCount = reader.GetString(12),
        Quantity = reader.GetInt32(13), AddedAt = DateTime.Parse(reader.GetString(14), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        Printed = reader.GetInt32(15) != 0, LastModifiedUtc = DateTime.Parse(reader.GetString(16), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), IsDeleted = reader.GetInt32(17) != 0
    };

    private static void AddParameters(SqliteCommand command, CargoQueueRecord item)
    {
        command.Parameters.AddWithValue("$syncId", item.SyncId);
        command.Parameters.AddWithValue("$queueId", (object?)item.DesktopQueueId ?? DBNull.Value);
        command.Parameters.AddWithValue("$registrationId", item.RegistrationId);
        command.Parameters.AddWithValue("$desktopRegistrationId", (object?)item.DesktopRegistrationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$fullName", item.FullName); command.Parameters.AddWithValue("$tc", item.Tc);
        command.Parameters.AddWithValue("$phone", item.Phone); command.Parameters.AddWithValue("$address", item.Address);
        command.Parameters.AddWithValue("$tourType", item.TourType); command.Parameters.AddWithValue("$period", item.Period);
        command.Parameters.AddWithValue("$groupType", item.GroupType); command.Parameters.AddWithValue("$daysCount", item.DaysCount);
        command.Parameters.AddWithValue("$quantity", item.Quantity); command.Parameters.AddWithValue("$addedAt", item.AddedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$printed", item.Printed ? 1 : 0); command.Parameters.AddWithValue("$modified", item.LastModifiedUtc.ToString("O", CultureInfo.InvariantCulture));
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        await _persons.InitializeAsync();
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task EnsureColumnAsync(SqliteConnection connection, string table, string column, string definition)
    {
        await using var check = connection.CreateCommand(); check.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await check.ExecuteReaderAsync();
        while (await reader.ReadAsync()) if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
        await reader.DisposeAsync();
        await using var alter = connection.CreateCommand(); alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync();
    }
}

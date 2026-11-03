using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Maui.Storage;
using TOSLite.Mobile.Models;
using TOSLite.Mobile.Services;

namespace TOSLite.Mobile.Data;

public sealed class RoomAllocationRepository
{
    private readonly PersonRepository _personRepository;

    public RoomAllocationRepository(PersonRepository personRepository)
    {
        _personRepository = personRepository;
    }

    public async Task InitializeAsync()
    {
        await _personRepository.InitializeAsync();
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Hotels (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE
            );
            CREATE TABLE IF NOT EXISTS RoomAllocations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                HotelId INTEGER NOT NULL,
                RoomNumber TEXT NOT NULL,
                RoomType TEXT NOT NULL,
                TourType TEXT NOT NULL,
                Period TEXT NOT NULL,
                GroupType TEXT NOT NULL,
                DaysCount TEXT NOT NULL,
                CheckInDate TEXT NOT NULL,
                CheckOutDate TEXT NOT NULL,
                IsConnection INTEGER NOT NULL,
                ConnectionGroupId TEXT NOT NULL,
                SyncId TEXT NOT NULL DEFAULT '',
                LastModifiedUtc TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.0000000Z',
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (HotelId) REFERENCES Hotels(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_RoomAllocations_HotelCriteria
                ON RoomAllocations (HotelId, TourType, Period, GroupType, DaysCount);
            CREATE TABLE IF NOT EXISTS RoomOccupants (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RoomAllocationId INTEGER NOT NULL,
                PassengerId INTEGER NOT NULL,
                FOREIGN KEY (RoomAllocationId) REFERENCES RoomAllocations(Id) ON DELETE CASCADE,
                FOREIGN KEY (PassengerId) REFERENCES PersonRecords(Id) ON DELETE RESTRICT,
                UNIQUE (RoomAllocationId, PassengerId)
            );
            CREATE INDEX IF NOT EXISTS IX_RoomOccupants_PassengerId
                ON RoomOccupants (PassengerId);
            """;
        await command.ExecuteNonQueryAsync();
        await EnsureColumnAsync(connection, "RoomAllocations", "SyncId", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "RoomAllocations", "LastModifiedUtc", "TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.0000000Z'");
        await EnsureColumnAsync(connection, "RoomAllocations", "IsDeleted", "INTEGER NOT NULL DEFAULT 0");
        await using var syncIds = connection.CreateCommand();
        syncIds.CommandText = "UPDATE RoomAllocations SET SyncId = lower(hex(randomblob(16))) WHERE SyncId = '';";
        await syncIds.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<string>> GetHotelsAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppSettings WHERE Type = 'Otel Ekle' AND IsActive = 1 ORDER BY SortOrder, Id;";
        var hotels = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) hotels.Add(reader.GetString(0));
        return hotels;
    }

    public async Task<IReadOnlyList<RoomingCriteria>> GetCriteriaAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT r.TourType, r.Period, r.GroupType, r.DaysCount
            FROM TourRegistrations r
            WHERE r.IsActive = 1
            ORDER BY r.TourType, r.Period, r.GroupType, r.DaysCount;
            """;
        var criteria = new List<RoomingCriteria>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            criteria.Add(new RoomingCriteria
            {
                TourType = reader.GetString(0),
                Period = reader.GetString(1),
                GroupType = reader.GetString(2),
                DaysCount = reader.GetString(3)
            });
        }
        return criteria;
    }

    public async Task<IReadOnlyList<RoomingCandidate>> GetCandidatesAsync(RoomingCriteria criteria)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.Id, p.FirstName || ' ' || p.LastName, r.RoomType
            FROM TourRegistrations r
            INNER JOIN PersonRecords p ON p.Id = r.PersonId
            WHERE r.IsActive = 1 AND p.IsActive = 1
              AND r.TourType = $tourType AND r.Period = $period
              AND r.GroupType = $groupType AND r.DaysCount = $daysCount
              AND NOT EXISTS (
                  SELECT 1 FROM RoomOccupants o
                  INNER JOIN RoomAllocations a ON a.Id = o.RoomAllocationId
                  WHERE o.PassengerId = p.Id
                    AND a.TourType = $tourType AND a.Period = $period
                    AND a.GroupType = $groupType AND a.DaysCount = $daysCount
                    AND a.HotelId <> COALESCE((SELECT Id FROM Hotels WHERE Name = $hotelName), -1)
              )
            ORDER BY p.LastName, p.FirstName, p.Id;
            """;
        AddCriteria(command, criteria);
        var candidates = new List<RoomingCandidate>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            candidates.Add(new RoomingCandidate
            {
                PersonId = reader.GetInt32(0),
                PersonName = reader.GetString(1),
                RoomType = string.IsNullOrWhiteSpace(reader.GetString(2)) ? "Belirtilmemiş" : reader.GetString(2)
            });
        }
        return candidates;
    }

    public async Task<IReadOnlyList<RoomAllocationRecord>> GetAllocationsAsync(RoomingCriteria criteria)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.Id, a.HotelId, h.Name, a.RoomNumber, a.RoomType, a.TourType, a.Period,
                   a.GroupType, a.DaysCount, a.CheckInDate, a.CheckOutDate,
                   a.IsConnection, a.ConnectionGroupId, a.SyncId, a.LastModifiedUtc, a.IsDeleted,
                   o.Id, o.PassengerId, p.FirstName || ' ' || p.LastName, r.RoomType
            FROM RoomAllocations a
            INNER JOIN Hotels h ON h.Id = a.HotelId
            LEFT JOIN RoomOccupants o ON o.RoomAllocationId = a.Id
            LEFT JOIN PersonRecords p ON p.Id = o.PassengerId
            LEFT JOIN TourRegistrations r ON r.PersonId = o.PassengerId
                AND r.TourType = a.TourType AND r.Period = a.Period
                AND r.GroupType = a.GroupType AND r.DaysCount = a.DaysCount
            WHERE a.HotelId = (SELECT Id FROM Hotels WHERE Name = $hotelName)
              AND a.TourType = $tourType AND a.Period = $period
              AND a.GroupType = $groupType AND a.DaysCount = $daysCount
              AND a.IsDeleted = 0
            ORDER BY CAST(a.RoomNumber AS INTEGER), a.RoomNumber, o.Id;
            """;
        AddCriteria(command, criteria);
        var allocations = new Dictionary<int, RoomAllocationRecord>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var allocationId = reader.GetInt32(0);
            if (!allocations.TryGetValue(allocationId, out var allocation))
            {
                allocation = new RoomAllocationRecord
                {
                    Id = allocationId,
                    HotelId = reader.GetInt32(1),
                    HotelName = reader.GetString(2),
                    RoomNumber = reader.GetString(3),
                    RoomType = reader.GetString(4),
                    TourType = reader.GetString(5),
                    Period = reader.GetString(6),
                    GroupType = reader.GetString(7),
                    DaysCount = reader.GetString(8),
                    CheckInDate = reader.GetString(9),
                    CheckOutDate = reader.GetString(10),
                    IsConnection = reader.GetInt32(11) != 0,
                    ConnectionGroupId = reader.GetString(12),
                    SyncId = reader.GetString(13),
                    LastModifiedUtc = DateTime.Parse(reader.GetString(14), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    IsDeleted = reader.GetInt32(15) != 0
                };
                allocations.Add(allocationId, allocation);
            }
            if (!reader.IsDBNull(16))
            {
                allocation.Occupants.Add(new RoomOccupantRecord
                {
                    Id = reader.GetInt32(16),
                    RoomAllocationId = allocationId,
                    PassengerId = reader.GetInt32(17),
                    PersonName = reader.IsDBNull(18) ? string.Empty : reader.GetString(18),
                    RoomType = reader.IsDBNull(19) ? string.Empty : reader.GetString(19)
                });
            }
        }
        return allocations.Values.ToList();
    }

    public async Task<IReadOnlyList<RoomAllocationRecord>> GetAllForSyncAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.Id, h.Name, a.RoomNumber, a.RoomType, a.TourType, a.Period, a.GroupType,
                   a.DaysCount, a.CheckInDate, a.CheckOutDate, a.IsConnection, a.ConnectionGroupId,
                   a.SyncId, a.LastModifiedUtc, a.IsDeleted, o.PassengerId
            FROM RoomAllocations a
            INNER JOIN Hotels h ON h.Id = a.HotelId
            LEFT JOIN RoomOccupants o ON o.RoomAllocationId = a.Id
            ORDER BY a.Id, o.Id;
            """;
        var records = new Dictionary<int, RoomAllocationRecord>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetInt32(0);
            if (!records.TryGetValue(id, out var record))
            {
                record = new RoomAllocationRecord
                {
                    Id = id,
                    HotelName = reader.GetString(1),
                    RoomNumber = reader.GetString(2),
                    RoomType = reader.GetString(3),
                    TourType = reader.GetString(4),
                    Period = reader.GetString(5),
                    GroupType = reader.GetString(6),
                    DaysCount = reader.GetString(7),
                    CheckInDate = reader.GetString(8),
                    CheckOutDate = reader.GetString(9),
                    IsConnection = reader.GetInt32(10) != 0,
                    ConnectionGroupId = reader.GetString(11),
                    SyncId = reader.GetString(12),
                    LastModifiedUtc = DateTime.Parse(reader.GetString(13), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    IsDeleted = reader.GetInt32(14) != 0
                };
                records.Add(id, record);
            }
            if (!reader.IsDBNull(15)) record.Occupants.Add(new RoomOccupantRecord { PassengerId = reader.GetInt32(15) });
        }
        return records.Values.ToList();
    }

    public async Task ApplyDesktopStateAsync(IReadOnlyList<TOSLite.Mobile.Services.MobileRoomingDto> rooms)
    {
        if (rooms.Count == 0) return;
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var room in rooms)
        {
            var existingId = 0;
            await using (var find = connection.CreateCommand())
            {
                find.Transaction = (SqliteTransaction)transaction;
                find.CommandText = "SELECT Id, LastModifiedUtc FROM RoomAllocations WHERE SyncId=$syncId OR ($id>0 AND Id=$id) LIMIT 1;";
                find.Parameters.AddWithValue("$syncId", room.SyncId);
                find.Parameters.AddWithValue("$id", room.Id);
                await using var reader = await find.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    existingId = reader.GetInt32(0);
                    var localModified = DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                    if (localModified > room.LastModifiedUtc) continue;
                }
            }
            if (existingId > 0)
            {
                await using var update = connection.CreateCommand();
                update.Transaction = (SqliteTransaction)transaction;
                update.CommandText = "UPDATE RoomAllocations SET RoomNumber=$roomNumber, RoomType=$roomType, TourType=$tourType, Period=$period, GroupType=$groupType, DaysCount=$daysCount, CheckInDate=$checkIn, CheckOutDate=$checkOut, IsConnection=$isConnection, ConnectionGroupId=$connectionGroupId, SyncId=$syncId, LastModifiedUtc=$modified, IsDeleted=$deleted WHERE Id=$id; DELETE FROM RoomOccupants WHERE RoomAllocationId=$id;";
                update.Parameters.AddWithValue("$id", existingId);
                AddRoomParameters(update, room);
                await update.ExecuteNonQueryAsync();
            }
            else
            {
                if (room.IsDeleted) continue;
                await using var hotel = connection.CreateCommand();
                hotel.Transaction = (SqliteTransaction)transaction;
                hotel.CommandText = "INSERT INTO Hotels (Name) VALUES ($name) ON CONFLICT(Name) DO NOTHING; SELECT Id FROM Hotels WHERE Name=$name;";
                hotel.Parameters.AddWithValue("$name", room.HotelName);
                var hotelId = Convert.ToInt32(await hotel.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
                await using var insert = connection.CreateCommand();
                insert.Transaction = (SqliteTransaction)transaction;
                insert.CommandText = "INSERT INTO RoomAllocations (HotelId,RoomNumber,RoomType,TourType,Period,GroupType,DaysCount,CheckInDate,CheckOutDate,IsConnection,ConnectionGroupId,SyncId,LastModifiedUtc,IsDeleted) VALUES ($hotelId,$roomNumber,$roomType,$tourType,$period,$groupType,$daysCount,$checkIn,$checkOut,$isConnection,$connectionGroupId,$syncId,$modified,$deleted); SELECT last_insert_rowid();";
                insert.Parameters.AddWithValue("$hotelId", hotelId);
                AddRoomParameters(insert, room);
                existingId = Convert.ToInt32(await insert.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            }
            if (!room.IsDeleted)
            foreach (var passengerId in room.PassengerIds.Distinct())
            {
                await using (var personExists = connection.CreateCommand())
                {
                    personExists.Transaction = (SqliteTransaction)transaction;
                    personExists.CommandText = "SELECT EXISTS(SELECT 1 FROM PersonRecords WHERE Id=$personId AND IsDeleted=0);";
                    personExists.Parameters.AddWithValue("$personId", passengerId);
                    if (Convert.ToInt32(await personExists.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 0) continue;
                }
                await using var occupant = connection.CreateCommand();
                occupant.Transaction = (SqliteTransaction)transaction;
                occupant.CommandText = "INSERT OR IGNORE INTO RoomOccupants (RoomAllocationId,PassengerId) VALUES ($roomId,$personId);";
                occupant.Parameters.AddWithValue("$roomId", existingId);
                occupant.Parameters.AddWithValue("$personId", passengerId);
                await occupant.ExecuteNonQueryAsync();
            }
        }
        await transaction.CommitAsync();
    }

    private static void AddRoomParameters(SqliteCommand command, TOSLite.Mobile.Services.MobileRoomingDto room)
    {
        command.Parameters.AddWithValue("$roomNumber", room.RoomNumber);
        command.Parameters.AddWithValue("$roomType", room.RoomType);
        command.Parameters.AddWithValue("$tourType", room.TourType);
        command.Parameters.AddWithValue("$period", room.Period);
        command.Parameters.AddWithValue("$groupType", room.GroupType);
        command.Parameters.AddWithValue("$daysCount", room.DaysCount);
        command.Parameters.AddWithValue("$checkIn", room.CheckInDate);
        command.Parameters.AddWithValue("$checkOut", room.CheckOutDate);
        command.Parameters.AddWithValue("$isConnection", room.IsConnection ? 1 : 0);
        command.Parameters.AddWithValue("$connectionGroupId", room.ConnectionGroupId);
        command.Parameters.AddWithValue("$syncId", room.SyncId);
        command.Parameters.AddWithValue("$modified", room.LastModifiedUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$deleted", room.IsDeleted ? 1 : 0);
    }

    public async Task SaveBatchAsync(RoomingCriteria criteria, IReadOnlyList<RoomAllocationRecord> allocations, bool allowCapacityOverride = false)
    {
        if (allocations.Count > 0 && allocations.All(x => x.Occupants.Count == 0))
            throw new InvalidOperationException("Kaydetmek için en az bir yerleştirilmiş kişi gereklidir.");
        if (!allowCapacityOverride && allocations.Any(x => x.Occupants.Count > x.Capacity))
            throw new InvalidOperationException("Oda kapasitesi aşılamaz.");
        if (allocations.SelectMany(x => x.Occupants).GroupBy(x => x.PassengerId).Any(x => x.Count() > 1))
            throw new InvalidOperationException("Aynı kişi birden fazla odaya yerleştirilemez.");
        if (allocations.Any(x => x.Occupants.Any(o => o.PassengerId <= 0)))
            throw new InvalidOperationException("Geçersiz kişi kaydı bulundu.");
        if (allocations.Any(x => !DateOnly.TryParseExact(x.CheckInDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                                 !DateOnly.TryParseExact(x.CheckOutDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
            throw new InvalidOperationException("Giriş ve çıkış tarihlerini geçerli biçimde girin.");

        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var hotelId = await GetOrCreateHotelAsync(connection, (SqliteTransaction)transaction, criteria.HotelName);
        var modifiedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var existingAllocations = new List<(int Id, string RoomNumber, string SyncId)>();
        await using (var existing = connection.CreateCommand())
        {
            existing.Transaction = (SqliteTransaction)transaction;
            existing.CommandText = "SELECT Id, RoomNumber, SyncId FROM RoomAllocations WHERE HotelId = $hotelId AND TourType = $tourType AND Period = $period AND GroupType = $groupType AND DaysCount = $daysCount;";
            existing.Parameters.AddWithValue("$hotelId", hotelId);
            AddCriteria(existing, criteria);
            await using var reader = await existing.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                existingAllocations.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        }
        var retainedIds = new HashSet<int>();
        foreach (var allocation in allocations)
        {
            var existing = existingAllocations.FirstOrDefault(x => allocation.Id > 0 && x.Id == allocation.Id);
            if (existing.Id == 0 && !string.IsNullOrWhiteSpace(allocation.SyncId))
                existing = existingAllocations.FirstOrDefault(x => x.SyncId == allocation.SyncId);
            if (existing.Id == 0)
                existing = existingAllocations.FirstOrDefault(x => x.RoomNumber == allocation.RoomNumber);
            var syncId = !string.IsNullOrWhiteSpace(allocation.SyncId) ? allocation.SyncId :
                !string.IsNullOrWhiteSpace(existing.SyncId) ? existing.SyncId : Guid.NewGuid().ToString("N");
            int allocationId;
            await using (var save = connection.CreateCommand())
            {
                save.Transaction = (SqliteTransaction)transaction;
                save.CommandText = existing.Id > 0
                    ? "UPDATE RoomAllocations SET RoomNumber=$roomNumber, RoomType=$roomType, CheckInDate=$checkIn, CheckOutDate=$checkOut, IsConnection=$isConnection, ConnectionGroupId=$connectionGroupId, SyncId=$syncId, LastModifiedUtc=$lastModifiedUtc, IsDeleted=0 WHERE Id=$id; DELETE FROM RoomOccupants WHERE RoomAllocationId=$id;"
                    : "INSERT INTO RoomAllocations (HotelId, RoomNumber, RoomType, TourType, Period, GroupType, DaysCount, CheckInDate, CheckOutDate, IsConnection, ConnectionGroupId, SyncId, LastModifiedUtc, IsDeleted) VALUES ($hotelId,$roomNumber,$roomType,$tourType,$period,$groupType,$daysCount,$checkIn,$checkOut,$isConnection,$connectionGroupId,$syncId,$lastModifiedUtc,0); SELECT last_insert_rowid();";
                save.Parameters.AddWithValue("$hotelId", hotelId);
                save.Parameters.AddWithValue("$roomNumber", allocation.RoomNumber);
                save.Parameters.AddWithValue("$roomType", allocation.RoomType);
                save.Parameters.AddWithValue("$tourType", criteria.TourType);
                save.Parameters.AddWithValue("$period", criteria.Period);
                save.Parameters.AddWithValue("$groupType", criteria.GroupType);
                save.Parameters.AddWithValue("$daysCount", criteria.DaysCount);
                save.Parameters.AddWithValue("$checkIn", allocation.CheckInDate);
                save.Parameters.AddWithValue("$checkOut", allocation.CheckOutDate);
                save.Parameters.AddWithValue("$isConnection", allocation.IsConnection ? 1 : 0);
                save.Parameters.AddWithValue("$connectionGroupId", allocation.ConnectionGroupId);
                save.Parameters.AddWithValue("$syncId", syncId);
                save.Parameters.AddWithValue("$lastModifiedUtc", modifiedAt);
                if (existing.Id > 0)
                {
                    save.Parameters.AddWithValue("$id", existing.Id);
                    await save.ExecuteNonQueryAsync();
                    allocationId = existing.Id;
                }
                else allocationId = Convert.ToInt32(await save.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            }
            if (existing.Id > 0) retainedIds.Add(existing.Id);

            foreach (var occupant in allocation.Occupants)
            {
                await using var insertOccupant = connection.CreateCommand();
                insertOccupant.Transaction = (SqliteTransaction)transaction;
                insertOccupant.CommandText = "INSERT INTO RoomOccupants (RoomAllocationId, PassengerId) VALUES ($roomId, $personId);";
                insertOccupant.Parameters.AddWithValue("$roomId", allocationId);
                insertOccupant.Parameters.AddWithValue("$personId", occupant.PassengerId);
                await insertOccupant.ExecuteNonQueryAsync();
            }
        }
        foreach (var removed in existingAllocations.Where(x => !retainedIds.Contains(x.Id)))
        {
            await using var tombstone = connection.CreateCommand();
            tombstone.Transaction = (SqliteTransaction)transaction;
            tombstone.CommandText = "UPDATE RoomAllocations SET IsDeleted=1, LastModifiedUtc=$lastModifiedUtc WHERE Id=$id; DELETE FROM RoomOccupants WHERE RoomAllocationId=$id;";
            tombstone.Parameters.AddWithValue("$id", removed.Id);
            tombstone.Parameters.AddWithValue("$lastModifiedUtc", modifiedAt);
            await tombstone.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    private static async Task EnsureColumnAsync(SqliteConnection connection, string table, string column, string declaration)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await check.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
        await reader.DisposeAsync();
        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {declaration};";
        await alter.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<string>> GetRoomTypesAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppSettings WHERE Type = 'RoomType' AND IsActive = 1 ORDER BY SortOrder, Id;";
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return values;
    }

    private static void AddCriteria(SqliteCommand command, RoomingCriteria criteria)
    {
        command.Parameters.AddWithValue("$hotelName", criteria.HotelName);
        command.Parameters.AddWithValue("$tourType", criteria.TourType);
        command.Parameters.AddWithValue("$period", criteria.Period);
        command.Parameters.AddWithValue("$groupType", criteria.GroupType);
        command.Parameters.AddWithValue("$daysCount", criteria.DaysCount);
    }

    private static async Task<int> GetOrCreateHotelAsync(SqliteConnection connection, SqliteTransaction transaction, string hotelName)
    {
        if (string.IsNullOrWhiteSpace(hotelName)) throw new InvalidOperationException("Lütfen otel seçin.");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO Hotels (Name) VALUES ($name) ON CONFLICT(Name) DO NOTHING; SELECT Id FROM Hotels WHERE Name = $name;";
        command.Parameters.AddWithValue("$name", hotelName.Trim());
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        await _personRepository.InitializeAsync();
        var path = Path.Combine(FileSystem.AppDataDirectory, "toslite-mobile.db3");
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        }.ToString());
        await connection.OpenAsync();
        return connection;
    }
}

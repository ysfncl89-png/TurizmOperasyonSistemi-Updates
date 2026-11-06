using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Maui.Storage;
using TOSLite.Mobile.Models;

namespace TOSLite.Mobile.Data;

public sealed class PaymentRepository
{
    private readonly RegistrationRepository _registrations;
    private readonly PersonRepository _persons;
    private readonly string _connectionString;

    public PaymentRepository(RegistrationRepository registrations, PersonRepository persons)
    {
        _registrations = registrations;
        _persons = persons;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(FileSystem.AppDataDirectory, "toslite-mobile.db3"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        }.ToString();
    }

    public async Task InitializeAsync()
    {
        await _registrations.InitializeAsync();
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Payments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SyncId TEXT NOT NULL UNIQUE,
                DesktopPaymentId INTEGER NULL,
                RegistrationId INTEGER NOT NULL,
                DesktopRegistrationId INTEGER NULL,
                CreatedAt TEXT NOT NULL,
                OriginalAmount REAL NOT NULL,
                OriginalCurrency TEXT NOT NULL,
                AmountInRegistrationCurrency REAL NOT NULL,
                Rate REAL NULL,
                Note TEXT NOT NULL,
                UploadedDocumentPath TEXT NOT NULL,
                PaymentMethod TEXT NOT NULL,
                LastModifiedUtc TEXT NOT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (RegistrationId) REFERENCES TourRegistrations(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_Payments_RegistrationId ON Payments(RegistrationId);
            """;
        await command.ExecuteNonQueryAsync();
        await EnsureColumnAsync(connection, "Payments", "SyncId", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "Payments", "DesktopPaymentId", "INTEGER NULL");
        await EnsureColumnAsync(connection, "Payments", "DesktopRegistrationId", "INTEGER NULL");
        await EnsureColumnAsync(connection, "Payments", "LastModifiedUtc", "TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.0000000Z'");
        await EnsureColumnAsync(connection, "Payments", "IsDeleted", "INTEGER NOT NULL DEFAULT 0");
        await using var update = connection.CreateCommand();
        update.CommandText = "UPDATE Payments SET SyncId = lower(hex(randomblob(16))) WHERE SyncId = ''; CREATE UNIQUE INDEX IF NOT EXISTS IX_Payments_SyncId ON Payments(SyncId);";
        await update.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<PaymentRecord>> GetAllAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.Id, p.SyncId, p.DesktopPaymentId, p.RegistrationId, p.DesktopRegistrationId, p.CreatedAt,
                   p.OriginalAmount, p.OriginalCurrency, p.AmountInRegistrationCurrency,
                   p.Rate, p.Note, p.UploadedDocumentPath, p.PaymentMethod,
                   p.LastModifiedUtc, p.IsDeleted, pe.FirstName || ' ' || pe.LastName,
                   r.Currency, r.TourType || ' · ' || r.Period || ' · ' || r.GroupType || ' · ' || r.DaysCount
            FROM Payments p
            INNER JOIN TourRegistrations r ON r.Id = p.RegistrationId
            INNER JOIN PersonRecords pe ON pe.Id = r.PersonId
            ORDER BY p.CreatedAt DESC, p.Id DESC;
            """;
        var payments = new List<PaymentRecord>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) payments.Add(Read(reader));
        return payments;
    }

    public async Task<IReadOnlyList<PaymentRecord>> GetForRegistrationAsync(int registrationId)
    {
        var all = await GetAllAsync();
        return all.Where(x => x.RegistrationId == registrationId).ToList();
    }

    public async Task SaveAsync(PaymentRecord payment)
    {
        await InitializeAsync();
        if (payment.RegistrationId <= 0) throw new InvalidOperationException("Ödeme için tur kaydı seçilmelidir.");
        payment.LastModifiedUtc = DateTime.UtcNow;
        payment.SyncId = string.IsNullOrWhiteSpace(payment.SyncId) ? Guid.NewGuid().ToString("N") : payment.SyncId;
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = payment.Id <= 0
            ? "INSERT INTO Payments (SyncId, DesktopPaymentId, RegistrationId, CreatedAt, OriginalAmount, OriginalCurrency, AmountInRegistrationCurrency, Rate, Note, UploadedDocumentPath, PaymentMethod, LastModifiedUtc, IsDeleted) VALUES ($syncId, $desktopId, $registrationId, $createdAt, $originalAmount, $originalCurrency, $converted, $rate, $note, $document, $method, $modified, $deleted); SELECT last_insert_rowid();"
            : "UPDATE Payments SET SyncId=$syncId, DesktopPaymentId=$desktopId, RegistrationId=$registrationId, CreatedAt=$createdAt, OriginalAmount=$originalAmount, OriginalCurrency=$originalCurrency, AmountInRegistrationCurrency=$converted, Rate=$rate, Note=$note, UploadedDocumentPath=$document, PaymentMethod=$method, LastModifiedUtc=$modified, IsDeleted=$deleted WHERE Id=$id; SELECT $id;";
        AddParameters(command, payment);
        if (payment.Id > 0) command.Parameters.AddWithValue("$id", payment.Id);
        payment.Id = Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        await transaction.CommitAsync();
        await RecalculateRegistrationAsync(payment.RegistrationId);
    }

    public async Task DeleteAsync(int paymentId)
    {
        await InitializeAsync();
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Payments SET IsDeleted = 1, LastModifiedUtc = $modified WHERE Id = $id; SELECT RegistrationId FROM Payments WHERE Id = $id;";
        command.Parameters.AddWithValue("$modified", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$id", paymentId);
        var registrationId = Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        await RecalculateRegistrationAsync(registrationId);
    }

    public async Task<IReadOnlyList<PaymentRecord>> GetAllForSyncAsync()
    {
        await InitializeAsync();
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, SyncId, DesktopPaymentId, RegistrationId, DesktopRegistrationId, CreatedAt, OriginalAmount, OriginalCurrency, AmountInRegistrationCurrency, Rate, Note, UploadedDocumentPath, PaymentMethod, LastModifiedUtc, IsDeleted, '', '', '' FROM Payments ORDER BY Id;";
        var records = new List<PaymentRecord>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) records.Add(Read(reader));
        return records;
    }

    public async Task<IReadOnlyList<PaymentRecord>> GetAllForSyncAsync(IReadOnlyDictionary<int, int> desktopRegistrationIds)
    {
        var records = await GetAllForSyncAsync();
        foreach (var record in records)
            if (desktopRegistrationIds.TryGetValue(record.RegistrationId, out var desktopId))
            {
                record.DesktopRegistrationId = desktopId;
                await SaveDesktopRegistrationIdAsync(record.Id, desktopId);
            }
        return records;
    }

    private async Task SaveDesktopRegistrationIdAsync(int paymentId, int desktopRegistrationId)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Payments SET DesktopRegistrationId=$desktopId WHERE Id=$id;";
        command.Parameters.AddWithValue("$desktopId", desktopRegistrationId);
        command.Parameters.AddWithValue("$id", paymentId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task ApplyDesktopStateAsync(IEnumerable<PaymentRecord> payments)
    {
        await InitializeAsync();
        var records = payments.ToList();
        var oldRegistrationIds = (await GetAllForSyncAsync()).Select(x => x.RegistrationId).Distinct().ToList();
        foreach (var payment in records)
            if (payment.DesktopPaymentId is > 0) payment.Id = payment.DesktopPaymentId.Value;
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = (SqliteTransaction)transaction;
            clear.CommandText = "DELETE FROM Payments;";
            await clear.ExecuteNonQueryAsync();
        }
        foreach (var payment in records)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "INSERT INTO Payments (SyncId, DesktopPaymentId, RegistrationId, DesktopRegistrationId, CreatedAt, OriginalAmount, OriginalCurrency, AmountInRegistrationCurrency, Rate, Note, UploadedDocumentPath, PaymentMethod, LastModifiedUtc, IsDeleted) VALUES ($syncId, $desktopId, $registrationId, $desktopRegistrationId, $createdAt, $originalAmount, $originalCurrency, $converted, $rate, $note, $document, $method, $modified, $deleted);";
            AddParameters(command, payment);
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        foreach (var registrationId in oldRegistrationIds)
            await RecalculateRegistrationAsync(registrationId);
        foreach (var registrationId in records.Where(x => !x.IsDeleted).Select(x => x.RegistrationId).Distinct())
            await RecalculateRegistrationAsync(registrationId);
    }

    public async Task ApplyDesktopStateAsync(IEnumerable<PaymentRecord> payments, IReadOnlyDictionary<int, int> localRegistrationIds)
    {
        var mapped = payments.Where(x => x.IsDeleted || localRegistrationIds.ContainsKey(x.RegistrationId))
            .Select(x =>
            {
                if (!x.IsDeleted) x.RegistrationId = localRegistrationIds[x.RegistrationId];
                return x;
            }).ToList();
        await ApplyDesktopStateAsync(mapped);
    }

    private async Task RecalculateRegistrationAsync(int registrationId)
    {
        var registration = await _registrations.GetByIdAsync(registrationId);
        if (registration == null) return;
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(SUM(AmountInRegistrationCurrency), 0) FROM Payments WHERE RegistrationId=$registrationId AND IsDeleted=0;";
        command.Parameters.AddWithValue("$registrationId", registrationId);
        var paidAmount = Convert.ToDecimal(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        await _registrations.SavePaymentSummaryAsync(registrationId, paidAmount);
    }

    private static PaymentRecord Read(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0), SyncId = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
        DesktopPaymentId = reader.IsDBNull(2) ? null : reader.GetInt32(2), RegistrationId = reader.GetInt32(3),
        DesktopRegistrationId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
        CreatedAt = DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        OriginalAmount = Convert.ToDecimal(reader.GetValue(6), CultureInfo.InvariantCulture), OriginalCurrency = reader.GetString(7),
        AmountInRegistrationCurrency = Convert.ToDecimal(reader.GetValue(8), CultureInfo.InvariantCulture),
        Rate = reader.IsDBNull(9) ? null : Convert.ToDecimal(reader.GetValue(9), CultureInfo.InvariantCulture),
        Note = reader.GetString(10), UploadedDocumentPath = reader.GetString(11), PaymentMethod = reader.GetString(12),
        LastModifiedUtc = DateTime.Parse(reader.GetString(13), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        IsDeleted = reader.GetInt32(14) != 0, PersonName = reader.GetString(15), RegistrationCurrency = reader.GetString(16), TourSummary = reader.GetString(17)
    };

    private static void AddParameters(SqliteCommand command, PaymentRecord payment)
    {
        command.Parameters.AddWithValue("$syncId", payment.SyncId);
        command.Parameters.AddWithValue("$desktopId", (object?)payment.DesktopPaymentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$registrationId", payment.RegistrationId);
        command.Parameters.AddWithValue("$desktopRegistrationId", (object?)payment.DesktopRegistrationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", payment.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$originalAmount", payment.OriginalAmount);
        command.Parameters.AddWithValue("$originalCurrency", payment.OriginalCurrency);
        command.Parameters.AddWithValue("$converted", payment.AmountInRegistrationCurrency);
        command.Parameters.AddWithValue("$rate", (object?)payment.Rate ?? DBNull.Value);
        command.Parameters.AddWithValue("$note", payment.Note);
        command.Parameters.AddWithValue("$document", payment.UploadedDocumentPath);
        command.Parameters.AddWithValue("$method", payment.PaymentMethod);
        command.Parameters.AddWithValue("$modified", payment.LastModifiedUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$deleted", payment.IsDeleted ? 1 : 0);
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
        await using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await check.ExecuteReaderAsync();
        while (await reader.ReadAsync()) if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
        await reader.DisposeAsync();
        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync();
    }
}

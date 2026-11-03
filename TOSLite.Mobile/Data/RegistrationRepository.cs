using System.Globalization;
using Microsoft.Data.Sqlite;
using TOSLite.Mobile.Models;

namespace TOSLite.Mobile.Data;

public sealed class RegistrationRepository
{
    private readonly PersonRepository _personRepository;

    public RegistrationRepository(PersonRepository personRepository)
    {
        _personRepository = personRepository;
    }

    public async Task InitializeAsync()
    {
        await _personRepository.InitializeAsync();
        await using var connection = await OpenSharedConnectionAsync();
        await CreateSchemaIfNeededAsync(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS TourRegistrations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PersonId INTEGER NOT NULL,
                TourType TEXT NOT NULL,
                Period TEXT NOT NULL,
                GroupType TEXT NOT NULL,
                DaysCount TEXT NOT NULL,
                VisaRequest TEXT NOT NULL,
                TicketRequest TEXT NOT NULL,
                VisaStatusChoice TEXT NOT NULL,
                TicketStatusChoice TEXT NOT NULL,
                PassportArrived INTEGER NULL,
                PassportArrivedValue TEXT NOT NULL DEFAULT '',
                CargoRequest TEXT NOT NULL,
                RoomType TEXT NOT NULL,
                DepartureCity TEXT NOT NULL,
                HasTransfer TEXT NOT NULL,
                TransferCity TEXT NOT NULL,
                District TEXT NOT NULL,
                TotalPrice REAL NOT NULL,
                Currency TEXT NOT NULL,
                PaidAmount REAL NOT NULL,
                RemainingDebt REAL NOT NULL,
                Reference TEXT NOT NULL,
                Status TEXT NOT NULL,
                CreatedDate TEXT NOT NULL,
                IsActive INTEGER NOT NULL,
                LastModifiedUtc TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.0000000Z',
                FOREIGN KEY (PersonId) REFERENCES PersonRecords(Id) ON DELETE RESTRICT,
                UNIQUE (PersonId, TourType, Period, GroupType, DaysCount)
            );
            CREATE INDEX IF NOT EXISTS IX_TourRegistrations_PersonId
                ON TourRegistrations (PersonId);
            """;
        await command.ExecuteNonQueryAsync();
        await using (var deletedCommand = connection.CreateCommand())
        {
            deletedCommand.CommandText = "CREATE TABLE IF NOT EXISTS DeletedTourRegistrations (Id INTEGER PRIMARY KEY, DeletedAt TEXT NOT NULL);";
            await deletedCommand.ExecuteNonQueryAsync();
        }
        await EnsurePassportArrivedValueColumnAsync(connection);
        await EnsureLastModifiedColumnAsync(connection);

        foreach (var (type, values) in DefaultOptions)
        {
            await using var existsCommand = connection.CreateCommand();
            existsCommand.CommandText = "SELECT EXISTS(SELECT 1 FROM AppSettings WHERE Type = $type);";
            existsCommand.Parameters.AddWithValue("$type", type);
            if (Convert.ToInt32(await existsCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture) != 0)
            {
                continue;
            }

            for (var index = 0; index < values.Length; index++)
            {
                await using var insertCommand = connection.CreateCommand();
                insertCommand.CommandText = """
                    INSERT INTO AppSettings (Type, Value, IsActive, SortOrder)
                    VALUES ($type, $value, 1, $sortOrder);
                    """;
                insertCommand.Parameters.AddWithValue("$type", type);
                insertCommand.Parameters.AddWithValue("$value", values[index]);
                insertCommand.Parameters.AddWithValue("$sortOrder", index + 1);
                await insertCommand.ExecuteNonQueryAsync();
            }
        }
    }

    public async Task<IReadOnlyList<RegistrationRecord>> GetAllAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.Id, r.PersonId, p.FirstName || ' ' || p.LastName,
                   r.TourType, r.Period, r.GroupType, r.DaysCount,
                   r.VisaRequest, r.TicketRequest, r.VisaStatusChoice, r.TicketStatusChoice,
                   r.PassportArrived, r.PassportArrivedValue, r.CargoRequest, r.RoomType, r.DepartureCity,
                   r.HasTransfer, r.TransferCity, r.District, r.TotalPrice, r.Currency,
                   r.PaidAmount, r.RemainingDebt, r.Reference, r.Status, r.CreatedDate, r.IsActive, r.LastModifiedUtc
            FROM TourRegistrations r
            INNER JOIN PersonRecords p ON p.Id = r.PersonId
            ORDER BY r.Id DESC;
            """;

        var registrations = new List<RegistrationRecord>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            registrations.Add(ReadRegistration(reader));
        }

        return registrations;
    }

    public async Task<IReadOnlyList<string>> GetLookupOptionsAsync(string type)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        var typeNames = type switch
        {
            "Tur Türü" => new[] { "TourType" },
            "Grup Türü" => new[] { "GroupType" },
            "Oda Türü" => new[] { "RoomType" },
            "Bilet Talebi" => new[] { "Bilet Talebi", "TicketType" },
            "Vize Talebi" => new[] { "Vize Talebi", "VisaType" },
            "Kargo Talebi" => new[] { "Kargo Talebi", "CargoType" },
            "Kaç Günlük Grup" => new[] { "Kaç Günlük Grup", "CountOfDays" },
            _ => new[] { type }
        };
        command.CommandText = $"""
            SELECT Value FROM AppSettings
            WHERE Type IN ({string.Join(", ", typeNames.Select((_, index) => $"$type{index}"))}) AND IsActive = 1
            ORDER BY SortOrder, Id;
            """;
        for (var index = 0; index < typeNames.Length; index++)
        {
            command.Parameters.AddWithValue($"$type{index}", typeNames[index]);
        }
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    public async Task<bool> HasDuplicateAsync(RegistrationRecord registration)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM TourRegistrations
                WHERE PersonId = $personId AND TourType = $tourType AND Period = $period
                  AND GroupType = $groupType AND DaysCount = $daysCount AND Id <> $id);
            """;
        AddKeyParameters(command, registration);
        command.Parameters.AddWithValue("$id", registration.Id);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) != 0;
    }

    public async Task<int> SaveAsync(RegistrationRecord registration, bool preserveLastModifiedUtc = false)
    {
        await using var connection = await OpenConnectionAsync();
        if (!preserveLastModifiedUtc || registration.LastModifiedUtc == default)
            registration.LastModifiedUtc = DateTime.UtcNow;
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        if (registration.Id <= 0)
        {
            command.CommandText = """
                INSERT INTO TourRegistrations (
                    PersonId, TourType, Period, GroupType, DaysCount, VisaRequest, TicketRequest,
                    VisaStatusChoice, TicketStatusChoice, PassportArrived, CargoRequest, RoomType,
                    PassportArrivedValue,
                    DepartureCity, HasTransfer, TransferCity, District, TotalPrice, Currency,
                    PaidAmount, RemainingDebt, Reference, Status, CreatedDate, IsActive, LastModifiedUtc)
                VALUES (
                    $personId, $tourType, $period, $groupType, $daysCount, $visaRequest, $ticketRequest,
                    $visaStatusChoice, $ticketStatusChoice, $passportArrived, $cargoRequest, $roomType,
                    $passportArrivedValue,
                    $departureCity, $hasTransfer, $transferCity, $district, $totalPrice, $currency,
                    $paidAmount, $remainingDebt, $reference, $status, $createdDate, $isActive, $lastModifiedUtc);
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE TourRegistrations SET
                    PersonId = $personId, TourType = $tourType, Period = $period,
                    GroupType = $groupType, DaysCount = $daysCount, VisaRequest = $visaRequest,
                    TicketRequest = $ticketRequest, VisaStatusChoice = $visaStatusChoice,
                    TicketStatusChoice = $ticketStatusChoice, PassportArrived = $passportArrived,
                    PassportArrivedValue = $passportArrivedValue,
                    CargoRequest = $cargoRequest, RoomType = $roomType, DepartureCity = $departureCity,
                    HasTransfer = $hasTransfer, TransferCity = $transferCity, District = $district,
                    TotalPrice = $totalPrice, Currency = $currency, PaidAmount = $paidAmount,
                    RemainingDebt = $remainingDebt, Reference = $reference, Status = $status,
                    IsActive = $isActive, LastModifiedUtc = $lastModifiedUtc
                WHERE Id = $id;
                """;
            command.Parameters.AddWithValue("$id", registration.Id);
        }

        AddRegistrationParameters(command, registration);
        await command.ExecuteNonQueryAsync();
        if (registration.Id <= 0)
        {
            await using var idCommand = connection.CreateCommand();
            idCommand.CommandText = "SELECT last_insert_rowid();";
            idCommand.Transaction = (SqliteTransaction)transaction;
            registration.Id = Convert.ToInt32(await idCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        await transaction.CommitAsync();
        return registration.Id;
    }

    public async Task<RegistrationRecord?> GetByIdAsync(int id)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, PersonId, '', TourType, Period, GroupType, DaysCount,
                   VisaRequest, TicketRequest, VisaStatusChoice, TicketStatusChoice,
                   PassportArrived, PassportArrivedValue, CargoRequest, RoomType, DepartureCity,
                   HasTransfer, TransferCity, District, TotalPrice, Currency,
                    PaidAmount, RemainingDebt, Reference, Status, CreatedDate, IsActive, LastModifiedUtc
            FROM TourRegistrations WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadRegistration(reader) : null;
    }

    public async Task DeleteAsync(int id)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var deletedCommand = connection.CreateCommand())
        {
            deletedCommand.Transaction = (SqliteTransaction)transaction;
            deletedCommand.CommandText = "INSERT INTO DeletedTourRegistrations (Id, DeletedAt) VALUES ($id, $deletedAt) ON CONFLICT(Id) DO UPDATE SET DeletedAt = excluded.DeletedAt;";
            deletedCommand.Parameters.AddWithValue("$id", id);
            deletedCommand.Parameters.AddWithValue("$deletedAt", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            await deletedCommand.ExecuteNonQueryAsync();
        }
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "DELETE FROM TourRegistrations WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    public async Task<IReadOnlyList<int>> GetDeletedIdsAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM DeletedTourRegistrations ORDER BY Id;";
        var ids = new List<int>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetInt32(0));
        return ids;
    }

    public async Task AcknowledgeDeletedAsync(IEnumerable<int> ids)
    {
        var deletedIds = ids.Distinct().ToArray();
        if (deletedIds.Length == 0) return;
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "DELETE FROM DeletedTourRegistrations WHERE Id = $id;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$id";
        command.Parameters.Add(parameter);
        foreach (var id in deletedIds)
        {
            parameter.Value = id;
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = await OpenSharedConnectionAsync();
        await CreateSchemaIfNeededAsync(connection);
        return connection;
    }

    private async Task<SqliteConnection> OpenSharedConnectionAsync()
    {
        await _personRepository.InitializeAsync();
        var databasePath = Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "toslite-mobile.db3");
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        }.ToString());
        await connection.OpenAsync();
        return connection;
    }

    private static async Task CreateSchemaIfNeededAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS TourRegistrations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PersonId INTEGER NOT NULL,
                TourType TEXT NOT NULL,
                Period TEXT NOT NULL,
                GroupType TEXT NOT NULL,
                DaysCount TEXT NOT NULL,
                VisaRequest TEXT NOT NULL,
                TicketRequest TEXT NOT NULL,
                VisaStatusChoice TEXT NOT NULL,
                TicketStatusChoice TEXT NOT NULL,
                PassportArrived INTEGER NULL,
                PassportArrivedValue TEXT NOT NULL DEFAULT '',
                CargoRequest TEXT NOT NULL,
                RoomType TEXT NOT NULL,
                DepartureCity TEXT NOT NULL,
                HasTransfer TEXT NOT NULL,
                TransferCity TEXT NOT NULL,
                District TEXT NOT NULL,
                TotalPrice REAL NOT NULL,
                Currency TEXT NOT NULL,
                PaidAmount REAL NOT NULL,
                RemainingDebt REAL NOT NULL,
                Reference TEXT NOT NULL,
                Status TEXT NOT NULL,
                CreatedDate TEXT NOT NULL,
                IsActive INTEGER NOT NULL,
                LastModifiedUtc TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.0000000Z',
                FOREIGN KEY (PersonId) REFERENCES PersonRecords(Id) ON DELETE RESTRICT,
                UNIQUE (PersonId, TourType, Period, GroupType, DaysCount)
            );
            CREATE INDEX IF NOT EXISTS IX_TourRegistrations_PersonId
                ON TourRegistrations (PersonId);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task EnsureLastModifiedColumnAsync(SqliteConnection connection)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA table_info(TourRegistrations);";
        await using var reader = await check.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            if (string.Equals(reader.GetString(1), "LastModifiedUtc", StringComparison.OrdinalIgnoreCase)) return;
        await reader.DisposeAsync();
        await using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE TourRegistrations ADD COLUMN LastModifiedUtc TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.0000000Z';";
        await alter.ExecuteNonQueryAsync();
    }

    private static async Task EnsurePassportArrivedValueColumnAsync(SqliteConnection connection)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA table_info(TourRegistrations);";
        await using var reader = await check.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), "PassportArrivedValue", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await reader.DisposeAsync();
        await using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE TourRegistrations ADD COLUMN PassportArrivedValue TEXT NOT NULL DEFAULT '';";
        await alter.ExecuteNonQueryAsync();
    }

    private static void AddKeyParameters(SqliteCommand command, RegistrationRecord registration)
    {
        command.Parameters.AddWithValue("$personId", registration.PersonId);
        command.Parameters.AddWithValue("$tourType", registration.TourType);
        command.Parameters.AddWithValue("$period", registration.Period);
        command.Parameters.AddWithValue("$groupType", registration.GroupType);
        command.Parameters.AddWithValue("$daysCount", registration.DaysCount);
    }

    private static void AddRegistrationParameters(SqliteCommand command, RegistrationRecord record)
    {
        AddKeyParameters(command, record);
        command.Parameters.AddWithValue("$visaRequest", record.VisaRequest);
        command.Parameters.AddWithValue("$ticketRequest", record.TicketRequest);
        command.Parameters.AddWithValue("$visaStatusChoice", record.VisaStatusChoice);
        command.Parameters.AddWithValue("$ticketStatusChoice", record.TicketStatusChoice);
        command.Parameters.AddWithValue("$passportArrived", record.PassportArrived.HasValue ? (object)record.PassportArrived.Value : DBNull.Value);
        command.Parameters.AddWithValue("$passportArrivedValue", record.PassportArrivedValue);
        command.Parameters.AddWithValue("$cargoRequest", record.CargoRequest);
        command.Parameters.AddWithValue("$roomType", record.RoomType);
        command.Parameters.AddWithValue("$departureCity", record.DepartureCity);
        command.Parameters.AddWithValue("$hasTransfer", record.HasTransfer);
        command.Parameters.AddWithValue("$transferCity", record.TransferCity);
        command.Parameters.AddWithValue("$district", record.District);
        command.Parameters.AddWithValue("$totalPrice", record.TotalPrice);
        command.Parameters.AddWithValue("$currency", record.Currency);
        command.Parameters.AddWithValue("$paidAmount", record.PaidAmount);
        command.Parameters.AddWithValue("$remainingDebt", record.RemainingDebt);
        command.Parameters.AddWithValue("$reference", record.Reference);
        command.Parameters.AddWithValue("$status", record.Status);
        command.Parameters.AddWithValue("$createdDate", record.CreatedDate.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$isActive", record.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("$lastModifiedUtc", record.LastModifiedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
    }

    private static RegistrationRecord ReadRegistration(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        PersonId = reader.GetInt32(1),
        PersonName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
        TourType = reader.GetString(3),
        Period = reader.GetString(4),
        GroupType = reader.GetString(5),
        DaysCount = reader.GetString(6),
        VisaRequest = reader.GetString(7),
        TicketRequest = reader.GetString(8),
        VisaStatusChoice = reader.GetString(9),
        TicketStatusChoice = reader.GetString(10),
        PassportArrived = reader.IsDBNull(11) ? null : reader.GetInt32(11),
        PassportArrivedValue = reader.GetString(12),
        CargoRequest = reader.GetString(13),
        RoomType = reader.GetString(14),
        DepartureCity = reader.GetString(15),
        HasTransfer = reader.GetString(16),
        TransferCity = reader.GetString(17),
        District = reader.GetString(18),
        TotalPrice = reader.GetDecimal(19),
        Currency = reader.GetString(20),
        PaidAmount = reader.GetDecimal(21),
        RemainingDebt = reader.GetDecimal(22),
        Reference = reader.GetString(23),
        Status = reader.GetString(24),
        CreatedDate = DateTime.Parse(reader.GetString(25), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        IsActive = reader.GetInt32(26) != 0,
        LastModifiedUtc = DateTime.Parse(reader.GetString(27), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
    };

    private static readonly Dictionary<string, string[]> DefaultOptions = new()
    {
        ["Dönem"] = CreatePeriods(),
        ["Vize Talebi"] = ["Vize Alacağız", "Vizeyi Kendisi Alacak"],
        ["Bilet Talebi"] = ["Biletini Alacağız", "Bileti Kendisi Alacak"],
        ["Vize Durumu"] = ["Vize Çıktı", "Vize Çıkmadı", "Vizesi Alınıyor", "Vizeyi Kendisi Alacak", "Pasaport Bekleniyor"],
        ["Bilet Durumu"] = ["Bileti geldi", "Bileti gelmedi", "Bileti Alınıyor", "Bileti Kendisi Alacak"],
        ["Pasaportu Geldi mi ?"] = ["Evet", "Hayır", "Pasaport Vermeyecek"],
        ["Kargo Talebi"] = ["Kargo Gönderilecek", "Kargo Almayacak", "Kargo Gönderildi", "Elden Teslim Alacak", "Elden Teslim Aldı"],
        ["RoomType"] = ["SINGLE", "DBL", "TWIN", "TRPL", "QUAD", "QUINT"],
        ["Para Birimi"] = ["Dolar $", "Euro €", "Pound £ (İngiliz Sterlini)", "Türk Lirası ₺", "Suudi Riyali (SAR)"],
        ["Aktarma Şehri"] = CreateProvinces(),
        ["Katılım Yapılacak Şehir"] = CreateProvinces()
    };

    private static string[] CreatePeriods()
    {
        var months = new[] { "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };
        var periods = new List<string>();
        var year = 2026;
        var month = 7;
        while (year < 2126 || (year == 2126 && month <= 12))
        {
            periods.Add($"{months[month - 1]} {year}");
            if (++month > 12)
            {
                month = 1;
                year++;
            }
        }

        return periods.ToArray();
    }

    private static string[] CreateProvinces() =>
    [
        "Adana", "Adıyaman", "Afyonkarahisar", "Ağrı", "Amasya", "Ankara", "Antalya", "Artvin",
        "Aydın", "Balıkesir", "Bilecik", "Bingöl", "Bitlis", "Bolu", "Burdur", "Bursa",
        "Çanakkale", "Çankırı", "Çorum", "Denizli", "Diyarbakır", "Edirne", "Elazığ", "Erzincan",
        "Erzurum", "Eskişehir", "Gaziantep", "Giresun", "Gümüşhane", "Hakkari", "Hatay", "Isparta",
        "Mersin", "İstanbul", "İzmir", "Kars", "Kastamonu", "Kayseri", "Kırklareli", "Kırşehir",
        "Kocaeli", "Konya", "Kütahya", "Malatya", "Manisa", "Kahramanmaraş", "Mardin", "Muğla",
        "Muş", "Nevşehir", "Niğde", "Ordu", "Rize", "Sakarya", "Samsun", "Siirt", "Sinop", "Sivas",
        "Tekirdağ", "Tokat", "Trabzon", "Tunceli", "Şanlıurfa", "Uşak", "Van", "Yozgat", "Zonguldak",
        "Aksaray", "Bayburt", "Karaman", "Kırıkkale", "Batman", "Şırnak", "Bartın", "Ardahan", "Iğdır",
        "Yalova", "Karabük", "Kilis", "Osmaniye", "Düzce"
    ];

}

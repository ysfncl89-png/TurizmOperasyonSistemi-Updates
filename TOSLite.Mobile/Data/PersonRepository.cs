using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Maui.Storage;
using TOSLite.Mobile.Models;

namespace TOSLite.Mobile.Data;

public sealed class PersonRepository
{
    private static readonly SemaphoreSlim InitializationGate = new(1, 1);
    private static bool _sqliteInitialized;
    private static bool _databaseInitialized;

    private readonly string _connectionString;

    public PersonRepository()
    {
        var databasePath = Path.Combine(FileSystem.AppDataDirectory, "toslite-mobile.db3");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public async Task InitializeAsync()
    {
        if (_databaseInitialized)
        {
            return;
        }

        await InitializationGate.WaitAsync();
        try
        {
            if (_databaseInitialized)
            {
                return;
            }

            if (!_sqliteInitialized)
            {
                SQLitePCL.Batteries_V2.Init();
                _sqliteInitialized = true;
            }

            Directory.CreateDirectory(FileSystem.AppDataDirectory);
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS PersonRecords (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ClientId TEXT NOT NULL UNIQUE,
                    DesktopPersonId INTEGER NULL,
                    FirstName TEXT NOT NULL,
                    LastName TEXT NOT NULL,
                    TcKimlikNo TEXT NOT NULL,
                    Gender TEXT NOT NULL,
                    BirthDate TEXT NOT NULL,
                    PhoneNumber TEXT NOT NULL,
                    Address TEXT NOT NULL,
                    Aciklama TEXT NULL,
                    PassportType TEXT NOT NULL,
                    PassportNumber TEXT NOT NULL,
                    PassportStartDate TEXT NULL,
                    PassportEndDate TEXT NULL,
                    BloodType TEXT NOT NULL,
                    HealthStatus TEXT NOT NULL,
                    VaccineStatus TEXT NOT NULL,
                    IsActive INTEGER NOT NULL,
                    AgeRange TEXT NOT NULL,
                    Nationality TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_PersonRecords_TcKimlikNo
                    ON PersonRecords (TcKimlikNo);
                CREATE TABLE IF NOT EXISTS AppSettings (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Type TEXT NOT NULL,
                    Value TEXT NOT NULL,
                    IsActive INTEGER NOT NULL,
                    SortOrder INTEGER NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
            await SeedDefaultLookupOptionsAsync(connection);
            _databaseInitialized = true;
        }
        finally
        {
            InitializationGate.Release();
        }
    }

    public async Task<IReadOnlyList<PersonRecord>> GetAllAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ClientId, DesktopPersonId, FirstName, LastName, TcKimlikNo,
                   Gender, BirthDate, PhoneNumber, Address, Aciklama, PassportType,
                   PassportNumber, PassportStartDate, PassportEndDate, BloodType,
                   HealthStatus, VaccineStatus, IsActive, AgeRange, Nationality
            FROM PersonRecords
            ORDER BY Id DESC;
            """;

        var people = new List<PersonRecord>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            people.Add(ReadPerson(reader));
        }

        return people;
    }

    public async Task<IReadOnlyList<string>> GetLookupOptionsAsync(string type)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Value
            FROM AppSettings
            WHERE Type = $type AND IsActive = 1
            ORDER BY SortOrder, Id;
            """;
        command.Parameters.AddWithValue("$type", type);

        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    public async Task<PersonRecord?> GetByIdAsync(int id)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ClientId, DesktopPersonId, FirstName, LastName, TcKimlikNo,
                   Gender, BirthDate, PhoneNumber, Address, Aciklama, PassportType,
                   PassportNumber, PassportStartDate, PassportEndDate, BloodType,
                   HealthStatus, VaccineStatus, IsActive, AgeRange, Nationality
            FROM PersonRecords
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadPerson(reader) : null;
    }

    public async Task<bool> HasDuplicateTurkishIdAsync(string tcKimlikNo, int? exceptId = null)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = exceptId.HasValue
            ? "SELECT EXISTS(SELECT 1 FROM PersonRecords WHERE TcKimlikNo = $tc AND Id <> $exceptId);"
            : "SELECT EXISTS(SELECT 1 FROM PersonRecords WHERE TcKimlikNo = $tc);";
        command.Parameters.AddWithValue("$tc", tcKimlikNo);
        if (exceptId.HasValue)
        {
            command.Parameters.AddWithValue("$exceptId", exceptId.Value);
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) != 0;
    }

    public async Task<int> SaveAsync(PersonRecord person)
    {
        if (string.IsNullOrWhiteSpace(person.ClientId))
        {
            person.ClientId = Guid.NewGuid().ToString("N");
        }

        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();

        if (person.Id <= 0)
        {
            command.CommandText = """
                INSERT INTO PersonRecords (
                    ClientId, DesktopPersonId, FirstName, LastName, TcKimlikNo, Gender,
                    BirthDate, PhoneNumber, Address, Aciklama, PassportType, PassportNumber,
                    PassportStartDate, PassportEndDate, BloodType, HealthStatus, VaccineStatus,
                    IsActive, AgeRange, Nationality
                ) VALUES (
                    $clientId, $desktopPersonId, $firstName, $lastName, $tcKimlikNo, $gender,
                    $birthDate, $phoneNumber, $address, $aciklama, $passportType, $passportNumber,
                    $passportStartDate, $passportEndDate, $bloodType, $healthStatus, $vaccineStatus,
                    $isActive, $ageRange, $nationality
                );
                """;
            AddPersonParameters(command, person);
            await command.ExecuteNonQueryAsync();

            await using var idCommand = connection.CreateCommand();
            idCommand.CommandText = "SELECT last_insert_rowid();";
            person.Id = Convert.ToInt32(await idCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            return person.Id;
        }

        command.CommandText = """
            UPDATE PersonRecords SET
                ClientId = $clientId,
                DesktopPersonId = $desktopPersonId,
                FirstName = $firstName,
                LastName = $lastName,
                TcKimlikNo = $tcKimlikNo,
                Gender = $gender,
                BirthDate = $birthDate,
                PhoneNumber = $phoneNumber,
                Address = $address,
                Aciklama = $aciklama,
                PassportType = $passportType,
                PassportNumber = $passportNumber,
                PassportStartDate = $passportStartDate,
                PassportEndDate = $passportEndDate,
                BloodType = $bloodType,
                HealthStatus = $healthStatus,
                VaccineStatus = $vaccineStatus,
                IsActive = $isActive,
                AgeRange = $ageRange,
                Nationality = $nationality
            WHERE Id = $id;
            """;
        AddPersonParameters(command, person);
        command.Parameters.AddWithValue("$id", person.Id);
        await command.ExecuteNonQueryAsync();
        return person.Id;
    }

    public async Task DeleteAsync(int id)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM PersonRecords WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        await InitializeAsync();
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task SeedDefaultLookupOptionsAsync(SqliteConnection connection)
    {
        var defaults = new Dictionary<string, string[]>
        {
            ["Cinsiyet"] = ["Erkek", "Kadın"],
            ["Yaş Aralığı"] = ["Bebek", "Çocuk", "Yetişkin"],
            ["Kan Grubu"] = ["A+", "A-", "B+", "B-", "AB+", "AB-", "0+", "0-"],
            ["Pasaport Türü"] = ["Bordo", "Yeşil", "Gri", "Siyah"],
            ["Uyruk"] = ["TÜRKİYE", "YABANCI"]
        };

        foreach (var (type, values) in defaults)
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

    private static void AddPersonParameters(SqliteCommand command, PersonRecord person)
    {
        command.Parameters.AddWithValue("$clientId", person.ClientId);
        AddNullableParameter(command, "$desktopPersonId", person.DesktopPersonId);
        command.Parameters.AddWithValue("$firstName", person.FirstName);
        command.Parameters.AddWithValue("$lastName", person.LastName);
        command.Parameters.AddWithValue("$tcKimlikNo", person.TcKimlikNo);
        command.Parameters.AddWithValue("$gender", person.Gender);
        command.Parameters.AddWithValue("$birthDate", FormatDate(person.BirthDate));
        command.Parameters.AddWithValue("$phoneNumber", person.PhoneNumber);
        command.Parameters.AddWithValue("$address", person.Address);
        AddNullableParameter(command, "$aciklama", person.Aciklama);
        command.Parameters.AddWithValue("$passportType", person.PassportType);
        command.Parameters.AddWithValue("$passportNumber", person.PassportNumber);
        AddNullableParameter(command, "$passportStartDate", person.PassportStartDate.HasValue ? FormatDate(person.PassportStartDate.Value) : null);
        AddNullableParameter(command, "$passportEndDate", person.PassportEndDate.HasValue ? FormatDate(person.PassportEndDate.Value) : null);
        command.Parameters.AddWithValue("$bloodType", person.BloodType);
        command.Parameters.AddWithValue("$healthStatus", person.HealthStatus);
        command.Parameters.AddWithValue("$vaccineStatus", person.VaccineStatus);
        command.Parameters.AddWithValue("$isActive", person.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("$ageRange", person.AgeRange);
        AddNullableParameter(command, "$nationality", person.Nationality);
    }

    private static void AddNullableParameter(SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static string FormatDate(DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTime ParseDate(SqliteDataReader reader, int ordinal) =>
        DateTime.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static DateTime? ParseNullableDate(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ParseDate(reader, ordinal);

    private static string ReadString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);

    private static PersonRecord ReadPerson(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        ClientId = reader.GetString(1),
        DesktopPersonId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
        FirstName = ReadString(reader, 3),
        LastName = ReadString(reader, 4),
        TcKimlikNo = ReadString(reader, 5),
        Gender = ReadString(reader, 6),
        BirthDate = ParseDate(reader, 7),
        PhoneNumber = ReadString(reader, 8),
        Address = ReadString(reader, 9),
        Aciklama = reader.IsDBNull(10) ? null : reader.GetString(10),
        PassportType = ReadString(reader, 11),
        PassportNumber = ReadString(reader, 12),
        PassportStartDate = ParseNullableDate(reader, 13),
        PassportEndDate = ParseNullableDate(reader, 14),
        BloodType = ReadString(reader, 15),
        HealthStatus = ReadString(reader, 16),
        VaccineStatus = ReadString(reader, 17),
        IsActive = reader.GetInt32(18) != 0,
        AgeRange = ReadString(reader, 19),
        Nationality = reader.IsDBNull(20) ? null : reader.GetString(20)
    };
}

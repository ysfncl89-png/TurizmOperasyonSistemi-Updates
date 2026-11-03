using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Maui.Storage;
using TOSLite.Mobile.Models;

namespace TOSLite.Mobile.Data;

public sealed class SettingsRepository
{
    private readonly PersonRepository _personRepository;
    private readonly RegistrationRepository _registrationRepository;

    public SettingsRepository(PersonRepository personRepository, RegistrationRepository registrationRepository)
    {
        _personRepository = personRepository;
        _registrationRepository = registrationRepository;
    }

    public Task InitializeAsync() => _registrationRepository.InitializeAsync();

    public async Task<IReadOnlyList<SettingOption>> GetOptionsAsync(string type, bool includeInactive = true)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = includeInactive
            ? "SELECT Id, Type, Value, IsActive, SortOrder FROM AppSettings WHERE Type = $type ORDER BY SortOrder, Id;"
            : "SELECT Id, Type, Value, IsActive, SortOrder FROM AppSettings WHERE Type = $type AND IsActive = 1 ORDER BY SortOrder, Id;";
        command.Parameters.AddWithValue("$type", type);
        var result = new List<SettingOption>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new SettingOption
            {
                Id = reader.GetInt32(0),
                Type = reader.GetString(1),
                Value = reader.GetString(2),
                IsActive = reader.GetInt32(3) != 0,
                SortOrder = reader.GetInt32(4)
            });
        }
        return result;
    }

    public async Task<IReadOnlyList<SettingOption>> GetAllAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Type, Value, IsActive, SortOrder FROM AppSettings ORDER BY Type, SortOrder, Id;";
        var result = new List<SettingOption>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new SettingOption
            {
                Id = reader.GetInt32(0),
                Type = reader.GetString(1),
                Value = reader.GetString(2),
                IsActive = reader.GetInt32(3) != 0,
                SortOrder = reader.GetInt32(4)
            });
        }
        return result;
    }

    public async Task<int> SaveAsync(SettingOption option)
    {
        var value = option.Value.Trim();
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Ayar değeri boş olamaz.", nameof(option));

        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var duplicateCommand = connection.CreateCommand();
        duplicateCommand.Transaction = (SqliteTransaction)transaction;
        duplicateCommand.CommandText = "SELECT EXISTS(SELECT 1 FROM AppSettings WHERE Type = $type AND Value = $value COLLATE NOCASE AND Id <> $id);";
        duplicateCommand.Parameters.AddWithValue("$type", option.Type);
        duplicateCommand.Parameters.AddWithValue("$value", value);
        duplicateCommand.Parameters.AddWithValue("$id", option.Id);
        if (Convert.ToInt32(await duplicateCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture) != 0)
            throw new InvalidOperationException("Bu seçenek zaten mevcut.");

        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        if (option.Id > 0)
        {
            command.CommandText = "UPDATE AppSettings SET Value = $value, IsActive = $active, SortOrder = $sortOrder WHERE Id = $id AND Type = $type;";
            command.Parameters.AddWithValue("$id", option.Id);
        }
        else
        {
            command.CommandText = "INSERT INTO AppSettings (Type, Value, IsActive, SortOrder) VALUES ($type, $value, $active, $sortOrder);";
        }
        command.Parameters.AddWithValue("$type", option.Type);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$active", option.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("$sortOrder", option.SortOrder);
        await command.ExecuteNonQueryAsync();

        if (option.Id <= 0)
        {
            await using var idCommand = connection.CreateCommand();
            idCommand.Transaction = (SqliteTransaction)transaction;
            idCommand.CommandText = "SELECT last_insert_rowid();";
            option.Id = Convert.ToInt32(await idCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }
        await transaction.CommitAsync();
        option.Value = value;
        return option.Id;
    }

    public async Task MergeFromDesktopAsync(IReadOnlyCollection<SettingOption> desktopOptions)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var option in desktopOptions)
        {
            if (string.IsNullOrWhiteSpace(option.Type) || string.IsNullOrWhiteSpace(option.Value)) continue;

            await using var findCommand = connection.CreateCommand();
            findCommand.Transaction = (SqliteTransaction)transaction;
            findCommand.CommandText = "SELECT Id FROM AppSettings WHERE Type = $type COLLATE NOCASE AND Value = $value COLLATE NOCASE LIMIT 1;";
            findCommand.Parameters.AddWithValue("$type", option.Type);
            findCommand.Parameters.AddWithValue("$value", option.Value.Trim());
            var existingId = await findCommand.ExecuteScalarAsync();

            await using var saveCommand = connection.CreateCommand();
            saveCommand.Transaction = (SqliteTransaction)transaction;
            if (existingId is null or DBNull)
            {
                saveCommand.CommandText = "INSERT INTO AppSettings (Type, Value, IsActive, SortOrder) VALUES ($type, $value, $active, $sortOrder);";
            }
            else
            {
                saveCommand.CommandText = "UPDATE AppSettings SET Type = $type, Value = $value, IsActive = $active, SortOrder = $sortOrder WHERE Id = $id;";
                saveCommand.Parameters.AddWithValue("$id", Convert.ToInt32(existingId, CultureInfo.InvariantCulture));
            }
            saveCommand.Parameters.AddWithValue("$type", option.Type);
            saveCommand.Parameters.AddWithValue("$value", option.Value.Trim());
            saveCommand.Parameters.AddWithValue("$active", option.IsActive ? 1 : 0);
            saveCommand.Parameters.AddWithValue("$sortOrder", option.SortOrder);
            await saveCommand.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    public async Task SetActiveAsync(int id, bool isActive)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE AppSettings SET IsActive = $active WHERE Id = $id;";
        command.Parameters.AddWithValue("$active", isActive ? 1 : 0);
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM AppSettings WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        await _personRepository.InitializeAsync();
        var path = Path.Combine(FileSystem.AppDataDirectory, "toslite-mobile.db3");
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());
        await connection.OpenAsync();
        return connection;
    }
}

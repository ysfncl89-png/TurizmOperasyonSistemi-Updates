using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Net.Http;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;
using TOSLite.Mobile.Data;
using TOSLite.Mobile.Models;

namespace TOSLite.Mobile.Services;

public sealed class MobileSyncService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly PersonRepository _persons;
    private readonly RegistrationRepository _registrations;
    private readonly SettingsRepository _settings;
    private readonly RoomAllocationRepository _rooming;
    private readonly PaymentRepository _payments;
    private readonly CargoQueueRepository _cargoQueue;
    private readonly string _connectionString;

    public MobileSyncService(PersonRepository persons, RegistrationRepository registrations, SettingsRepository settings, RoomAllocationRepository rooming, PaymentRepository payments, CargoQueueRepository cargoQueue)
    {
        _persons = persons;
        _registrations = registrations;
        _settings = settings;
        _rooming = rooming;
        _payments = payments;
        _cargoQueue = cargoQueue;
        var path = Path.Combine(FileSystem.AppDataDirectory, "toslite-mobile.db3");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
    }

    public bool IsPaired => Preferences.Default.Get("MobileSync.Paired", false) && Preferences.Default.ContainsKey("MobileSync.Url");
    public string PairedAddress => Preferences.Default.Get("MobileSync.Url", string.Empty);
    public bool IsAwaitingDesktopSync => Preferences.Default.Get("MobileSync.AwaitingDesktopSync", false);

    public async Task PairAsync(string qrText, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Deserialize<PairingPayload>(qrText, JsonOptions) ?? throw new InvalidOperationException("QR kod içeriği okunamadı.");
        if (payload.Version != 1 || !Uri.TryCreate(payload.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(payload.PairingCode) || string.IsNullOrWhiteSpace(payload.CertificateFingerprint))
            throw new InvalidOperationException("QR kodu geçerli bir TOS Lite eşleştirme kodu değil.");

        using var client = CreatePinnedClient(payload.CertificateFingerprint);
        var deviceId = GetDeviceId();
        var request = new PairRequest { PairingCode = payload.PairingCode, DeviceId = deviceId, DeviceName = DeviceInfo.Name };
        using var response = await client.PostAsJsonAsync(new Uri(uri, "pair"), request, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<PairResponse>(JsonOptions, cancellationToken) ?? throw new InvalidOperationException("Masaüstü eşleştirme yanıtı boş.");
        Preferences.Default.Set("MobileSync.Url", payload.Url);
        await SecureStorage.Default.SetAsync("MobileSync.Token", body.Token);
        Preferences.Default.Set("MobileSync.Fingerprint", payload.CertificateFingerprint);
        Preferences.Default.Set("MobileSync.DeviceId", deviceId);
        Preferences.Default.Set("MobileSync.Paired", true);
        Preferences.Default.Set("MobileSync.AwaitingDesktopSync", false);
    }

    public async Task<MobileSyncResultDto> SyncAsync(CancellationToken cancellationToken = default)
    {
        if (!IsPaired) throw new InvalidOperationException("Önce masaüstündeki QR kodu okutarak eşleştirin.");
        await _persons.InitializeAsync();
        await _registrations.InitializeAsync();
        await _settings.InitializeAsync();
        await _rooming.InitializeAsync();
        await _payments.InitializeAsync();
        await _cargoQueue.InitializeAsync();
        var package = await CreatePackageAsync(cancellationToken);
        var fingerprint = Preferences.Default.Get("MobileSync.Fingerprint", string.Empty);
        using var client = CreatePinnedClient(fingerprint);
        using var request = new HttpRequestMessage(HttpMethod.Post, Preferences.Default.Get("MobileSync.Url", string.Empty) + "sync");
        request.Version = System.Net.HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
        var token = await SecureStorage.Default.GetAsync("MobileSync.Token") ?? string.Empty;
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(package, JsonOptions));
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var serverError = TryReadServerError(responseBody);
                throw new InvalidOperationException(serverError is null
                    ? $"Masaüstü eşitleme isteğini kabul etmedi (HTTP {(int)response.StatusCode}). QR kodunu yeniden okutun ve masaüstü penceresindeki durumu kontrol edin."
                    : $"Masaüstü eşitleme hatası: {serverError}");
            }
            var result = JsonSerializer.Deserialize<MobileSyncResultDto>(responseBody, JsonOptions)
                ?? throw new InvalidOperationException("Masaüstünden boş eşitleme yanıtı geldi. Masaüstü raporunu kontrol edin.");
            Preferences.Default.Set("MobileSync.AwaitingDesktopSync", true);
            return result;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("Masaüstüne bağlanılamadı. Masaüstü eşitleme penceresini açık tutun ve telefon ile bilgisayarın aynı Wi-Fi ağına bağlı olduğundan emin olun. Windows izin penceresi çıkarsa TOS Lite için İzin Ver'i seçin.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Masaüstünden zamanında yanıt alınamadı. Masaüstü eşitleme penceresinin açık olduğunu ve aynı Wi-Fi ağında olduğunuzu kontrol edin.", ex);
        }
    }

    public async Task<MobileSyncResultDto> SyncFromDesktopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsPaired) throw new InvalidOperationException("Önce masaüstündeki QR kodu okutarak eşleştirin.");
        if (!IsAwaitingDesktopSync) throw new InvalidOperationException("Önce Mobilden Masaüstü Programa Eşitle adımını tamamlayın.");

        var fingerprint = Preferences.Default.Get("MobileSync.Fingerprint", string.Empty);
        using var client = CreatePinnedClient(fingerprint);
        using var request = new HttpRequestMessage(HttpMethod.Post, Preferences.Default.Get("MobileSync.Url", string.Empty) + "download");
        request.Version = System.Net.HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
        var token = await SecureStorage.Default.GetAsync("MobileSync.Token") ?? string.Empty;
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new MobileSyncDeviceRequest
        {
            DeviceId = Preferences.Default.Get("MobileSync.DeviceId", string.Empty)
        }, options: JsonOptions);

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var serverError = TryReadServerError(responseBody);
                throw new InvalidOperationException(serverError is null
                    ? $"Masaüstü verileri alınamadı (HTTP {(int)response.StatusCode})."
                    : serverError);
            }
            var result = JsonSerializer.Deserialize<MobileSyncResultDto>(responseBody, JsonOptions)
                ?? throw new InvalidOperationException("Masaüstünden boş veri paketi geldi.");
            await ApplyDesktopSnapshotAsync(result, cancellationToken, CompleteDesktopSyncAsync);
            Preferences.Default.Set("MobileSync.AwaitingDesktopSync", false);
            return result;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("Masaüstüne bağlanılamadı. Masaüstü eşitleme penceresini açık tutun ve aynı Wi-Fi ağında olduğunuzu kontrol edin.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Masaüstünden zamanında yanıt alınamadı. Masaüstü eşitleme penceresinin açık olduğunu kontrol edin.", ex);
        }
    }

    private async Task CompleteDesktopSyncAsync(CancellationToken cancellationToken)
    {
        using var client = CreatePinnedClient(Preferences.Default.Get("MobileSync.Fingerprint", string.Empty));
        using var request = new HttpRequestMessage(HttpMethod.Post, Preferences.Default.Get("MobileSync.Url", string.Empty) + "complete");
        request.Version = System.Net.HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
        var token = await SecureStorage.Default.GetAsync("MobileSync.Token") ?? string.Empty;
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new MobileSyncDeviceRequest
        {
            DeviceId = Preferences.Default.Get("MobileSync.DeviceId", string.Empty)
        }, options: JsonOptions);
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Mobil veriler uygulandı ancak masaüstü tamamlandı onayı alınamadı.");
    }

    private async Task ApplyDesktopStateAsync(MobileSyncResultDto result, CancellationToken cancellationToken)
    {
        var localPeople = (await _persons.GetAllAsync()).ToList();
        foreach (var deletedPerson in result.DeletedPersons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var local = localPeople.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(deletedPerson.ClientId) && x.ClientId == deletedPerson.ClientId ||
                deletedPerson.DesktopPersonId.HasValue && x.DesktopPersonId == deletedPerson.DesktopPersonId ||
                deletedPerson.LocalId > 0 && x.Id == deletedPerson.LocalId);
            if (local is null && !string.IsNullOrWhiteSpace(deletedPerson.TcKimlikNo))
            {
                var digits = new string(deletedPerson.TcKimlikNo.Where(char.IsDigit).ToArray());
                var matches = localPeople.Where(x => new string((x.TcKimlikNo ?? string.Empty).Where(char.IsDigit).ToArray()) == digits).Take(2).ToList();
                if (matches.Count == 1) local = matches[0];
            }
            if (local is null) continue;
            await _persons.DeleteSyncedAsync(local.Id);
            localPeople.Remove(local);
        }
        foreach (var desktopPerson in result.Persons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (desktopPerson.IsDeleted) continue;
            var local = localPeople.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(desktopPerson.ClientId) && x.ClientId == desktopPerson.ClientId ||
                desktopPerson.DesktopPersonId.HasValue && x.DesktopPersonId == desktopPerson.DesktopPersonId);
            var digits = new string((desktopPerson.TcKimlikNo ?? string.Empty).Where(char.IsDigit).ToArray());
            if (local is null && digits.Length == 11)
            {
                var matchingPeople = localPeople.Where(x => new string((x.TcKimlikNo ?? string.Empty).Where(char.IsDigit).ToArray()) == digits).Take(2).ToList();
                if (matchingPeople.Count == 1) local = matchingPeople[0];
            }
            var isNew = local is null;
            if (local is null)
            {
                local = new PersonRecord
                {
                    ClientId = string.IsNullOrWhiteSpace(desktopPerson.ClientId) ? Guid.NewGuid().ToString("N") : desktopPerson.ClientId,
                    DesktopPersonId = desktopPerson.DesktopPersonId
                };
            }
            var linkChanged = desktopPerson.DesktopPersonId.HasValue && local.DesktopPersonId != desktopPerson.DesktopPersonId;
            local.DesktopPersonId = desktopPerson.DesktopPersonId;
            var inactiveWins = !desktopPerson.IsActive && local.IsActive;
            var applyDesktopValues = isNew || desktopPerson.LastModifiedUtc >= local.LastModifiedUtc || inactiveWins;
            if (applyDesktopValues)
            {
                local.FirstName = desktopPerson.FirstName;
                local.LastName = desktopPerson.LastName;
                local.TcKimlikNo = desktopPerson.TcKimlikNo ?? string.Empty;
                local.Gender = desktopPerson.Gender;
                local.BirthDate = desktopPerson.BirthDate;
                local.PhoneNumber = desktopPerson.PhoneNumber;
                local.Address = desktopPerson.Address;
                local.Aciklama = desktopPerson.Aciklama;
                local.PassportType = desktopPerson.PassportType;
                local.PassportNumber = desktopPerson.PassportNumber;
                local.PassportStartDate = desktopPerson.PassportStartDate;
                local.PassportEndDate = desktopPerson.PassportEndDate;
                local.BloodType = desktopPerson.BloodType;
                local.HealthStatus = desktopPerson.HealthStatus;
                local.VaccineStatus = desktopPerson.VaccineStatus;
                local.IsActive = desktopPerson.IsActive;
                local.AgeRange = desktopPerson.AgeRange;
                local.Nationality = desktopPerson.Nationality;
                local.LastModifiedUtc = desktopPerson.LastModifiedUtc > local.LastModifiedUtc
                    ? desktopPerson.LastModifiedUtc
                    : local.LastModifiedUtc;
            }
            if (isNew || linkChanged || applyDesktopValues)
            {
                await _persons.SaveAsync(local, preserveLastModifiedUtc: true);
                if (isNew) localPeople.Add(local);
            }
        }

        localPeople = (await _persons.GetAllAsync()).ToList();
        foreach (var desktopRegistration in result.Registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var person = localPeople.FirstOrDefault(x => x.DesktopPersonId == desktopRegistration.DesktopPersonId ||
                desktopRegistration.PersonId > 0 && x.Id == desktopRegistration.PersonId);
            if (person is null) continue;
            var localRegistrations = await _registrations.GetAllAsync();
            var local = (desktopRegistration.Id > 0 ? localRegistrations.FirstOrDefault(x => x.Id == desktopRegistration.Id) : null) ?? localRegistrations.FirstOrDefault(x =>
                x.PersonId == person.Id && x.TourType == desktopRegistration.TourType && x.Period == desktopRegistration.Period &&
                x.GroupType == desktopRegistration.GroupType && x.DaysCount == desktopRegistration.DaysCount);
            if (local is null) local = new RegistrationRecord { PersonId = person.Id };
            var applyDesktopValues = local.Id <= 0 || desktopRegistration.LastModifiedUtc >= local.LastModifiedUtc;
            if (applyDesktopValues)
            {
                if (local.Id > 0 && (local.PersonId != person.Id || local.TourType != desktopRegistration.TourType ||
                    local.Period != desktopRegistration.Period || local.GroupType != desktopRegistration.GroupType ||
                    local.DaysCount != desktopRegistration.DaysCount) && await _registrations.HasDuplicateAsync(new RegistrationRecord
                    {
                        Id = local.Id,
                        PersonId = person.Id,
                        TourType = desktopRegistration.TourType,
                        Period = desktopRegistration.Period,
                        GroupType = desktopRegistration.GroupType,
                        DaysCount = desktopRegistration.DaysCount
                    }))
                    continue;
                local.PersonId = person.Id;
                local.DesktopRegistrationId = desktopRegistration.DesktopRegistrationId ?? desktopRegistration.Id;
                local.TourType = desktopRegistration.TourType;
                local.Period = desktopRegistration.Period;
                local.GroupType = desktopRegistration.GroupType;
                local.DaysCount = desktopRegistration.DaysCount;
                local.VisaRequest = desktopRegistration.VisaRequest;
                local.TicketRequest = desktopRegistration.TicketRequest;
                local.VisaStatusChoice = desktopRegistration.VisaStatusChoice;
                local.TicketStatusChoice = desktopRegistration.TicketStatusChoice;
                local.PassportArrived = desktopRegistration.PassportArrived;
                local.PassportArrivedValue = desktopRegistration.PassportArrivedValue;
                local.CargoRequest = desktopRegistration.CargoRequest;
                local.RoomType = desktopRegistration.RoomType;
                local.DepartureCity = desktopRegistration.DepartureCity;
                local.HasTransfer = desktopRegistration.HasTransfer;
                local.TransferCity = desktopRegistration.TransferCity;
                local.District = desktopRegistration.District;
                local.TotalPrice = desktopRegistration.TotalPrice;
                local.Currency = desktopRegistration.Currency;
                local.PaidAmount = desktopRegistration.PaidAmount;
                local.RemainingDebt = desktopRegistration.RemainingDebt;
                local.Reference = desktopRegistration.Reference;
                local.Status = desktopRegistration.Status;
                local.CreatedDate = desktopRegistration.CreatedDate;
                local.IsActive = desktopRegistration.IsActive;
                local.LastModifiedUtc = desktopRegistration.LastModifiedUtc;
                await _registrations.SaveAsync(local, preserveLastModifiedUtc: true);
            }
        }
        localPeople = (await _persons.GetAllAsync()).ToList();
        var localIdsByDesktopId = localPeople
            .Where(x => x.DesktopPersonId.HasValue)
            .ToDictionary(x => x.DesktopPersonId!.Value, x => x.Id);
        foreach (var room in result.Rooming)
        {
            var passengerDesktopIds = room.PassengerDesktopIds.Count > 0
                ? room.PassengerDesktopIds
                : result.Persons.Where(x => room.PassengerIds.Contains(x.LocalId) && x.DesktopPersonId.HasValue)
                    .Select(x => x.DesktopPersonId!.Value).ToList();
            room.PassengerIds = passengerDesktopIds
                .Where(localIdsByDesktopId.ContainsKey)
                .Select(id => localIdsByDesktopId[id])
                .Distinct()
                .ToList();
        }
        await _rooming.ApplyDesktopStateAsync(result.Rooming);
        var localRegistrationIds = await _registrations.GetLocalIdsByDesktopIdAsync();
        var payments = result.Payments.Select(x => new PaymentRecord
        {
            Id = x.DesktopPaymentId ?? x.Id, SyncId = x.SyncId, DesktopPaymentId = x.DesktopPaymentId,
            RegistrationId = x.DesktopRegistrationId ?? x.RegistrationId,
            CreatedAt = x.CreatedAt, OriginalAmount = x.OriginalAmount, OriginalCurrency = x.OriginalCurrency,
            AmountInRegistrationCurrency = x.AmountInRegistrationCurrency, Rate = x.Rate, Note = x.Note,
            UploadedDocumentPath = x.UploadedDocumentPath, PaymentMethod = x.PaymentMethod,
            LastModifiedUtc = x.LastModifiedUtc, IsDeleted = x.IsDeleted
        }).ToList();
        await _payments.ApplyDesktopStateAsync(payments, localRegistrationIds);
        var cargoItems = result.CargoQueue.Select(x => new CargoQueueRecord
        {
            Id = x.Id, SyncId = x.SyncId, DesktopQueueId = x.DesktopQueueId,
            RegistrationId = x.DesktopRegistrationId ?? x.RegistrationId,
            DesktopRegistrationId = x.DesktopRegistrationId,
            FullName = x.FullName, Tc = x.Tc, Phone = x.Phone, Address = x.Address,
            TourType = x.TourType, Period = x.Period, GroupType = x.GroupType, DaysCount = x.DaysCount,
            Quantity = x.Quantity, AddedAt = x.AddedAt, Printed = x.Printed,
            LastModifiedUtc = x.LastModifiedUtc, IsDeleted = x.IsDeleted
        }).ToList();
        await _cargoQueue.ApplyDesktopStateAsync(cargoItems, localRegistrationIds);
    }

    private async Task ApplyDesktopSnapshotAsync(MobileSyncResultDto result, CancellationToken cancellationToken, Func<CancellationToken, Task> confirmCompletion)
    {
        var backupPath = Path.Combine(FileSystem.AppDataDirectory, $"toslite-mobile-sync-backup-{Guid.NewGuid():N}.db3");
        await CreateDatabaseBackupAsync(backupPath, cancellationToken);
        try
        {
            await ClearSyncedDataAsync(cancellationToken);
            await ApplyDesktopStateAsync(result, cancellationToken);
            await _settings.MergeFromDesktopAsync(result.Settings.Select(x => new SettingOption
            {
                Type = ToMobileSettingType(x.Type),
                Value = x.Value,
                IsActive = x.IsActive,
                SortOrder = x.SortOrder
            }).ToList());
            await _registrations.AcknowledgeDeletedAsync(result.AcknowledgedDeletedRegistrationIds);
            await VerifyDesktopSnapshotAsync(result, cancellationToken);
            await confirmCompletion(cancellationToken);
            File.Delete(backupPath);
        }
        catch (Exception applyException)
        {
            try
            {
                await RestoreDatabaseBackupAsync(backupPath, CancellationToken.None);
                File.Delete(backupPath);
            }
            catch (Exception restoreException)
            {
                throw new InvalidOperationException(
                    $"Masaüstünden mobil uygulamaya eşitleme başarısız oldu ve otomatik geri yükleme tamamlanamadı. Yedek korunuyor: {backupPath}",
                    new AggregateException(applyException, restoreException));
            }

            throw new InvalidOperationException("Masaüstünden mobil uygulamaya eşitleme başarısız oldu. Önceki mobil veriler geri yüklendi.", applyException);
        }
    }

    private async Task CreateDatabaseBackupAsync(string backupPath, CancellationToken cancellationToken)
    {
        await using var source = new SqliteConnection(_connectionString);
        await source.OpenAsync(cancellationToken);
        await using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupPath }.ToString());
        await backup.OpenAsync(cancellationToken);
        source.BackupDatabase(backup);
    }

    private async Task RestoreDatabaseBackupAsync(string backupPath, CancellationToken cancellationToken)
    {
        await using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupPath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await backup.OpenAsync(cancellationToken);
        await using var target = new SqliteConnection(_connectionString);
        await target.OpenAsync(cancellationToken);
        backup.BackupDatabase(target);
    }

    private async Task ClearSyncedDataAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "DELETE FROM RoomOccupants; DELETE FROM RoomAllocations; DELETE FROM Payments; DELETE FROM CargoQueue; DELETE FROM TourRegistrations; DELETE FROM PersonRecords; DELETE FROM Hotels; DELETE FROM DeletedTourRegistrations;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task VerifyDesktopSnapshotAsync(MobileSyncResultDto result, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var expectedPersons = result.Persons.Count(x => !x.IsDeleted);
        var expectedRegistrations = result.Registrations.Count;
        var expectedRooms = result.Rooming.Count(x => !x.IsDeleted);
        var expectedPayments = result.Payments.Count(x => !x.IsDeleted);
        var expectedCargo = result.CargoQueue.Count(x => !x.IsDeleted);
        foreach (var (table, expected) in new[]
        {
            ("PersonRecords", expectedPersons),
            ("TourRegistrations", expectedRegistrations),
            ("RoomAllocations", expectedRooms),
            ("Payments", expectedPayments),
            ("CargoQueue", expectedCargo)
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table};";
            var actual = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
            if (actual != expected)
                throw new InvalidOperationException($"{table} doğrulaması başarısız: beklenen {expected}, bulunan {actual}.");
        }
    }

    public void Unpair()
    {
        Preferences.Default.Remove("MobileSync.Url");
        Preferences.Default.Remove("MobileSync.Fingerprint");
        Preferences.Default.Remove("MobileSync.DeviceId");
        Preferences.Default.Remove("MobileSync.Paired");
        Preferences.Default.Remove("MobileSync.AwaitingDesktopSync");
        SecureStorage.Default.Remove("MobileSync.Token");
    }

    private async Task<MobileSyncPackage> CreatePackageAsync(CancellationToken cancellationToken)
    {
        var people = await _persons.GetAllAsync();
        var registrations = await _registrations.GetAllAsync();
        var deletedRegistrationIds = await _registrations.GetDeletedIdsAsync();
        var settings = await _settings.GetAllAsync();
        var deviceId = Preferences.Default.Get("MobileSync.DeviceId", string.Empty);
        var roomRecords = await _rooming.GetAllForSyncAsync();
        var desktopRegistrationIds = await _registrations.GetDesktopIdsByLocalIdAsync();
        var paymentRecords = await _payments.GetAllForSyncAsync(desktopRegistrationIds);
        var cargoRecords = await _cargoQueue.GetAllForSyncAsync(desktopRegistrationIds);
        var package = new MobileSyncPackage
        {
            DeviceId = deviceId,
            Persons = people.Select(x => new MobilePersonDto
            {
                LocalId = x.Id, ClientId = x.ClientId, DesktopPersonId = x.DesktopPersonId,
                FirstName = x.FirstName, LastName = x.LastName, TcKimlikNo = x.TcKimlikNo,
                Gender = x.Gender, BirthDate = x.BirthDate, PhoneNumber = x.PhoneNumber, Address = x.Address,
                Aciklama = x.Aciklama, PassportType = x.PassportType, PassportNumber = x.PassportNumber,
                PassportStartDate = x.PassportStartDate, PassportEndDate = x.PassportEndDate,
                BloodType = x.BloodType, HealthStatus = x.HealthStatus, VaccineStatus = x.VaccineStatus,
                IsActive = x.IsActive, IsDeleted = x.IsDeleted, AgeRange = x.AgeRange, Nationality = x.Nationality,
                LastModifiedUtc = x.LastModifiedUtc
            }).ToList(),
            PersonLocalIds = people.ToDictionary(x => x.Id, x => x.ClientId),
            Registrations = registrations.Select(x => new MobileRegistrationDto
            {
                Id = x.Id, DesktopRegistrationId = x.DesktopRegistrationId, PersonId = x.PersonId, TourType = x.TourType, Period = x.Period, GroupType = x.GroupType,
                DaysCount = x.DaysCount, VisaRequest = x.VisaRequest, TicketRequest = x.TicketRequest,
                VisaStatusChoice = x.VisaStatusChoice, TicketStatusChoice = x.TicketStatusChoice,
                PassportArrived = x.PassportArrived, PassportArrivedValue = x.PassportArrivedValue,
                CargoRequest = x.CargoRequest, RoomType = x.RoomType, DepartureCity = x.DepartureCity,
                HasTransfer = x.HasTransfer, TransferCity = x.TransferCity, District = x.District,
                TotalPrice = x.TotalPrice, Currency = x.Currency, PaidAmount = x.PaidAmount,
                RemainingDebt = x.RemainingDebt, Reference = x.Reference, Status = x.Status,
                CreatedDate = x.CreatedDate, IsActive = x.IsActive, LastModifiedUtc = x.LastModifiedUtc
            }).ToList(),
            DeletedRegistrationIds = deletedRegistrationIds.ToList(),
            Settings = settings.Select(x => new MobileSettingDto { Type = x.Type, Value = x.Value, IsActive = x.IsActive, SortOrder = x.SortOrder }).ToList(),
            Rooming = roomRecords.Select(x => new MobileRoomingDto
            {
                Id = x.Id, SyncId = x.SyncId, HotelName = x.HotelName, RoomNumber = x.RoomNumber,
                RoomType = x.RoomType, TourType = x.TourType, Period = x.Period, GroupType = x.GroupType,
                DaysCount = x.DaysCount, CheckInDate = x.CheckInDate, CheckOutDate = x.CheckOutDate,
                IsConnection = x.IsConnection, ConnectionGroupId = x.ConnectionGroupId,
                PassengerIds = x.Occupants.Select(o => o.PassengerId).ToList(),
                LastModifiedUtc = x.LastModifiedUtc, IsDeleted = x.IsDeleted
            }).ToList(),
            Payments = paymentRecords.Select(x => new MobilePaymentDto
            {
                Id = x.Id, SyncId = x.SyncId, DesktopPaymentId = x.DesktopPaymentId,
                RegistrationId = x.RegistrationId, DesktopRegistrationId = x.DesktopRegistrationId,
                CreatedAt = x.CreatedAt, OriginalAmount = x.OriginalAmount, OriginalCurrency = x.OriginalCurrency,
                AmountInRegistrationCurrency = x.AmountInRegistrationCurrency, Rate = x.Rate, Note = x.Note,
                UploadedDocumentPath = x.UploadedDocumentPath, PaymentMethod = x.PaymentMethod,
                LastModifiedUtc = x.LastModifiedUtc, IsDeleted = x.IsDeleted
            }).ToList(),
            CargoQueue = cargoRecords.Select(x => new MobileCargoQueueDto
            {
                Id = x.Id, SyncId = x.SyncId, DesktopQueueId = x.DesktopQueueId,
                RegistrationId = x.RegistrationId, DesktopRegistrationId = x.DesktopRegistrationId,
                FullName = x.FullName, Tc = x.Tc, Phone = x.Phone, Address = x.Address,
                TourType = x.TourType, Period = x.Period, GroupType = x.GroupType, DaysCount = x.DaysCount,
                Quantity = x.Quantity, AddedAt = x.AddedAt, Printed = x.Printed,
                LastModifiedUtc = x.LastModifiedUtc, IsDeleted = x.IsDeleted
            }).ToList()
        };
        return package;
    }

    private static HttpClient CreatePinnedClient(string fingerprint)
    {
        var expected = Convert.FromHexString(fingerprint);
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
                certificate is not null && CryptographicOperations.FixedTimeEquals(SHA256.HashData(certificate.GetRawCertData()), expected)
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
    }

    private static string GetDeviceId()
    {
        var id = Preferences.Default.Get("MobileSync.DeviceId", string.Empty);
        if (!string.IsNullOrWhiteSpace(id)) return id;
        id = Guid.NewGuid().ToString("N");
        Preferences.Default.Set("MobileSync.DeviceId", id);
        return id;
    }

    private static string? TryReadServerError(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            return document.RootElement.TryGetProperty("error", out var error) ? error.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ToMobileSettingType(string type) => type switch
    {
        "Tour Type" => "TourType",
        "Group Type" => "GroupType",
        "Room Type" => "RoomType",
        _ => type
    };

    private sealed class PairResponse { public string Token { get; set; } = string.Empty; }
}

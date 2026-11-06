using System.IO;
using System.Globalization;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Services;

public sealed class MobileSyncServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly X509Certificate2 _certificate;
    private readonly TcpListener _listener;
    private string _pairingCode = string.Empty;
    private DateTime _pairingExpiresUtc;
    private readonly SemaphoreSlim _importLock = new(1, 1);
    private readonly ConcurrentDictionary<string, MobileSyncResultDto> _pendingExports = new(StringComparer.OrdinalIgnoreCase);
    private string? _pairedDeviceId;
    private int _disposed;

    public MobileSyncServer()
    {
        var ip = GetLocalIPv4() ?? throw new InvalidOperationException("Bu bilgisayarda kullanılabilir bir IPv4 ağ adresi bulunamadı.");
        _listener = new TcpListener(IPAddress.Any, 49321);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        BaseUrl = $"https://{ip}:{Port}/";
        _certificate = LoadOrCreateCertificate();
        Fingerprint = Convert.ToHexString(SHA256.HashData(_certificate.RawData));
        ResetPairing();
        _ = Task.Run(ListenAsync);
    }

    public int Port { get; }
    public string BaseUrl { get; }
    public string Fingerprint { get; }
    public string PairingCode => _pairingCode;
    public DateTime PairingExpiresUtc => _pairingExpiresUtc;
    public string PairingPayload => JsonSerializer.Serialize(new { version = 1, url = BaseUrl, pairingCode = _pairingCode, certificateFingerprint = Fingerprint }, JsonOptions);
    public event Action<string>? StatusChanged;
    public event Action<MobileSyncResultDto>? SyncCompleted;

    private async Task ListenAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_shutdown.Token); }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { break; }
            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        using (var stream = new SslStream(client.GetStream(), false))
        {
            var authenticated = false;
            try
            {
                await stream.AuthenticateAsServerAsync(_certificate, false, System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13, false);
                authenticated = true;
                var request = await ReadRequestAsync(stream, _shutdown.Token);
                var response = await ProcessRequestAsync(request);
                await WriteResponseAsync(stream, response.StatusCode, response.Body);
            }
            catch (Exception ex)
            {
                var errorDetails = GetExceptionDetails(ex);
                StatusChanged?.Invoke($"Eşitleme hatası ({ex.GetType().Name}): {errorDetails}");
                if (authenticated)
                {
                    try
                    {
                        var response = JsonResponse(500, new { error = $"{ex.GetType().Name}: {errorDetails}" });
                        await WriteResponseAsync(stream, response.StatusCode, response.Body);
                    }
                    catch (Exception responseException) when (responseException is IOException or ObjectDisposedException or SocketException)
                    {
                        StatusChanged?.Invoke($"Bağlantı yanıtı gönderilemedi: {responseException.Message}");
                    }
                }
            }
        }
    }

    private static string GetExceptionDetails(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(current.Message) && !messages.Contains(current.Message, StringComparer.Ordinal))
                messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }

    private async Task<(int StatusCode, byte[] Body)> ProcessRequestAsync(HttpRequest request)
    {
        if (request.Method == "POST" && request.Path == "/pair")
        {
            if (DateTime.UtcNow > _pairingExpiresUtc)
                return JsonResponse(410, new { error = "Eşleştirme kodunun süresi doldu veya kod kullanıldı." });
            var pair = JsonSerializer.Deserialize<MobileSyncPairRequest>(request.Body, JsonOptions);
            if (pair is null || !FixedEquals(pair.PairingCode, _pairingCode) || string.IsNullOrWhiteSpace(pair.DeviceId))
                return JsonResponse(401, new { error = "Eşleştirme reddedildi." });
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await SaveDevicePairingAsync(pair.DeviceId, pair.DeviceName, HashToken(token), _shutdown.Token);
            _pairedDeviceId = pair.DeviceId;
            StatusChanged?.Invoke($"Eşleştirildi: {pair.DeviceName}");
            ResetPairing();
            StatusChanged?.Invoke("Yeni eşleştirme kodu hazır.");
            return JsonResponse(200, new { token });
        }

        if (request.Method == "POST" && request.Path == "/sync")
        {
            await _importLock.WaitAsync(_shutdown.Token);
            try
            {
                if (request.Body.Length == 0)
                    return JsonResponse(400, new { error = "Telefondan boş eşitleme verisi geldi. Mobil uygulamayı yeniden açıp tekrar deneyin." });
                var package = JsonSerializer.Deserialize<MobileSyncPackage>(request.Body, JsonOptions);
                if (package is null || string.IsNullOrWhiteSpace(request.Authorization) ||
                    !await IsDeviceAuthorizedAsync(package.DeviceId, HashToken(request.Authorization), _shutdown.Token))
                    return JsonResponse(400, new { error = "Eşitleme paketi doğrulanamadı." });
                var result = await ImportAsync(package, _shutdown.Token);
                _pendingExports[package.DeviceId] = result;
                SyncCompleted?.Invoke(result);
                return JsonResponse(200, result);
            }
            finally { _importLock.Release(); }
        }

        if (request.Method == "POST" && request.Path == "/download")
        {
            var deviceRequest = JsonSerializer.Deserialize<MobileSyncDeviceRequest>(request.Body, JsonOptions);
            if (deviceRequest is null || string.IsNullOrWhiteSpace(request.Authorization) ||
                !await IsDeviceAuthorizedAsync(deviceRequest.DeviceId, HashToken(request.Authorization), _shutdown.Token))
                return JsonResponse(401, new { error = "Cihaz doğrulanamadı. Masaüstü QR kodunu yeniden okutun." });
            if (!_pendingExports.TryGetValue(deviceRequest.DeviceId, out var pendingExport))
                return JsonResponse(409, new { error = "Masaüstünde bekleyen eşitleme bulunamadı. Mobilden Masaüstü Programa Eşitle adımını yeniden başlatın." });
            return JsonResponse(200, pendingExport);
        }

        if (request.Method == "POST" && request.Path == "/complete")
        {
            var deviceRequest = JsonSerializer.Deserialize<MobileSyncDeviceRequest>(request.Body, JsonOptions);
            if (deviceRequest is null || string.IsNullOrWhiteSpace(request.Authorization) ||
                !await IsDeviceAuthorizedAsync(deviceRequest.DeviceId, HashToken(request.Authorization), _shutdown.Token))
                return JsonResponse(401, new { error = "Cihaz doğrulanamadı. Masaüstü QR kodunu yeniden okutun." });
            _pendingExports.TryRemove(deviceRequest.DeviceId, out _);
            StatusChanged?.Invoke("Her iki cihaz da eşitlendi. Mobil uygulamadaki veriler masaüstündeki verilerle aynı.");
            return JsonResponse(200, new { completed = true });
        }

        return JsonResponse(404, new { error = "Uç nokta bulunamadı." });
    }

    private static X509Certificate2 LoadOrCreateCertificate()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TurizmOperasyonSistemi");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "mobile-sync-certificate.pfx");
        if (File.Exists(path))
        {
            try { return new X509Certificate2(path, (string?)null, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet); }
            catch { File.Delete(path); }
        }

        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=TOSLite-MobileSync", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddYears(10));
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx));
        return new X509Certificate2(path, (string?)null, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
    }

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static async Task SaveDevicePairingAsync(string deviceId, string deviceName, string tokenHash, CancellationToken cancellationToken)
    {
        await using var db = new AppDbContext();
        await db.Database.OpenConnectionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS MobileSyncDevices (DeviceId TEXT PRIMARY KEY, DeviceName TEXT NOT NULL, TokenHash TEXT NOT NULL, PairedAt TEXT NOT NULL);");
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO MobileSyncDevices (DeviceId, DeviceName, TokenHash, PairedAt) VALUES ($deviceId, $deviceName, $tokenHash, $pairedAt) ON CONFLICT(DeviceId) DO UPDATE SET DeviceName = excluded.DeviceName, TokenHash = excluded.TokenHash, PairedAt = excluded.PairedAt;";
        AddParameter(command, "$deviceId", deviceId);
        AddParameter(command, "$deviceName", deviceName);
        AddParameter(command, "$tokenHash", tokenHash);
        AddParameter(command, "$pairedAt", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> IsDeviceAuthorizedAsync(string deviceId, string tokenHash, CancellationToken cancellationToken)
    {
        await using var db = new AppDbContext();
        await db.Database.OpenConnectionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS MobileSyncDevices (DeviceId TEXT PRIMARY KEY, DeviceName TEXT NOT NULL, TokenHash TEXT NOT NULL, PairedAt TEXT NOT NULL);");
        var command = db.Database.GetDbConnection().CreateCommand();
        await using (command)
        {
            command.CommandText = "SELECT TokenHash FROM MobileSyncDevices WHERE DeviceId = $deviceId;";
            AddParameter(command, "$deviceId", deviceId);
            var storedHash = await command.ExecuteScalarAsync(cancellationToken) as string;
            return storedHash is not null && FixedEquals(storedHash, tokenHash);
        }
    }

    private static async Task<MobileSyncResultDto> ImportAsync(MobileSyncPackage package, CancellationToken cancellationToken)
    {
        var result = new MobileSyncResultDto();
        await using var db = new AppDbContext();
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS MobileSyncLedger (DeviceId TEXT NOT NULL, RecordType TEXT NOT NULL, SourceId TEXT NOT NULL, DesktopId INTEGER NOT NULL, PRIMARY KEY(DeviceId, RecordType, SourceId));", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS DeletedPersons (DeviceId TEXT NOT NULL, ClientId TEXT NOT NULL, DesktopPersonId INTEGER NOT NULL, DeletedAt TEXT NOT NULL, PRIMARY KEY(DeviceId, ClientId));", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS Payments (Id INTEGER PRIMARY KEY AUTOINCREMENT, RegistrationId INTEGER, CreatedAt TEXT, OriginalAmount REAL, OriginalCurrency TEXT, AmountInRegistrationCurrency REAL, Rate REAL, Note TEXT, UploadedDocumentPath TEXT, PaymentMethod TEXT);", cancellationToken);
        await EnsureSyncColumnAsync(db, "Payments", "SyncId", "TEXT NULL", cancellationToken);
        await EnsureSyncColumnAsync(db, "Payments", "MobileDeviceId", "TEXT NULL", cancellationToken);
        await EnsureSyncColumnAsync(db, "Payments", "LastModifiedUtc", "TEXT NULL", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("UPDATE Payments SET SyncId = 'desktop-payment-' || Id WHERE SyncId IS NULL OR SyncId = '';", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_Payments_SyncId ON Payments(SyncId);", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("UPDATE Payments SET LastModifiedUtc = CreatedAt WHERE LastModifiedUtc IS NULL OR LastModifiedUtc = '';", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS RoomAllocationHistories (Id INTEGER PRIMARY KEY AUTOINCREMENT, RoomAllocationId INTEGER, HotelId INTEGER, HotelName TEXT, RoomNumber TEXT, RoomType TEXT, TourType TEXT, Period TEXT, GroupType TEXT, DaysCount TEXT, PersonId INTEGER, RegistrationId INTEGER, RemovedAt TEXT, Restored INTEGER DEFAULT 0, RestoredAt TEXT);", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS MobileSyncCargoQueue (DeviceId TEXT NOT NULL, SyncId TEXT NOT NULL, DesktopQueueId INTEGER NULL, RegistrationId INTEGER NOT NULL, FullName TEXT NOT NULL, Tc TEXT NOT NULL, Phone TEXT NOT NULL, Address TEXT NOT NULL, TourType TEXT NOT NULL, Period TEXT NOT NULL, GroupType TEXT NOT NULL, DaysCount TEXT NOT NULL, Quantity INTEGER NOT NULL, AddedAt TEXT NOT NULL, Printed INTEGER NOT NULL, LastModifiedUtc TEXT NOT NULL, IsDeleted INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(DeviceId, SyncId));", cancellationToken);
        var personMap = new Dictionary<int, int>();
        var clientMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var deletedClientIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mobilePerson in package.Persons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(mobilePerson.ClientId))
            {
                AddConflict(result, "Kişi", "Bilinmiyor", "Kaynak kimliği bulunamadı; aktarılmadı.");
                continue;
            }
            if (mobilePerson.IsDeleted)
            {
                deletedClientIds.Add(mobilePerson.ClientId);
                var deletedPersonId = await FindLedgerIdAsync(db, package.DeviceId, "Person", mobilePerson.ClientId, cancellationToken);
                if (deletedPersonId is null && mobilePerson.DesktopPersonId is > 0)
                    deletedPersonId = mobilePerson.DesktopPersonId;
                if (deletedPersonId is null)
                {
                    var deletedDigits = new string((mobilePerson.TcKimlikNo ?? string.Empty).Where(char.IsDigit).ToArray());
                    if (deletedDigits.Length == 11)
                        deletedPersonId = await db.Persons.Where(x => x.TcKimlikNo == deletedDigits).Select(x => (int?)x.Id).FirstOrDefaultAsync(cancellationToken);
                }
                if (deletedPersonId is > 0)
                {
                    await PersonDeletionService.DeleteAsync(db, deletedPersonId.Value, cancellationToken);
                    AddImportedItem(result, "Kişi silindi", $"{mobilePerson.FirstName} {mobilePerson.LastName}");
                }
                await PersonDeletionService.RecordDeletedAsync(db, package.DeviceId, mobilePerson.ClientId, deletedPersonId ?? mobilePerson.DesktopPersonId ?? 0, cancellationToken);
                continue;
            }
            var globallyDeletedId = await db.Database.SqlQueryRaw<int?>("SELECT DesktopPersonId AS Value FROM DeletedPersons WHERE (DeviceId = '*' OR DeviceId = {0}) AND (ClientId = {1} OR DesktopPersonId = {2}) AND DesktopPersonId > 0 LIMIT 1", package.DeviceId, mobilePerson.ClientId, mobilePerson.DesktopPersonId ?? 0).FirstOrDefaultAsync(cancellationToken);
            if (globallyDeletedId.HasValue)
            {
                deletedClientIds.Add(mobilePerson.ClientId);
                await PersonDeletionService.RecordDeletedAsync(db, package.DeviceId, mobilePerson.ClientId, globallyDeletedId.Value, cancellationToken);
                continue;
            }
            var importedId = await FindLedgerIdAsync(db, package.DeviceId, "Person", mobilePerson.ClientId, cancellationToken);
            if (importedId is > 0)
            {
                var existing = await db.Persons.FirstOrDefaultAsync(x => x.Id == importedId.Value, cancellationToken);
                if (existing is not null)
                {
                    CopyPerson(mobilePerson, existing);
                    existing.LastModifiedUtc = mobilePerson.LastModifiedUtc;
                    await db.SaveChangesAsync(cancellationToken);
                    result.ImportedPersons++;
                    AddImportedItem(result, "Kişi güncellendi", $"{existing.FirstName} {existing.LastName}");
                    clientMap[mobilePerson.ClientId] = importedId.Value;
                    continue;
                }
                await DeleteLedgerAsync(db, package.DeviceId, "Person", mobilePerson.ClientId, cancellationToken);
            }

            Person? match = null;
            if (mobilePerson.DesktopPersonId is > 0)
                match = await db.Persons.FirstOrDefaultAsync(x => x.Id == mobilePerson.DesktopPersonId.Value, cancellationToken);
            var digits = new string((mobilePerson.TcKimlikNo ?? string.Empty).Where(char.IsDigit).ToArray());
            if (match is null && digits.Length == 11)
            {
                var matchingPeople = await db.Persons.Where(x => x.TcKimlikNo == digits).Take(2).ToListAsync(cancellationToken);
                if (matchingPeople.Count == 1) match = matchingPeople[0];
                else if (matchingPeople.Count > 1)
                {
                    AddConflict(result, "Kişi", mobilePerson.ClientId, $"{mobilePerson.FirstName} {mobilePerson.LastName}: aynı T.C. kimlik numarasıyla birden fazla masaüstü kişi kaydı var; otomatik eşleştirme yapılmadı.");
                    continue;
                }
            }
            if (digits.Length != 11 && match is null)
            {
                AddConflict(result, "Kişi", mobilePerson.ClientId, $"{mobilePerson.FirstName} {mobilePerson.LastName}: geçerli T.C. kimlik numarası veya geçerli masaüstü kişi bağlantısı yok; aktarılmadı.");
                continue;
            }
            if (match is null)
            {
                match = new Person
                {
                    FirstName = mobilePerson.FirstName,
                    LastName = mobilePerson.LastName,
                    TcKimlikNo = digits,
                    Gender = mobilePerson.Gender,
                    BirthDate = mobilePerson.BirthDate,
                    PhoneNumber = mobilePerson.PhoneNumber,
                    Address = mobilePerson.Address,
                    Aciklama = mobilePerson.Aciklama,
                    PassportType = mobilePerson.PassportType,
                    PassportNumber = mobilePerson.PassportNumber,
                    PassportStartDate = mobilePerson.PassportStartDate,
                    PassportEndDate = mobilePerson.PassportEndDate,
                    BloodType = mobilePerson.BloodType,
                    HealthStatus = mobilePerson.HealthStatus,
                    VaccineStatus = mobilePerson.VaccineStatus,
                    IsActive = mobilePerson.IsActive,
                    AgeRange = mobilePerson.AgeRange,
                    Nationality = mobilePerson.Nationality,
                    LastModifiedUtc = mobilePerson.LastModifiedUtc
                };
                db.Persons.Add(match);
                await db.SaveChangesAsync(cancellationToken);
                result.ImportedPersons++;
                AddImportedItem(result, "Kişi", $"{match.FirstName} {match.LastName}");
            }
            else
            {
                CopyPerson(mobilePerson, match);
                match.LastModifiedUtc = mobilePerson.LastModifiedUtc;
                await db.SaveChangesAsync(cancellationToken);
                result.ImportedPersons++;
                AddImportedItem(result, "Kişi güncellendi", $"{match.FirstName} {match.LastName}");
            }
            await AddLedgerAsync(db, package.DeviceId, "Person", mobilePerson.ClientId, match.Id, cancellationToken);
            clientMap[mobilePerson.ClientId] = match.Id;
        }

        var deletedRegistrationIds = package.DeletedRegistrationIds.ToHashSet();
        foreach (var deletedRegistrationId in deletedRegistrationIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceId = deletedRegistrationId.ToString(CultureInfo.InvariantCulture);
            var desktopRegistrationId = await FindLedgerIdAsync(db, package.DeviceId, "Registration", sourceId, cancellationToken);
            if (desktopRegistrationId is > 0)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Payments WHERE RegistrationId = {desktopRegistrationId.Value}", cancellationToken);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE MobileSyncCargoQueue SET IsDeleted = 1, LastModifiedUtc = {DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)} WHERE RegistrationId = {desktopRegistrationId.Value}", cancellationToken);
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RoomAllocationHistories WHERE RegistrationId = {desktopRegistrationId.Value}", cancellationToken);
                var registration = await db.Registrations.FirstOrDefaultAsync(x => x.Id == desktopRegistrationId.Value, cancellationToken);
                if (registration is not null)
                {
                    db.Registrations.Remove(registration);
                    await db.SaveChangesAsync(cancellationToken);
                    AddImportedItem(result, "Tur kaydı silindi", $"Kayıt No {desktopRegistrationId.Value}");
                }
                await DeleteLedgerAsync(db, package.DeviceId, "Registration", sourceId, cancellationToken);
            }
            result.AcknowledgedDeletedRegistrationIds.Add(deletedRegistrationId);
        }

        foreach (var registration in package.Registrations)
        {
            if (deletedRegistrationIds.Contains(registration.Id)) continue;
            var sourcePerson = package.Persons.FirstOrDefault(p => p.ClientId == GetClientIdForPerson(package, registration.PersonId));
            if (sourcePerson is not null && deletedClientIds.Contains(sourcePerson.ClientId)) continue;
            if (sourcePerson is null || !clientMap.TryGetValue(sourcePerson.ClientId, out var desktopPersonId))
            {
                AddConflict(result, "Tur kaydı", $"{package.DeviceId}:{registration.Id}", "Bağlı kişi aktarılmadı veya eşleştirilemedi; kayıt aktarılmadı.");
                continue;
            }
            var sourceId = registration.Id.ToString(CultureInfo.InvariantCulture);
            var importedRegistrationId = await FindLedgerIdAsync(db, package.DeviceId, "Registration", sourceId, cancellationToken);
            if (importedRegistrationId is not null)
            {
                var existing = await db.Registrations.FirstOrDefaultAsync(x => x.Id == importedRegistrationId.Value, cancellationToken);
                if (existing is not null)
                {
                    CopyRegistration(registration, existing, desktopPersonId);
                    existing.LastModifiedUtc = registration.LastModifiedUtc;
                    await db.SaveChangesAsync(cancellationToken);
                    result.ImportedRegistrations++;
                    AddImportedItem(result, "Tur kaydı güncellendi", $"{sourcePerson.FirstName} {sourcePerson.LastName} - {registration.TourType} / {registration.Period}");
                    await AddLedgerAsync(db, package.DeviceId, "Registration", sourceId, existing.Id, cancellationToken);
                    continue;
                }
                await DeleteLedgerAsync(db, package.DeviceId, "Registration", sourceId, cancellationToken);
            }
            var matches = await db.Registrations.Where(x => x.PersonId == desktopPersonId && x.TourType == registration.TourType && x.GroupType == registration.GroupType && x.DaysCount == registration.DaysCount).ToListAsync(cancellationToken);
            if (matches.Count == 1)
            {
                var existing = matches[0];
                await AddLedgerAsync(db, package.DeviceId, "Registration", sourceId, existing.Id, cancellationToken);
                CopyRegistration(registration, existing, desktopPersonId);
                existing.LastModifiedUtc = registration.LastModifiedUtc;
                await db.SaveChangesAsync(cancellationToken);
                result.ImportedRegistrations++;
                AddImportedItem(result, "Tur kaydı güncellendi", $"{sourcePerson.FirstName} {sourcePerson.LastName} - {registration.TourType} / {registration.Period}");
                continue;
            }
            if (matches.Count > 1)
            {
                AddConflict(result, "Tur kaydı", sourceId, $"{registration.TourType} / {registration.Period} / {registration.GroupType} / {registration.DaysCount}: masaüstünde birden fazla eşleşen kayıt var; otomatik bağlama yapılmadı.");
                continue;
            }
            var desktopRegistration = new Registration
            {
                PersonId = desktopPersonId,
                TourType = registration.TourType,
                Period = registration.Period,
                GroupType = registration.GroupType,
                DaysCount = registration.DaysCount,
                VisaRequest = registration.VisaRequest,
                TicketRequest = registration.TicketRequest,
                VisaStatusChoice = registration.VisaStatusChoice,
                TicketStatusChoice = registration.TicketStatusChoice,
                PassportArrived = registration.PassportArrived,
                CargoRequest = registration.CargoRequest,
                RoomType = registration.RoomType,
                DepartureCity = registration.DepartureCity,
                HasTransfer = registration.HasTransfer,
                TransferCity = registration.TransferCity,
                District = registration.District,
                TotalPrice = registration.TotalPrice,
                Currency = registration.Currency,
                PaidAmount = registration.PaidAmount,
                RemainingDebt = registration.RemainingDebt,
                Reference = registration.Reference,
                Status = registration.Status,
                CreatedDate = registration.CreatedDate,
                IsActive = registration.IsActive,
                LastModifiedUtc = registration.LastModifiedUtc
            };
            db.Registrations.Add(desktopRegistration);
            await db.SaveChangesAsync(cancellationToken);
            await AddLedgerAsync(db, package.DeviceId, "Registration", sourceId, desktopRegistration.Id, cancellationToken);
            result.ImportedRegistrations++;
            AddImportedItem(result, "Tur kaydı", $"{sourcePerson.FirstName} {sourcePerson.LastName} - {registration.TourType} / {registration.Period} / {registration.GroupType}");
        }

        foreach (var payment in package.Payments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(payment.SyncId))
            {
                AddConflict(result, "Ödeme", payment.Id.ToString(CultureInfo.InvariantCulture), "Ödeme için kalıcı eşitleme kimliği bulunamadı; aktarılmadı.");
                continue;
            }
            var registrationId = payment.DesktopRegistrationId;
            if (registrationId is null && payment.RegistrationId > 0)
                registrationId = await FindLedgerIdAsync(db, package.DeviceId, "Registration", payment.RegistrationId.ToString(CultureInfo.InvariantCulture), cancellationToken);
            if (registrationId is null || !await db.Registrations.AnyAsync(x => x.Id == registrationId.Value, cancellationToken))
            {
                AddConflict(result, "Ödeme", payment.SyncId, "Bağlı tur kaydı masaüstünde bulunamadı; ödeme aktarılmadı.");
                continue;
            }
            if (payment.IsDeleted)
            {
                var deleteCommand = db.Database.GetDbConnection().CreateCommand();
                await using (deleteCommand)
                {
                    deleteCommand.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
                    deleteCommand.CommandText = "DELETE FROM Payments WHERE SyncId = $syncId OR Id = $desktopId;";
                    AddParameter(deleteCommand, "$syncId", payment.SyncId);
                    AddParameter(deleteCommand, "$desktopId", (object?)payment.DesktopPaymentId ?? DBNull.Value);
                    await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
                }
                await RecalculateDesktopPaymentSummaryAsync(db, registrationId.Value, cancellationToken);
                result.Payments.Add(new MobilePaymentDto
                {
                    Id = payment.Id,
                    SyncId = payment.SyncId,
                    DesktopPaymentId = payment.DesktopPaymentId,
                    RegistrationId = payment.RegistrationId,
                    DesktopRegistrationId = registrationId,
                    CreatedAt = payment.CreatedAt,
                    OriginalAmount = payment.OriginalAmount,
                    OriginalCurrency = payment.OriginalCurrency,
                    AmountInRegistrationCurrency = payment.AmountInRegistrationCurrency,
                    Rate = payment.Rate,
                    Note = payment.Note,
                    PaymentMethod = payment.PaymentMethod,
                    LastModifiedUtc = payment.LastModifiedUtc,
                    IsDeleted = true
                });
                continue;
            }
            var upsert = db.Database.GetDbConnection().CreateCommand();
            await using (upsert)
            {
                upsert.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
                upsert.CommandText = "INSERT INTO Payments (SyncId, MobileDeviceId, RegistrationId, CreatedAt, OriginalAmount, OriginalCurrency, AmountInRegistrationCurrency, Rate, Note, UploadedDocumentPath, PaymentMethod, LastModifiedUtc) VALUES ($syncId, $deviceId, $registrationId, $createdAt, $originalAmount, $originalCurrency, $converted, $rate, $note, '', $method, $modified) ON CONFLICT(SyncId) DO UPDATE SET MobileDeviceId=excluded.MobileDeviceId, RegistrationId=excluded.RegistrationId, CreatedAt=excluded.CreatedAt, OriginalAmount=excluded.OriginalAmount, OriginalCurrency=excluded.OriginalCurrency, AmountInRegistrationCurrency=excluded.AmountInRegistrationCurrency, Rate=excluded.Rate, Note=excluded.Note, PaymentMethod=excluded.PaymentMethod, LastModifiedUtc=excluded.LastModifiedUtc;";
                AddParameter(upsert, "$syncId", payment.SyncId);
                AddParameter(upsert, "$deviceId", package.DeviceId);
                AddParameter(upsert, "$registrationId", registrationId.Value);
                AddParameter(upsert, "$createdAt", payment.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
                AddParameter(upsert, "$originalAmount", payment.OriginalAmount);
                AddParameter(upsert, "$originalCurrency", payment.OriginalCurrency);
                AddParameter(upsert, "$converted", payment.AmountInRegistrationCurrency);
                AddParameter(upsert, "$rate", (object?)payment.Rate ?? DBNull.Value);
                AddParameter(upsert, "$note", payment.Note);
                AddParameter(upsert, "$method", payment.PaymentMethod);
                AddParameter(upsert, "$modified", payment.LastModifiedUtc.ToString("O", CultureInfo.InvariantCulture));
                await upsert.ExecuteNonQueryAsync(cancellationToken);
            }
            await RecalculateDesktopPaymentSummaryAsync(db, registrationId.Value, cancellationToken);
            result.ImportedItems.Add(new MobileSyncItemDto { RecordType = "Ödeme", Description = payment.SyncId });
        }

        foreach (var cargo in package.CargoQueue)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(cargo.SyncId))
            {
                AddConflict(result, "Kargo kuyruğu", cargo.Id.ToString(CultureInfo.InvariantCulture), "Kargo öğesi için kalıcı eşitleme kimliği bulunamadı; aktarılmadı.");
                continue;
            }
            var registrationId = cargo.DesktopRegistrationId;
            if (registrationId is null && cargo.RegistrationId > 0)
                registrationId = await FindLedgerIdAsync(db, package.DeviceId, "Registration", cargo.RegistrationId.ToString(CultureInfo.InvariantCulture), cancellationToken);
            if (registrationId is null || !await db.Registrations.AnyAsync(x => x.Id == registrationId.Value, cancellationToken))
            {
                AddConflict(result, "Kargo kuyruğu", cargo.SyncId, "Bağlı tur kaydı masaüstünde bulunamadı; kargo öğesi aktarılmadı.");
                continue;
            }
            var queueCommand = db.Database.GetDbConnection().CreateCommand();
            var cargoSyncId = cargo.SyncId;
            var cargoIsDeleted = cargo.IsDeleted;
            await using (queueCommand)
            {
                queueCommand.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
                queueCommand.CommandText = "INSERT INTO MobileSyncCargoQueue (DeviceId, SyncId, DesktopQueueId, RegistrationId, FullName, Tc, Phone, Address, TourType, Period, GroupType, DaysCount, Quantity, AddedAt, Printed, LastModifiedUtc, IsDeleted) VALUES ($deviceId, $syncId, $queueId, $registrationId, $fullName, $tc, $phone, $address, $tourType, $period, $groupType, $daysCount, $quantity, $addedAt, $printed, $modified, $deleted) ON CONFLICT(DeviceId, SyncId) DO UPDATE SET DesktopQueueId=excluded.DesktopQueueId, RegistrationId=excluded.RegistrationId, FullName=excluded.FullName, Tc=excluded.Tc, Phone=excluded.Phone, Address=excluded.Address, TourType=excluded.TourType, Period=excluded.Period, GroupType=excluded.GroupType, DaysCount=excluded.DaysCount, Quantity=excluded.Quantity, AddedAt=excluded.AddedAt, Printed=excluded.Printed, LastModifiedUtc=excluded.LastModifiedUtc, IsDeleted=excluded.IsDeleted;";
                AddParameter(queueCommand, "$deviceId", package.DeviceId);
                AddParameter(queueCommand, "$syncId", cargo.SyncId);
                AddParameter(queueCommand, "$queueId", (object?)cargo.DesktopQueueId ?? DBNull.Value);
                AddParameter(queueCommand, "$registrationId", registrationId.Value);
                AddParameter(queueCommand, "$fullName", cargo.FullName);
                AddParameter(queueCommand, "$tc", cargo.Tc);
                AddParameter(queueCommand, "$phone", cargo.Phone);
                AddParameter(queueCommand, "$address", cargo.Address);
                AddParameter(queueCommand, "$tourType", cargo.TourType);
                AddParameter(queueCommand, "$period", cargo.Period);
                AddParameter(queueCommand, "$groupType", cargo.GroupType);
                AddParameter(queueCommand, "$daysCount", cargo.DaysCount);
                AddParameter(queueCommand, "$quantity", cargo.Quantity);
                AddParameter(queueCommand, "$addedAt", cargo.AddedAt.ToString("O", CultureInfo.InvariantCulture));
                AddParameter(queueCommand, "$printed", cargo.Printed ? 1 : 0);
                AddParameter(queueCommand, "$modified", cargo.LastModifiedUtc.ToString("O", CultureInfo.InvariantCulture));
                AddParameter(queueCommand, "$deleted", cargo.IsDeleted ? 1 : 0);
                await queueCommand.ExecuteNonQueryAsync(cancellationToken);
            }
            var cargoItem = new PrintQueueItem
            {
                SyncId = cargoSyncId, SyncDeviceId = package.DeviceId, DesktopQueueId = cargo.DesktopQueueId,
                RegistrationId = registrationId.Value, FullName = cargo.FullName, Tc = cargo.Tc,
                Phone = cargo.Phone, Address = cargo.Address, TourType = cargo.TourType, Period = cargo.Period,
                GroupType = cargo.GroupType, DaysCount = cargo.DaysCount, Quantity = cargo.Quantity,
                AddedAt = cargo.AddedAt, Printed = cargo.Printed, LastModifiedUtc = cargo.LastModifiedUtc,
                IsDeleted = cargoIsDeleted
            };
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
                CargoPrintQueue.Instance.ApplySyncItem(cargoItem);
            else
                await dispatcher.InvokeAsync(() => CargoPrintQueue.Instance.ApplySyncItem(cargoItem));
        }

        foreach (var setting in package.Settings)
        {
            if (string.IsNullOrWhiteSpace(setting.Type) || string.IsNullOrWhiteSpace(setting.Value)) continue;
            var exists = await db.AppSettings.FirstOrDefaultAsync(x => x.Type == setting.Type && x.Value == setting.Value, cancellationToken);
            if (exists is not null)
            {
                if (exists.IsActive != setting.IsActive || exists.SortOrder != setting.SortOrder)
                    AddConflict(result, "Ayar", $"{setting.Type}:{setting.Value}", "Aynı ayar farklı etkinlik/sıra değerine sahip; masaüstü ayarı değiştirilmedi.");
                continue;
            }
            db.AppSettings.Add(new AppSetting { Type = setting.Type, Value = setting.Value, IsActive = setting.IsActive, SortOrder = setting.SortOrder });
            await db.SaveChangesAsync(cancellationToken);
            result.ImportedSettings++;
            AddImportedItem(result, "Ayar", $"{setting.Type}: {setting.Value}");
        }

        var hotelMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var room in package.Rooming)
        {
            var activePassengerIds = room.PassengerIds
                .Where(id => package.PersonLocalIds.TryGetValue(id, out var clientId) && !deletedClientIds.Contains(clientId))
                .ToList();
            var sourceId = string.IsNullOrWhiteSpace(room.SyncId) ? room.Id.ToString(CultureInfo.InvariantCulture) : room.SyncId;
            var legacyDesktopId = room.Id > 0
                ? await FindLedgerIdAsync(db, package.DeviceId, "RoomAllocation", room.Id.ToString(CultureInfo.InvariantCulture), cancellationToken)
                : null;
            var existing = legacyDesktopId is > 0
                ? await db.RoomAllocations.Include(x => x.Hotel).Include(x => x.Occupants).FirstOrDefaultAsync(x => x.Id == legacyDesktopId.Value, cancellationToken)
                : null;
            existing ??= await db.RoomAllocations.Include(x => x.Hotel).Include(x => x.Occupants)
                .FirstOrDefaultAsync(x => x.SyncId == room.SyncId, cancellationToken);
            var existingRoomId = existing?.Id ?? 0;
            if (room.IsDeleted)
            {
                if (existing is null)
                {
                    continue;
                }
                db.RoomOccupants.RemoveRange(existing.Occupants);
                existing.IsDeleted = true;
                existing.LastModifiedUtc = room.LastModifiedUtc;
                await db.SaveChangesAsync(cancellationToken);
                if (await FindLedgerIdAsync(db, package.DeviceId, "RoomAllocation", sourceId, cancellationToken) is null)
                    await AddLedgerAsync(db, package.DeviceId, "RoomAllocation", sourceId, existing.Id, cancellationToken);
                continue;
            }
            if (activePassengerIds.Count == 0) continue;
            var personIds = new List<int>();
            foreach (var mobilePersonId in activePassengerIds)
            {
                if (!package.PersonLocalIds.TryGetValue(mobilePersonId, out var clientId) || !clientMap.TryGetValue(clientId, out var desktopId))
                {
                    AddConflict(result, "Oda yerleşimi", sourceId, $"Katılımcı ({mobilePersonId}) eşleştirilemedi; oda aktarılmadı.");
                    personIds.Clear();
                    break;
                }
                personIds.Add(desktopId);
            }
            if (personIds.Count == 0) continue;
            var roomConflict = await db.RoomAllocations.AnyAsync(x => x.Id != existingRoomId && !x.IsDeleted && x.RoomNumber == room.RoomNumber && x.TourType == room.TourType && x.Period == room.Period && x.GroupType == room.GroupType && x.DaysCount == room.DaysCount && x.Hotel != null && x.Hotel.Name == room.HotelName, cancellationToken);
            if (roomConflict)
            {
                AddConflict(result, "Oda yerleşimi", sourceId, $"{room.HotelName} / {room.RoomNumber}: masaüstünde aynı tur ölçütlerinde oda mevcut; aktarılmadı.");
                continue;
            }
            var alreadyAllocated = await db.RoomOccupants.Where(x => personIds.Contains(x.PassengerId) && x.RoomAllocationId != existingRoomId)
                .Select(x => new { x.PassengerId, x.RoomAllocation.TourType, x.RoomAllocation.Period, x.RoomAllocation.GroupType, x.RoomAllocation.DaysCount, HotelName = x.RoomAllocation.Hotel.Name })
                .AnyAsync(x => personIds.Contains(x.PassengerId) && x.TourType == room.TourType && x.Period == room.Period && x.GroupType == room.GroupType && x.DaysCount == room.DaysCount && x.HotelName == room.HotelName, cancellationToken);
            if (alreadyAllocated)
            {
                AddConflict(result, "Oda yerleşimi", sourceId, "Katılımcılardan en az biri aynı tur ölçütlerinde başka odaya atanmış; oda aktarılmadı.");
                continue;
            }
            var hotel = existing?.Hotel ?? await db.Hotels.FirstOrDefaultAsync(x => x.Name == room.HotelName, cancellationToken);
            if (hotel is null)
            {
                hotel = new Hotel { Name = room.HotelName };
                db.Hotels.Add(hotel);
                await db.SaveChangesAsync(cancellationToken);
            }
            var allocation = existing ?? new RoomAllocation();
            allocation.HotelId = hotel.Id;
            allocation.Hotel = hotel;
            allocation.RoomNumber = room.RoomNumber;
            allocation.RoomType = room.RoomType;
            allocation.TourType = room.TourType;
            allocation.Period = room.Period;
            allocation.GroupType = room.GroupType;
            allocation.DaysCount = room.DaysCount;
            allocation.CheckInDate = room.CheckInDate;
            allocation.CheckOutDate = room.CheckOutDate;
            allocation.IsConnection = room.IsConnection;
            allocation.ConnectionGroupId = room.ConnectionGroupId;
            allocation.SyncId = room.SyncId;
            allocation.LastModifiedUtc = room.LastModifiedUtc;
            allocation.IsDeleted = room.IsDeleted;
            if (existing is not null)
            {
                db.RoomOccupants.RemoveRange(existing.Occupants);
            }
            else db.RoomAllocations.Add(allocation);
            await db.SaveChangesAsync(cancellationToken);
            foreach (var personId in personIds)
                db.RoomOccupants.Add(new RoomOccupant { RoomAllocationId = allocation.Id, PassengerId = personId });
            await db.SaveChangesAsync(cancellationToken);
            if (await FindLedgerIdAsync(db, package.DeviceId, "RoomAllocation", sourceId, cancellationToken) is null)
                await AddLedgerAsync(db, package.DeviceId, "RoomAllocation", sourceId, allocation.Id, cancellationToken);
            result.ImportedRooming++;
            AddImportedItem(result, "Oda yerleşimi", $"{room.HotelName} - Oda {room.RoomNumber} ({personIds.Count} kişi)");
        }

        result.Settings = await db.AppSettings.AsNoTracking()
            .Select(x => new MobileSettingDto { Type = x.Type, Value = x.Value, IsActive = x.IsActive, SortOrder = x.SortOrder })
            .ToListAsync(cancellationToken);
        var deletedPersons = await db.Database.SqlQueryRaw<DeletedPersonRow>("SELECT DeviceId, ClientId, DesktopPersonId FROM DeletedPersons WHERE DeviceId = {0} OR DeviceId = '*'", package.DeviceId).ToListAsync(cancellationToken);
        foreach (var deletedPerson in deletedPersons)
        {
            var linkedMobilePerson = package.Persons.FirstOrDefault(x => x.ClientId == deletedPerson.ClientId || x.DesktopPersonId == deletedPerson.DesktopPersonId);
            result.DeletedPersons.Add(new MobilePersonDto { LocalId = linkedMobilePerson?.LocalId ?? 0, ClientId = deletedPerson.ClientId, DesktopPersonId = deletedPerson.DesktopPersonId, FirstName = linkedMobilePerson?.FirstName ?? string.Empty, LastName = linkedMobilePerson?.LastName ?? string.Empty, TcKimlikNo = linkedMobilePerson?.TcKimlikNo ?? string.Empty, IsDeleted = true });
        }
        var allDesktopPeople = await db.Persons.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var person in allDesktopPeople)
        {
            var linkedMobilePerson = package.Persons.FirstOrDefault(x =>
                clientMap.TryGetValue(x.ClientId, out var mappedId) && mappedId == person.Id);
            result.Persons.Add(ToMobilePerson(person, linkedMobilePerson ?? new MobilePersonDto()));
        }

        var localPersonIds = clientMap.ToDictionary(x => x.Value, x => package.Persons.First(p => p.ClientId == x.Key).LocalId);
        foreach (var deletedPerson in deletedPersons)
        {
            var linkedMobilePerson = package.Persons.FirstOrDefault(x => x.ClientId == deletedPerson.ClientId || x.DesktopPersonId == deletedPerson.DesktopPersonId);
            if (linkedMobilePerson is not null)
                localPersonIds.Remove(deletedPerson.DesktopPersonId);
        }
        foreach (var desktopPerson in allDesktopPeople)
        {
            if (localPersonIds.ContainsKey(desktopPerson.Id)) continue;
            var mobilePerson = package.Persons.FirstOrDefault(x => x.DesktopPersonId == desktopPerson.Id);
            if (mobilePerson is not null) localPersonIds[desktopPerson.Id] = mobilePerson.LocalId;
        }
        var desktopPersonIds = allDesktopPeople.Select(x => x.Id).ToList();
        var desktopRegistrations = await db.Registrations.AsNoTracking()
            .Where(x => desktopPersonIds.Contains(x.PersonId))
            .ToListAsync(cancellationToken);
        var mobileRegistrationsById = package.Registrations.ToDictionary(x => x.Id);
        foreach (var registration in desktopRegistrations)
        {
            var mobilePersonId = localPersonIds.TryGetValue(registration.PersonId, out var mappedPersonId) ? mappedPersonId : 0;
            var sourceId = await FindLedgerSourceIdAsync(db, package.DeviceId, "Registration", registration.Id, cancellationToken);
            var mobileId = int.TryParse(sourceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId) &&
                mobileRegistrationsById.ContainsKey(parsedId) && mobileRegistrationsById[parsedId].PersonId == mobilePersonId
                ? parsedId
                : package.Registrations.FirstOrDefault(x => x.PersonId == mobilePersonId &&
                    x.TourType == registration.TourType && x.Period == registration.Period &&
                    x.GroupType == registration.GroupType && x.DaysCount == registration.DaysCount)?.Id ?? 0;
            var mobileRegistration = mobileRegistrationsById.GetValueOrDefault(mobileId);
            var mobileDto = ToMobileRegistration(registration, mobileId, mobilePersonId);
            mobileDto.PassportArrivedValue = mobileRegistration?.PassportArrived == registration.PassportArrived
                ? mobileRegistration.PassportArrivedValue
                : registration.PassportArrived switch
                {
                    1 => "Evet",
                    0 => "Hayır",
                    _ => string.Empty
                };
            result.Registrations.Add(mobileDto);
        }
        var paymentCommand = db.Database.GetDbConnection().CreateCommand();
        await using (paymentCommand)
        {
            paymentCommand.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            paymentCommand.CommandText = "SELECT Id, RegistrationId, CreatedAt, OriginalAmount, OriginalCurrency, AmountInRegistrationCurrency, Rate, Note, UploadedDocumentPath, PaymentMethod, SyncId, LastModifiedUtc FROM Payments ORDER BY Id;";
            await using var paymentReader = await paymentCommand.ExecuteReaderAsync(cancellationToken);
            while (await paymentReader.ReadAsync(cancellationToken))
            {
                var paymentId = paymentReader.GetInt32(0);
                var registrationId = paymentReader.GetInt32(1);
                var syncId = paymentReader.IsDBNull(10) || string.IsNullOrWhiteSpace(paymentReader.GetString(10))
                    ? $"desktop-payment-{paymentId}"
                    : paymentReader.GetString(10);
                result.Payments.Add(new MobilePaymentDto
                {
                    Id = package.Payments.FirstOrDefault(x => x.SyncId == syncId)?.Id ?? 0,
                    SyncId = syncId,
                    DesktopPaymentId = paymentId,
                    RegistrationId = package.Payments.FirstOrDefault(x => x.SyncId == syncId)?.RegistrationId ?? 0,
                    DesktopRegistrationId = registrationId,
                    CreatedAt = DateTime.TryParse(paymentReader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt) ? createdAt : DateTime.UtcNow,
                    OriginalAmount = Convert.ToDecimal(paymentReader.GetValue(3), CultureInfo.InvariantCulture),
                    OriginalCurrency = paymentReader.IsDBNull(4) ? string.Empty : paymentReader.GetString(4),
                    AmountInRegistrationCurrency = Convert.ToDecimal(paymentReader.GetValue(5), CultureInfo.InvariantCulture),
                    Rate = paymentReader.IsDBNull(6) ? null : Convert.ToDecimal(paymentReader.GetValue(6), CultureInfo.InvariantCulture),
                    Note = paymentReader.IsDBNull(7) ? string.Empty : paymentReader.GetString(7),
                    UploadedDocumentPath = string.Empty,
                    PaymentMethod = paymentReader.IsDBNull(9) ? string.Empty : paymentReader.GetString(9),
                    LastModifiedUtc = paymentReader.IsDBNull(11) || !DateTime.TryParse(paymentReader.GetString(11), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var modifiedAt) ? DateTime.UtcNow : modifiedAt
                });
            }
        }
        foreach (var payment in result.Payments)
        {
            if (payment.RegistrationId > 0) continue;
            var sourceRegistrationId = await FindLedgerSourceIdAsync(db, package.DeviceId, "Registration", payment.DesktopRegistrationId!.Value, cancellationToken);
            payment.RegistrationId = int.TryParse(sourceRegistrationId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRegistrationId)
                ? parsedRegistrationId
                : package.Registrations.FirstOrDefault(x => x.DesktopRegistrationId == payment.DesktopRegistrationId)?.Id ?? 0;
        }
        var cargoRows = await db.Database.SqlQueryRaw<CargoQueueSyncRow>("SELECT DeviceId, SyncId, DesktopQueueId, RegistrationId, FullName, Tc, Phone, Address, TourType, Period, GroupType, DaysCount, Quantity, AddedAt, Printed, LastModifiedUtc, IsDeleted FROM MobileSyncCargoQueue").ToListAsync(cancellationToken);
        foreach (var cargo in cargoRows)
        {
            var sourceRegistrationId = await FindLedgerSourceIdAsync(db, package.DeviceId, "Registration", cargo.RegistrationId, cancellationToken);
            var mobileRegistrationId = int.TryParse(sourceRegistrationId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRegistrationId)
                ? parsedRegistrationId
                : package.Registrations.FirstOrDefault(x => x.DesktopRegistrationId == cargo.RegistrationId)?.Id ?? 0;
            result.CargoQueue.Add(new MobileCargoQueueDto
            {
                Id = 0, SyncId = cargo.SyncId, DesktopQueueId = cargo.DesktopQueueId ?? cargo.RegistrationId,
                RegistrationId = mobileRegistrationId, DesktopRegistrationId = cargo.RegistrationId,
                FullName = cargo.FullName, Tc = cargo.Tc, Phone = cargo.Phone, Address = cargo.Address,
                TourType = cargo.TourType, Period = cargo.Period, GroupType = cargo.GroupType, DaysCount = cargo.DaysCount,
                Quantity = cargo.Quantity, AddedAt = cargo.AddedAt, Printed = cargo.Printed,
                LastModifiedUtc = cargo.LastModifiedUtc, IsDeleted = cargo.IsDeleted
            });
        }
        foreach (var item in CargoPrintQueue.Instance.Items.Where(x => !x.IsDeleted))
        {
            var registrationSourceId = await FindLedgerSourceIdAsync(db, package.DeviceId, "Registration", item.RegistrationId, cancellationToken);
            var mobileRegistrationId = int.TryParse(registrationSourceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRegistrationId)
                ? parsedRegistrationId
                : package.Registrations.FirstOrDefault(x => x.DesktopRegistrationId == item.RegistrationId)?.Id ?? 0;
            var cargoDto = new MobileCargoQueueDto
            {
                Id = 0, SyncId = item.SyncId, DesktopQueueId = item.DesktopQueueId ?? item.RegistrationId,
                RegistrationId = mobileRegistrationId, DesktopRegistrationId = item.RegistrationId,
                FullName = item.FullName, Tc = item.Tc, Phone = item.Phone, Address = item.Address,
                TourType = item.TourType, Period = item.Period, GroupType = item.GroupType, DaysCount = item.DaysCount,
                Quantity = item.Quantity, AddedAt = item.AddedAt, Printed = item.Printed,
                LastModifiedUtc = item.LastModifiedUtc, IsDeleted = item.IsDeleted
            };
            var existingCargo = result.CargoQueue.FindIndex(x => x.SyncId == item.SyncId);
            if (existingCargo >= 0) result.CargoQueue[existingCargo] = cargoDto;
            else result.CargoQueue.Add(cargoDto);
        }
        var desktopRooms = await db.RoomAllocations.AsNoTracking().Include(x => x.Hotel).Include(x => x.Occupants)
            .ToListAsync(cancellationToken);
        foreach (var room in desktopRooms)
        {
            var syncedRoom = package.Rooming.FirstOrDefault(x => x.SyncId == room.SyncId);
            var mobileId = syncedRoom?.Id ?? 0;
            if (room.IsDeleted && syncedRoom is null) continue;
            result.Rooming.Add(new MobileRoomingDto
            {
                Id = mobileId,
                SyncId = room.SyncId,
                HotelName = room.Hotel?.Name ?? string.Empty,
                RoomNumber = room.RoomNumber,
                RoomType = room.RoomType,
                TourType = room.TourType,
                Period = room.Period,
                GroupType = room.GroupType,
                DaysCount = room.DaysCount,
                CheckInDate = room.CheckInDate,
                CheckOutDate = room.CheckOutDate,
                IsConnection = room.IsConnection,
                ConnectionGroupId = room.ConnectionGroupId,
                PassengerIds = room.Occupants.Where(x => localPersonIds.ContainsKey(x.PassengerId)).Select(x => localPersonIds[x.PassengerId]).ToList(),
                PassengerDesktopIds = room.Occupants.Select(x => x.PassengerId).ToList(),
                LastModifiedUtc = room.LastModifiedUtc,
                IsDeleted = room.IsDeleted
            });
        }

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static async Task EnsureSyncColumnAsync(AppDbContext db, string table, string column, string definition, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await check.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
        await reader.DisposeAsync();
        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RecalculateDesktopPaymentSummaryAsync(AppDbContext db, int registrationId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Registrations SET PaidAmount = (SELECT COALESCE(SUM(AmountInRegistrationCurrency), 0) FROM Payments WHERE RegistrationId = {registrationId}), RemainingDebt = TourPrice - (SELECT COALESCE(SUM(AmountInRegistrationCurrency), 0) FROM Payments WHERE RegistrationId = {registrationId}), LastModifiedUtc = {now} WHERE Id = {registrationId};", cancellationToken);
    }

    private sealed class CargoQueueSyncRow
    {
        public string DeviceId { get; set; } = string.Empty;
        public string SyncId { get; set; } = string.Empty;
        public int? DesktopQueueId { get; set; }
        public int RegistrationId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Tc { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string TourType { get; set; } = string.Empty;
        public string Period { get; set; } = string.Empty;
        public string GroupType { get; set; } = string.Empty;
        public string DaysCount { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public DateTime AddedAt { get; set; }
        public bool Printed { get; set; }
        public DateTime LastModifiedUtc { get; set; }
        public bool IsDeleted { get; set; }
    }

    private static string? GetClientIdForPerson(MobileSyncPackage package, int localId) => package.PersonLocalIds.TryGetValue(localId, out var id) ? id : null;
    private static bool PersonMatches(Person person, MobilePersonDto mobile, string digits) => person.TcKimlikNo == digits && string.Equals(person.FirstName, mobile.FirstName, StringComparison.OrdinalIgnoreCase) && string.Equals(person.LastName, mobile.LastName, StringComparison.OrdinalIgnoreCase) && person.BirthDate.Date == mobile.BirthDate.Date;
    private static void CopyPerson(MobilePersonDto source, Person target)
    {
        target.FirstName = source.FirstName;
        target.LastName = source.LastName;
        target.TcKimlikNo = new string(source.TcKimlikNo.Where(char.IsDigit).ToArray());
        target.Gender = source.Gender;
        target.BirthDate = source.BirthDate;
        target.PhoneNumber = source.PhoneNumber;
        target.Address = source.Address;
        target.Aciklama = source.Aciklama;
        target.PassportType = source.PassportType;
        target.PassportNumber = source.PassportNumber;
        target.PassportStartDate = source.PassportStartDate;
        target.PassportEndDate = source.PassportEndDate;
        target.BloodType = source.BloodType;
        target.HealthStatus = source.HealthStatus;
        target.VaccineStatus = source.VaccineStatus;
        target.IsActive = source.IsActive;
        target.AgeRange = source.AgeRange;
        target.Nationality = source.Nationality;
    }

    private static void CopyRegistration(MobileRegistrationDto source, Registration target, int desktopPersonId)
    {
        target.PersonId = desktopPersonId;
        target.TourType = source.TourType;
        target.Period = source.Period;
        target.GroupType = source.GroupType;
        target.DaysCount = source.DaysCount;
        target.VisaRequest = source.VisaRequest;
        target.TicketRequest = source.TicketRequest;
        target.VisaStatusChoice = source.VisaStatusChoice;
        target.TicketStatusChoice = source.TicketStatusChoice;
        target.PassportArrived = source.PassportArrived;
        target.CargoRequest = source.CargoRequest;
        target.RoomType = source.RoomType;
        target.DepartureCity = source.DepartureCity;
        target.HasTransfer = source.HasTransfer;
        target.TransferCity = source.TransferCity;
        target.District = source.District;
        target.TotalPrice = source.TotalPrice;
        target.Currency = source.Currency;
        target.PaidAmount = source.PaidAmount;
        target.RemainingDebt = source.RemainingDebt;
        target.Reference = source.Reference;
        target.Status = source.Status;
        target.CreatedDate = source.CreatedDate;
        target.IsActive = source.IsActive;
    }

    private static MobilePersonDto ToMobilePerson(Person person, MobilePersonDto source) => new()
    {
        LocalId = source.LocalId,
        ClientId = source.ClientId,
        DesktopPersonId = person.Id,
        FirstName = person.FirstName,
        LastName = person.LastName,
        TcKimlikNo = person.TcKimlikNo,
        Gender = person.Gender,
        BirthDate = person.BirthDate,
        PhoneNumber = person.PhoneNumber,
        Address = person.Address,
        Aciklama = person.Aciklama,
        PassportType = person.PassportType,
        PassportNumber = person.PassportNumber,
        PassportStartDate = person.PassportStartDate,
        PassportEndDate = person.PassportEndDate,
        BloodType = person.BloodType,
        HealthStatus = person.HealthStatus,
        VaccineStatus = person.VaccineStatus,
        IsActive = person.IsActive,
        AgeRange = person.AgeRange,
        Nationality = person.Nationality,
        LastModifiedUtc = person.LastModifiedUtc
    };

    private static MobileRegistrationDto ToMobileRegistration(Registration registration, int mobileId, int mobilePersonId) => new()
    {
        Id = mobileId,
        DesktopRegistrationId = registration.Id,
        PersonId = mobilePersonId,
        DesktopPersonId = registration.PersonId,
        TourType = registration.TourType,
        Period = registration.Period,
        GroupType = registration.GroupType,
        DaysCount = registration.DaysCount,
        VisaRequest = registration.VisaRequest,
        TicketRequest = registration.TicketRequest,
        VisaStatusChoice = registration.VisaStatusChoice,
        TicketStatusChoice = registration.TicketStatusChoice,
        PassportArrived = registration.PassportArrived,
        CargoRequest = registration.CargoRequest,
        RoomType = registration.RoomType,
        DepartureCity = registration.DepartureCity,
        HasTransfer = registration.HasTransfer,
        TransferCity = registration.TransferCity,
        District = registration.District,
        TotalPrice = registration.TotalPrice,
        Currency = registration.Currency,
        PaidAmount = registration.PaidAmount,
        RemainingDebt = registration.RemainingDebt,
        Reference = registration.Reference,
        Status = registration.Status,
        CreatedDate = registration.CreatedDate,
        IsActive = registration.IsActive,
        LastModifiedUtc = registration.LastModifiedUtc
    };

    private static void AddConflict(MobileSyncResultDto result, string type, string sourceId, string description) => result.Conflicts.Add(new MobileSyncConflictDto { RecordType = type, SourceId = sourceId, Description = description });
    private static void AddImportedItem(MobileSyncResultDto result, string type, string description) => result.ImportedItems.Add(new MobileSyncItemDto { RecordType = type, Description = description });

    private sealed class DeletedPersonRow
    {
        public string DeviceId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public int DesktopPersonId { get; set; }
    }

    private static async Task<int?> FindLedgerIdAsync(AppDbContext db, string deviceId, string recordType, string sourceId, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT DesktopId FROM MobileSyncLedger WHERE DeviceId = $deviceId AND RecordType = $recordType AND SourceId = $sourceId;";
        AddParameter(command, "$deviceId", deviceId);
        AddParameter(command, "$recordType", recordType);
        AddParameter(command, "$sourceId", sourceId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static async Task<string?> FindLedgerSourceIdAsync(AppDbContext db, string deviceId, string recordType, int desktopId, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT SourceId FROM MobileSyncLedger WHERE DeviceId = $deviceId AND RecordType = $recordType AND DesktopId = $desktopId LIMIT 1;";
        AddParameter(command, "$deviceId", deviceId);
        AddParameter(command, "$recordType", recordType);
        AddParameter(command, "$desktopId", desktopId);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static async Task DeleteLedgerAsync(AppDbContext db, string deviceId, string recordType, string sourceId, CancellationToken cancellationToken)
    {
        var command = db.Database.GetDbConnection().CreateCommand();
        await using (command)
        {
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = "DELETE FROM MobileSyncLedger WHERE DeviceId = $deviceId AND RecordType = $recordType AND SourceId = $sourceId;";
            AddParameter(command, "$deviceId", deviceId);
            AddParameter(command, "$recordType", recordType);
            AddParameter(command, "$sourceId", sourceId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task AddLedgerAsync(AppDbContext db, string deviceId, string recordType, string sourceId, int desktopId, CancellationToken cancellationToken)
    {
        var command = db.Database.GetDbConnection().CreateCommand();
        await using (command)
        {
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = "INSERT INTO MobileSyncLedger (DeviceId, RecordType, SourceId, DesktopId) VALUES ($deviceId, $recordType, $sourceId, $desktopId) ON CONFLICT(DeviceId, RecordType, SourceId) DO UPDATE SET DesktopId = excluded.DesktopId;";
            AddParameter(command, "$deviceId", deviceId);
            AddParameter(command, "$recordType", recordType);
            AddParameter(command, "$sourceId", sourceId);
            AddParameter(command, "$desktopId", desktopId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static bool FixedEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static (int StatusCode, byte[] Body) JsonResponse(int statusCode, object value) => (statusCode, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));

    private static async Task WriteResponseAsync(Stream stream, int statusCode, byte[] body)
    {
        var reason = statusCode switch { 200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", 404 => "Not Found", 410 => "Gone", _ => "Internal Server Error" };
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {statusCode} {reason}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header);
        await stream.WriteAsync(body);
        await stream.FlushAsync();
        if (stream is SslStream sslStream)
            await sslStream.ShutdownAsync();
    }

    private static async Task<HttpRequest> ReadRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var one = new byte[1];
        while (buffer.Length < 16_384)
        {
            var read = await stream.ReadAsync(one, cancellationToken);
            if (read == 0) throw new IOException("HTTP başlıkları tamamlanmadı.");
            buffer.WriteByte(one[0]);
            var bytes = buffer.GetBuffer();
            var length = (int)buffer.Length;
            if (length >= 4 && bytes[length - 4] == 13 && bytes[length - 3] == 10 && bytes[length - 2] == 13 && bytes[length - 1] == 10) break;
        }
        var headerText = Encoding.ASCII.GetString(buffer.ToArray());
        var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var first = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (first.Length < 2) throw new IOException("Geçersiz HTTP isteği.");
        var headers = lines.Skip(1).Select(line => line.Split(':', 2)).Where(parts => parts.Length == 2).ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
        byte[] body;
        if (headers.TryGetValue("Transfer-Encoding", out var transferEncoding) && transferEncoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
            body = await ReadChunkedBodyAsync(stream, cancellationToken);
        else if (headers.TryGetValue("Content-Length", out var lengthText) && int.TryParse(lengthText, out var contentLength))
        {
            if (contentLength < 0 || contentLength > 20_000_000) throw new IOException("İstek boyutu izin verilen sınırı aşıyor.");
            body = new byte[contentLength];
            var offset = 0;
            while (offset < contentLength)
            {
                var read = await stream.ReadAsync(body.AsMemory(offset, contentLength - offset), cancellationToken);
                if (read == 0) throw new IOException("İstek gövdesi eksik.");
                offset += read;
            }
        }
        else body = [];

        headers.TryGetValue("Authorization", out var authorization);
        if (authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true) authorization = authorization[7..];
        return new HttpRequest(first[0], first[1].Split('?')[0], authorization ?? string.Empty, body);
    }

    private static async Task<byte[]> ReadChunkedBodyAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();
        while (true)
        {
            var sizeLine = await ReadLineAsync(stream, cancellationToken);
            var extensionIndex = sizeLine.IndexOf(';');
            var sizeText = extensionIndex >= 0 ? sizeLine[..extensionIndex] : sizeLine;
            if (!int.TryParse(sizeText, System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var chunkSize) || chunkSize < 0)
                throw new IOException("Chunked HTTP isteğinin gövde uzunluğu geçersiz.");
            if (chunkSize == 0)
            {
                while ((await ReadLineAsync(stream, cancellationToken)).Length != 0) { }
                return body.ToArray();
            }
            if (body.Length + chunkSize > 20_000_000)
                throw new IOException("İstek boyutu izin verilen sınırı aşıyor.");

            var chunk = new byte[chunkSize];
            var offset = 0;
            while (offset < chunkSize)
            {
                var read = await stream.ReadAsync(chunk.AsMemory(offset, chunkSize - offset), cancellationToken);
                if (read == 0) throw new IOException("Chunked HTTP isteğinin gövdesi eksik.");
                offset += read;
            }
            await body.WriteAsync(chunk, cancellationToken);
            if ((await ReadLineAsync(stream, cancellationToken)).Length != 0)
                throw new IOException("Chunked HTTP isteğinin sonlandırıcısı eksik.");
        }
    }

    private static async Task<string> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var line = new MemoryStream();
        var buffer = new byte[1];
        while (line.Length < 1024)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) throw new IOException("Chunked HTTP isteği tamamlanmadı.");
            if (buffer[0] == '\n')
            {
                var bytes = line.ToArray();
                return Encoding.ASCII.GetString(bytes, 0, bytes.Length > 0 && bytes[^1] == '\r' ? bytes.Length - 1 : bytes.Length);
            }
            line.WriteByte(buffer[0]);
        }
        throw new IOException("Chunked HTTP başlık satırı izin verilen uzunluğu aşıyor.");
    }

    private static string? GetLocalIPv4() => System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
        .Where(x => x.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && x.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
        .SelectMany(x => x.GetIPProperties().UnicastAddresses)
        .FirstOrDefault(x => x.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x.Address))?.Address.ToString();

    private static X509Certificate2 CreateCertificate(string ip)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=TOSLite-MobileSync", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Parse(ip));
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(2));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _shutdown.Cancel();
        try { _listener.Stop(); } catch { }
        _certificate.Dispose();
        _importLock.Dispose();
        _shutdown.Dispose();
    }

    private sealed record HttpRequest(string Method, string Path, string Authorization, byte[] Body);

    private void ResetPairing()
    {
        _pairingCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _pairingExpiresUtc = DateTime.UtcNow.AddMinutes(10);
    }
}

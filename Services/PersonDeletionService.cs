using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TurizmOperasyonSistemi.Data;

namespace TurizmOperasyonSistemi.Services;

public static class PersonDeletionService
{
    public static async Task DeleteAsync(AppDbContext db, int personId, CancellationToken cancellationToken = default)
    {
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS DeletedPersons (DeviceId TEXT NOT NULL, ClientId TEXT NOT NULL, DesktopPersonId INTEGER NOT NULL, DeletedAt TEXT NOT NULL, PRIMARY KEY(DeviceId, ClientId));", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS MobileSyncLedger (DeviceId TEXT NOT NULL, RecordType TEXT NOT NULL, SourceId TEXT NOT NULL, DesktopId INTEGER NOT NULL, PRIMARY KEY(DeviceId, RecordType, SourceId));", cancellationToken);
        var person = await db.Persons.FirstOrDefaultAsync(x => x.Id == personId, cancellationToken);
        if (person is null) return;

        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS Payments (Id INTEGER PRIMARY KEY AUTOINCREMENT, RegistrationId INTEGER, CreatedAt TEXT, OriginalAmount REAL, OriginalCurrency TEXT, AmountInRegistrationCurrency REAL, Rate REAL, Note TEXT, UploadedDocumentPath TEXT, PaymentMethod TEXT); CREATE TABLE IF NOT EXISTS RoomAllocationHistories (Id INTEGER PRIMARY KEY AUTOINCREMENT, RoomAllocationId INTEGER, HotelId INTEGER, HotelName TEXT, RoomNumber TEXT, RoomType TEXT, TourType TEXT, Period TEXT, GroupType TEXT, DaysCount TEXT, PersonId INTEGER, RegistrationId INTEGER, RemovedAt TEXT, Restored INTEGER DEFAULT 0, RestoredAt TEXT);", cancellationToken);
        IDbContextTransaction? ownedTransaction = null;
        if (db.Database.CurrentTransaction is null)
            ownedTransaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var links = await db.Database.SqlQueryRaw<PersonLedgerRow>("SELECT DeviceId, SourceId AS ClientId FROM MobileSyncLedger WHERE RecordType = 'Person' AND DesktopId = {0}", personId).ToListAsync(cancellationToken);
            foreach (var link in links)
                await RecordDeletedAsync(db, link.DeviceId, link.ClientId, personId, cancellationToken);
        await RecordDeletedAsync(db, "*", links.FirstOrDefault()?.ClientId ?? $"desktop-{personId}", personId, cancellationToken);

            var registrations = await db.Registrations.Where(x => x.PersonId == personId).ToListAsync(cancellationToken);
            var registrationIds = registrations.Select(x => x.Id).ToList();
            var payments = await db.Payments.Where(x => registrationIds.Contains(x.RegistrationId)).ToListAsync(cancellationToken);
            db.Payments.RemoveRange(payments);
            var history = await db.RoomAllocationHistories
                .Where(x => x.PersonId == personId || registrationIds.Contains(x.RegistrationId))
                .ToListAsync(cancellationToken);
            db.RoomAllocationHistories.RemoveRange(history);

            var occupants = await db.RoomOccupants.Where(x => x.PassengerId == personId).ToListAsync(cancellationToken);
            var affectedRoomIds = occupants.Select(x => x.RoomAllocationId).Distinct().ToList();
            db.RoomOccupants.RemoveRange(occupants);
            db.Registrations.RemoveRange(registrations);
            db.Persons.Remove(person);
            await db.SaveChangesAsync(cancellationToken);

            foreach (var roomId in affectedRoomIds)
            {
                var allocation = await db.RoomAllocations.FirstOrDefaultAsync(x => x.Id == roomId, cancellationToken);
                if (allocation is null) continue;
                allocation.IsDeleted = !await db.RoomOccupants.AnyAsync(x => x.RoomAllocationId == roomId, cancellationToken);
                allocation.LastModifiedUtc = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(cancellationToken);
            if (ownedTransaction is not null)
                await ownedTransaction.CommitAsync(cancellationToken);
        }
        finally
        {
            if (ownedTransaction is not null)
                await ownedTransaction.DisposeAsync();
        }
    }

    public static async Task RecordDeletedAsync(AppDbContext db, string deviceId, string clientId, int desktopPersonId, CancellationToken cancellationToken = default)
    {
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS DeletedPersons (DeviceId TEXT NOT NULL, ClientId TEXT NOT NULL, DesktopPersonId INTEGER NOT NULL, DeletedAt TEXT NOT NULL, PRIMARY KEY(DeviceId, ClientId));", cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO DeletedPersons (DeviceId, ClientId, DesktopPersonId, DeletedAt) VALUES ({deviceId}, {clientId}, {desktopPersonId}, {DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)}) ON CONFLICT(DeviceId, ClientId) DO UPDATE SET DesktopPersonId = excluded.DesktopPersonId, DeletedAt = excluded.DeletedAt;", cancellationToken);
        if (deviceId != "*")
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO DeletedPersons (DeviceId, ClientId, DesktopPersonId, DeletedAt) VALUES ('*', {clientId}, {desktopPersonId}, {DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)}) ON CONFLICT(DeviceId, ClientId) DO UPDATE SET DesktopPersonId = excluded.DesktopPersonId, DeletedAt = excluded.DeletedAt;", cancellationToken);
    }

    private sealed class PersonLedgerRow
    {
        public string DeviceId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
    }
}

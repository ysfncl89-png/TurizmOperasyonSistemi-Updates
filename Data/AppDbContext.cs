using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data.Common;
using TurizmOperasyonSistemi.Models;


namespace TurizmOperasyonSistemi.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<Models.Notification> Notifications { get; set; }
        public DbSet<Person> Persons { get; set; }
        public DbSet<AppSetting> AppSettings { get; set; }
        public DbSet<Registration> Registrations { get; set; }
        public DbSet<Models.Payment> Payments { get; set; }
        public DbSet<Hotel> Hotels { get; set; }
        public DbSet<RoomAllocation> RoomAllocations { get; set; }
        public DbSet<RoomOccupant> RoomOccupants { get; set; }
        public DbSet<Models.RoomAllocationHistory> RoomAllocationHistories { get; set; }
        public DbSet<User> Users { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // Eski database path'ini kullan (veri kaybetmemek için)
            // C:\TurizmDB\turizm.db dizini yoksa oluştur
            string dbDirectory = @"C:\TurizmDB";
            if (!System.IO.Directory.Exists(dbDirectory))
            {
                System.IO.Directory.CreateDirectory(dbDirectory);
            }

            string dbPath = System.IO.Path.Combine(dbDirectory, "turizm.db");
            optionsBuilder.UseSqlite($@"Data Source={dbPath}");
        }

        public void EnsureRoomAllocationHistoryTableExists()
        {
            try
            {
                var conn = this.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS RoomAllocationHistories (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        RoomAllocationId INTEGER,
                        HotelId INTEGER,
                        HotelName TEXT,
                        RoomNumber TEXT,
                        RoomType TEXT,
                        TourType TEXT,
                        Period TEXT,
                        GroupType TEXT,
                        DaysCount TEXT,
                        PersonId INTEGER,
                        RegistrationId INTEGER,
                        RemovedAt TEXT,
                        Restored INTEGER DEFAULT 0,
                        RestoredAt TEXT
                    );";
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EnsureRoomAllocationHistoryTableExists hata: {ex.Message}");
            }
        }

        public void EnsurePaymentsTableExists()
        {
            try
            {
                var conn = this.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS Payments (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        RegistrationId INTEGER,
                        CreatedAt TEXT,
                        OriginalAmount REAL,
                        OriginalCurrency TEXT,
                        AmountInRegistrationCurrency REAL,
                        Rate REAL,
                        Note TEXT,
                        UploadedDocumentPath TEXT,
                        PaymentMethod TEXT
                    );";
                    cmd.ExecuteNonQuery();
                }

                // Ensure new columns exist for older databases (migrations not always run)
                EnsureColumnExists(conn, "Payments", "PaymentMethod", "TEXT");
                EnsureColumnExists(conn, "Payments", "UploadedDocumentPath", "TEXT");

                // Normalize numeric NULLs to 0 to avoid runtime read errors when model expects non-nullable types
                using (var up = conn.CreateCommand())
                {
                    up.CommandText = "UPDATE Payments SET AmountInRegistrationCurrency = 0 WHERE AmountInRegistrationCurrency IS NULL;";
                    up.ExecuteNonQuery();
                }
                using (var up2 = conn.CreateCommand())
                {
                    up2.CommandText = "UPDATE Payments SET Rate = 0 WHERE Rate IS NULL;";
                    up2.ExecuteNonQuery();
                }
                using (var up3 = conn.CreateCommand())
                {
                    up3.CommandText = "UPDATE Payments SET OriginalAmount = 0 WHERE OriginalAmount IS NULL;";
                    up3.ExecuteNonQuery();
                }
                using (var up4 = conn.CreateCommand())
                {
                    up4.CommandText = "UPDATE Payments SET PaymentMethod = '' WHERE PaymentMethod IS NULL;";
                    up4.ExecuteNonQuery();
                }
                using (var up5 = conn.CreateCommand())
                {
                    up5.CommandText = "UPDATE Payments SET UploadedDocumentPath = '' WHERE UploadedDocumentPath IS NULL;";
                    up5.ExecuteNonQuery();
                }
                using (var up6 = conn.CreateCommand())
                {
                    up6.CommandText = "UPDATE Payments SET Note = '' WHERE Note IS NULL;";
                    up6.ExecuteNonQuery();
                }
                using (var up7 = conn.CreateCommand())
                {
                    up7.CommandText = "UPDATE Payments SET OriginalCurrency = '' WHERE OriginalCurrency IS NULL;";
                    up7.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EnsurePaymentsTableExists hata: {ex.Message}");
            }
        }

        public void EnsureNotificationsTableExists()
        {
            try
            {
                var conn = this.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS Notifications (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Key TEXT,
                        PayloadJson TEXT,
                        Message TEXT,
                        IsRead INTEGER DEFAULT 0,
                        CreatedAt TEXT
                    );";
                    cmd.ExecuteNonQuery();
                }

                // Ensure columns exist for older DBs
                EnsureColumnExists(conn, "Notifications", "PayloadJson", "TEXT");
                EnsureColumnExists(conn, "Notifications", "Message", "TEXT");
                EnsureColumnExists(conn, "Notifications", "IsRead", "INTEGER");
                EnsureColumnExists(conn, "Notifications", "CreatedAt", "TEXT");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EnsureNotificationsTableExists hata: {ex.Message}");
            }
        }

        public void EnsureUsersTableExists()
        {
            try
            {
                var conn = this.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS Users (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Username TEXT NOT NULL UNIQUE,
                        PasswordHash TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        Role TEXT NOT NULL DEFAULT 'user',
                        PermissionsJson TEXT NOT NULL DEFAULT '[]'
                    );";
                    cmd.ExecuteNonQuery();
                }

                // Create unique index for Username if not exists
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_Username ON Users(Username);";
                    cmd.ExecuteNonQuery();
                }

                // Ensure Role and PermissionsJson columns exist for older databases
                EnsureColumnExists(conn, "Users", "Role", "TEXT");
                EnsureColumnExists(conn, "Users", "PermissionsJson", "TEXT");

                // Normalize Role NULLs to 'user' and PermissionsJson to '[]' to avoid runtime issues
                using (var up = conn.CreateCommand())
                {
                    up.CommandText = "UPDATE Users SET Role = 'user' WHERE Role IS NULL OR Role = '';";
                    up.ExecuteNonQuery();
                }
                using (var up2 = conn.CreateCommand())
                {
                    up2.CommandText = "UPDATE Users SET PermissionsJson = '[]' WHERE PermissionsJson IS NULL OR PermissionsJson = '';";
                    up2.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EnsureUsersTableExists hata: {ex.Message}");
            }
        }

        // Eğer Registrations tablosunda PassportArrived sütunu yoksa ekleyen yardımcı metot
        public void EnsureRegistrationPassportArrivedColumnExists()
        {
            try
            {
                var conn = this.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    conn.Open();

                bool hasColumn = false;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA table_info('Registrations');";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            try
                            {
                                var colName = reader.GetString(1);
                                if (string.Equals(colName, "PassportArrived", StringComparison.OrdinalIgnoreCase))
                                {
                                    hasColumn = true;
                                    break;
                                }
                            }
                            catch { }
                        }
                    }
                }

                if (!hasColumn)
                {
                    using (var addCmd = conn.CreateCommand())
                    {
                        addCmd.CommandText = "ALTER TABLE Registrations ADD COLUMN PassportArrived TEXT;";
                        addCmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EnsureRegistrationPassportArrivedColumnExists hata: {ex.Message}");
            }
        }

        // Eğer Persons tablosunda Nationality sütunu yoksa ekleyen yardımcı metot
        public void EnsureRegistrationStatusChoiceColumnsExist()
        {
            try
            {
                var conn = this.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    conn.Open();

                EnsureColumnExists(conn, "Registrations", "VisaStatusChoice", "TEXT");
                EnsureColumnExists(conn, "Registrations", "TicketStatusChoice", "TEXT");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EnsureRegistrationStatusChoiceColumnsExist hata: {ex.Message}");
            }
        }

        public void NormalizeRegistrationNullValues()
        {
            try
            {
                var conn = this.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    conn.Open();

                EnsureValue(conn, "Registrations", "TourType", "");
                EnsureValue(conn, "Registrations", "Period", "");
                EnsureValue(conn, "Registrations", "GroupType", "");
                EnsureValue(conn, "Registrations", "DayCount", "");
                EnsureValue(conn, "Registrations", "VisaStatus", "");
                EnsureValue(conn, "Registrations", "PassportArrived", "-1");
                EnsureValue(conn, "Registrations", "TicketStatus", "");
                EnsureValue(conn, "Registrations", "VisaStatusChoice", "");
                EnsureValue(conn, "Registrations", "TicketStatusChoice", "");
                EnsureValue(conn, "Registrations", "CargoStatus", "");
                EnsureValue(conn, "Registrations", "RoomType", "");
                EnsureValue(conn, "Registrations", "JoinCity", "");
                EnsureValue(conn, "Registrations", "TransferStatus", "");
                EnsureValue(conn, "Registrations", "TransferCity", "");
                EnsureValue(conn, "Registrations", "District", "");
                EnsureValue(conn, "Registrations", "CurrencyType", "");
                EnsureValue(conn, "Registrations", "Reference", "");
                EnsureValue(conn, "Registrations", "Status", "Aktif");
                EnsureNumericValue(conn, "Registrations", "TourPrice", "0");
                EnsureNumericValue(conn, "Registrations", "PaidAmount", "0");
                EnsureNumericValue(conn, "Registrations", "RemainingDebt", "0");
                EnsureDateValue(conn, "Registrations", "RegistrationDate");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NormalizeRegistrationNullValues hata: {ex.Message}");
            }
        }

        private void EnsureValue(DbConnection conn, string tableName, string columnName, string defaultValue)
        {
            if (!HasColumn(conn, tableName, columnName))
                return;

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"UPDATE {tableName} SET {columnName} = @value WHERE {columnName} IS NULL;";
                var param = cmd.CreateParameter();
                param.ParameterName = "@value";
                param.Value = defaultValue;
                cmd.Parameters.Add(param);
                cmd.ExecuteNonQuery();
            }
        }

        private void EnsureNumericValue(DbConnection conn, string tableName, string columnName, string defaultValue)
        {
            if (!HasColumn(conn, tableName, columnName))
                return;

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"UPDATE {tableName} SET {columnName} = @value WHERE {columnName} IS NULL;";
                var param = cmd.CreateParameter();
                param.ParameterName = "@value";
                param.Value = defaultValue;
                cmd.Parameters.Add(param);
                cmd.ExecuteNonQuery();
            }
        }

        private void EnsureDateValue(DbConnection conn, string tableName, string columnName)
        {
            if (!HasColumn(conn, tableName, columnName))
                return;

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"UPDATE {tableName} SET {columnName} = CURRENT_TIMESTAMP WHERE {columnName} IS NULL;";
                cmd.ExecuteNonQuery();
            }
        }

        private bool HasColumn(DbConnection conn, string tableName, string columnName)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info('{tableName}');";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        try
                        {
                            var colName = reader.GetString(1);
                            if (string.Equals(colName, columnName, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                        catch
                        {
                            // ignore malformed row
                        }
                    }
                }
            }

            return false;
        }

        private void EnsureColumnExists(DbConnection conn, string tableName, string columnName, string columnType)
        {
            if (!HasColumn(conn, tableName, columnName))
            {
                using (var addCmd = conn.CreateCommand())
                {
                    addCmd.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType};";
                    addCmd.ExecuteNonQuery();
                }
            }
        }

        public void EnsureNationalityColumnExists()
        {
            try
            {
                var conn = this.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    conn.Open();
                List<string> cols = new List<string>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA table_info('Persons');";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            // PRAGMA table_info returns columns: cid, name, type, notnull, dflt_value, pk
                            // name is at index 1
                            try
                            {
                                if (!reader.IsDBNull(1))
                                {
                                    var colName = reader.GetString(1);
                                    if (!string.IsNullOrEmpty(colName)) cols.Add(colName);
                                }
                            }
                            catch
                            {
                                // ignore malformed row
                            }
                        }
                    }
                }

                // Fallback: if PRAGMA did not work, try reading table SQL
                if (!cols.Any())
                {
                    using (var cmd2 = conn.CreateCommand())
                    {
                        cmd2.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='Persons'";
                        var sql = cmd2.ExecuteScalar() as string;
                        if (!string.IsNullOrEmpty(sql) && sql.IndexOf("Nationality", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            cols.Add("Nationality");
                        }
                    }
                }

                if (!cols.Any(x => string.Equals(x, "Nationality", StringComparison.OrdinalIgnoreCase)))
                {
                    using (var addCmd = conn.CreateCommand())
                    {
                        addCmd.CommandText = "ALTER TABLE Persons ADD COLUMN Nationality TEXT;";
                        addCmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EnsureNationalityColumnExists hata: {ex.Message}");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // RoomAllocation ve RoomOccupant arasındaki ilişkiyi tanımlayalım
            modelBuilder.Entity<RoomOccupant>()
                .HasOne(r => r.RoomAllocation)      // Bir yolcunun bir odası vardır
                .WithMany(a => a.Occupants)         // Bir odanın birçok yolcusu olabilir
                .HasForeignKey(r => r.RoomAllocationId) // Bağlantı anahtarı bu sütundur
                .OnDelete(DeleteBehavior.Cascade);  // Oda silinirse, o odaya atanan yolcular da silinsin
        }
    }
}

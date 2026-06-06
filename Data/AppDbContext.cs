using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data.Common;
using TurizmOperasyonSistemi.Models;


namespace TurizmOperasyonSistemi.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<Person> Persons { get; set; }
        public DbSet<AppSetting> AppSettings { get; set; }
        public DbSet<Registration> Registrations { get; set; }
        public DbSet<Hotel> Hotels { get; set; }
        public DbSet<RoomAllocation> RoomAllocations { get; set; }
        public DbSet<RoomOccupant> RoomOccupants { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite(@"Data Source=C:\TurizmDB\turizm.db");
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

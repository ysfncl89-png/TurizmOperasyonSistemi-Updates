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

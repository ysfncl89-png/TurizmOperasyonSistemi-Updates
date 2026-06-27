using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using TurizmOperasyonSistemi.Data;

namespace TurizmOperasyonSistemi.Services
{
    /// <summary>
    /// Database migration ve versiyonlama işlemlerini yönetir
    /// </summary>
    public class MigrationService
    {
        private readonly AppDbContext _context;
        private static readonly string CurrentVersionString = GetApplicationVersion();

        public MigrationService(AppDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// Uygulamanın mevcut versiyon numarasını döndürür (AssemblyVersion'dan)
        /// </summary>
        public static string GetApplicationVersion()
        {
            try
            {
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return version != null ? version.ToString() : "1.0.0.0";
            }
            catch
            {
                return "1.0.0.0";
            }
        }

        /// <summary>
        /// Database'deki versiyon bilgisini döndürür
        /// </summary>
        public string GetDatabaseVersion()
        {
            try
            {
                // Tablo var mı kontrol et
                var tableExists = _context.GetTableExists("DatabaseVersions");
                if (!tableExists)
                {
                    return null;
                }

                var latestVersion = _context.DatabaseVersions
                    .OrderByDescending(x => x.Id)
                    .FirstOrDefault();

                return latestVersion?.Version;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetDatabaseVersion Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Eksik migration'ları kontrol eder ve çalıştırır
        /// </summary>
        public bool ApplyMigrations()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== Database Migration Process Started ===");
                System.Diagnostics.Debug.WriteLine($"Current Application Version: {CurrentVersionString}");

                // Database'i oluştur (varsa hiçbir şey yapmaz)
                _context.Database.EnsureCreated();
                System.Diagnostics.Debug.WriteLine("Database ensured created.");

                // Bekleyen migration'ları al
                var pendingMigrations = _context.Database.GetPendingMigrations().ToList();

                if (pendingMigrations.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"Found {pendingMigrations.Count} pending migrations:");
                    foreach (var migration in pendingMigrations)
                    {
                        System.Diagnostics.Debug.WriteLine($"  - {migration}");
                    }

                    // Migration'ları uygula
                    _context.Database.Migrate();
                    System.Diagnostics.Debug.WriteLine("Migrations applied successfully.");

                    // Tüm uygulanan migrate'i kaydet
                    foreach (var migration in pendingMigrations)
                    {
                        LogMigration(migration);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("No pending migrations found.");
                }

                // Veritabanı versiyonunu güncelle
                UpdateDatabaseVersion(CurrentVersionString);

                System.Diagnostics.Debug.WriteLine("=== Database Migration Process Completed Successfully ===");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"=== Database Migration Process Failed ===");
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Database versiyonunu kayıt altına alır
        /// </summary>
        private void UpdateDatabaseVersion(string version)
        {
            try
            {
                // Tablonun var olup olmadığını kontrol et
                if (!_context.GetTableExists("DatabaseVersions"))
                {
                    return; // Tablo henüz oluşturulmamış, ilk migration sonrasında oluşturulacak
                }

                var existingVersion = _context.DatabaseVersions
                    .Where(x => x.Version == version)
                    .FirstOrDefault();

                if (existingVersion == null)
                {
                    var dbVersion = new Models.DatabaseVersion
                    {
                        Version = version,
                        CreatedAt = DateTime.Now,
                        Description = $"Database updated to version {version}"
                    };

                    _context.DatabaseVersions.Add(dbVersion);
                    _context.SaveChanges();
                    System.Diagnostics.Debug.WriteLine($"Database version recorded: {version}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateDatabaseVersion Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Migration uygulanmasını kayıt altına alır
        /// </summary>
        private void LogMigration(string migrationName)
        {
            try
            {
                if (!_context.GetTableExists("MigrationLogs"))
                {
                    return;
                }

                var log = new Models.MigrationLog
                {
                    MigrationName = migrationName,
                    AppliedAt = DateTime.Now,
                    Success = true
                };

                _context.MigrationLogs.Add(log);
                _context.SaveChanges();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LogMigration Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Uygulanan tüm migration'ları döndürür
        /// </summary>
        public IEnumerable<string> GetAppliedMigrations()
        {
            try
            {
                return _context.Database.GetMigrations();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Bekleyen migration'ları döndürür
        /// </summary>
        public IEnumerable<string> GetPendingMigrations()
        {
            try
            {
                return _context.Database.GetPendingMigrations();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Tüm özel tabloların var olup olmadığını kontrol et
        /// </summary>
        public void EnsureAllCustomTablesExists()
        {
            try
            {
                _context.EnsureRoomAllocationHistoryTableExists();
                _context.EnsurePaymentsTableExists();
                _context.EnsureNotificationsTableExists();
                _context.EnsureUsersTableExists();

                System.Diagnostics.Debug.WriteLine("All custom tables ensured.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EnsureAllCustomTablesExists Error: {ex.Message}");
            }
        }
    }
}

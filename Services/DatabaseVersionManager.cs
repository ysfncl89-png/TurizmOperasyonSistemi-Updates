using System;
using System.Collections.Generic;
using System.Linq;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Services
{
    /// <summary>
    /// Database versiyonlama bilgisini sorgulamak ve raporlandırmak için yardımcı sınıf
    /// </summary>
    public class DatabaseVersionManager
    {
        private readonly AppDbContext _context;

        public DatabaseVersionManager(AppDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// Veritabanı versiyonu özetini alır
        /// </summary>
        public DatabaseVersionInfo GetVersionInfo()
        {
            try
            {
                var current = GetCurrentVersion();
                var applied = GetAppliedMigrations();
                var pending = GetPendingMigrations();

                return new DatabaseVersionInfo
                {
                    CurrentVersion = current?.Version,
                    UpdatedAt = current?.CreatedAt,
                    TotalAppliedMigrations = applied.Count(),
                    AppliedMigrations = applied.ToList(),
                    PendingMigrations = pending.ToList(),
                    IsUpToDate = !pending.Any()
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetVersionInfo Error: {ex.Message}");
                return new DatabaseVersionInfo { IsUpToDate = false };
            }
        }

        /// <summary>
        /// Güncel veritabanı versiyonunu döndürür
        /// </summary>
        public DatabaseVersion GetCurrentVersion()
        {
            try
            {
                if (!_context.GetTableExists("DatabaseVersions"))
                    return null;

                return _context.DatabaseVersions
                    .OrderByDescending(d => d.CreatedAt)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Son N versiyonu döndürür (sayısız)
        /// </summary>
        public List<DatabaseVersion> GetVersionHistory(int count = 10)
        {
            try
            {
                if (!_context.GetTableExists("DatabaseVersions"))
                    return new List<DatabaseVersion>();

                return _context.DatabaseVersions
                    .OrderByDescending(d => d.CreatedAt)
                    .Take(count)
                    .ToList();
            }
            catch
            {
                return new List<DatabaseVersion>();
            }
        }

        /// <summary>
        /// Uygulanan tüm migration'ları döndürür
        /// </summary>
        public IEnumerable<string> GetAppliedMigrations()
        {
            try
            {
                // MigrationService'den aynı metodu kullanıyoruz
                var ms = new MigrationService(_context);
                return ms.GetAppliedMigrations();
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
                // MigrationService'den aynı metodu kullanıyoruz
                var ms = new MigrationService(_context);
                return ms.GetPendingMigrations();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Migration geçmişini döndürür (başarılı ve başarısız)
        /// </summary>
        public List<MigrationLog> GetMigrationHistory(int count = 50)
        {
            try
            {
                if (!_context.GetTableExists("MigrationLogs"))
                    return new List<MigrationLog>();

                return _context.MigrationLogs
                    .OrderByDescending(m => m.AppliedAt)
                    .Take(count)
                    .ToList();
            }
            catch
            {
                return new List<MigrationLog>();
            }
        }

        /// <summary>
        /// Son başarısız migration'larını döndürür
        /// </summary>
        public List<MigrationLog> GetFailedMigrations()
        {
            try
            {
                if (!_context.GetTableExists("MigrationLogs"))
                    return new List<MigrationLog>();

                return _context.MigrationLogs
                    .Where(m => !m.Success)
                    .OrderByDescending(m => m.AppliedAt)
                    .ToList();
            }
            catch
            {
                return new List<MigrationLog>();
            }
        }

        /// <summary>
        /// Belirli bir versiyonun uygulandığı zamanı döndürür
        /// </summary>
        public DateTime? GetVersionAppliedTime(string version)
        {
            try
            {
                if (!_context.GetTableExists("DatabaseVersions"))
                    return null;

                var dbVersion = _context.DatabaseVersions
                    .Where(d => d.Version == version)
                    .FirstOrDefault();

                return dbVersion?.CreatedAt;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Versiyon güncelleme raporunu oluşturur (admin kullanımı için)
        /// </summary>
        public string GenerateReport()
        {
            var info = GetVersionInfo();
            var history = GetVersionHistory(5);

            var reportLines = new List<string>
            {
                "=== DATABASE VERSION REPORT ===",
                $"Current Version: {info.CurrentVersion ?? "Unknown"}",
                $"Updated At: {info.UpdatedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A"}",
                $"Total Applied Migrations: {info.TotalAppliedMigrations}",
                $"Pending Migrations: {info.PendingMigrations.Count}",
                $"Status: {(info.IsUpToDate ? "✓ UP-TO-DATE" : "⚠ NEEDS UPDATE")}",
                "",
                "Recent Versions:",
            };

            foreach (var v in history)
            {
                reportLines.Add($"  - {v.Version} ({v.CreatedAt:yyyy-MM-dd HH:mm:ss}): {v.Description}");
            }

            if (info.PendingMigrations.Any())
            {
                reportLines.Add("");
                reportLines.Add("Pending Migrations:");
                foreach (var migration in info.PendingMigrations)
                {
                    reportLines.Add($"  - {migration}");
                }
            }

            return string.Join("\n", reportLines);
        }
    }

    /// <summary>
    /// Database versiyonu bilgisi
    /// </summary>
    public class DatabaseVersionInfo
    {
        public string CurrentVersion { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int TotalAppliedMigrations { get; set; }
        public List<string> AppliedMigrations { get; set; } = new List<string>();
        public List<string> PendingMigrations { get; set; } = new List<string>();
        public bool IsUpToDate { get; set; }
    }
}

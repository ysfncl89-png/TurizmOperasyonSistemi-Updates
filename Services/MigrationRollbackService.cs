using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using TurizmOperasyonSistemi.Data;

namespace TurizmOperasyonSistemi.Services
{
    /// <summary>
    /// Database'i önceki versiyonlara geri döndüren servis
    /// </summary>
    public class MigrationRollbackService
    {
        private readonly AppDbContext _context;
        private readonly BackupManager _backupManager;
        private readonly MigrationLogger _logger;
        private readonly ConfigurationProvider _config;

        public MigrationRollbackService(AppDbContext context, string dbPath = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _config = ConfigurationProvider.Instance;

            if (string.IsNullOrEmpty(dbPath))
                dbPath = _config.GetDatabasePath();

            _backupManager = new BackupManager(dbPath);
            _logger = new MigrationLogger(_context, dbPath);
        }

        /// <summary>
        /// Mevcut database versiyonundan önceki sürüme geri döner
        /// </summary>
        public RollbackResult RollbackToPreviousVersion()
        {
            var previousBackup = _backupManager.GetAllBackups().Skip(1).FirstOrDefault();

            if (previousBackup == null)
            {
                return new RollbackResult
                {
                    IsSuccess = false,
                    Message = "Önceki backup bulunamadı. Rollback yapılamaz."
                };
            }

            return RollbackToBackup(previousBackup.Name);
        }

        /// <summary>
        /// Belirtilen backup'tan geri döner
        /// </summary>
        public RollbackResult RollbackToBackup(string backupFileName)
        {
            var result = new RollbackResult();

            try
            {
                System.Diagnostics.Debug.WriteLine($"\n=== ROLLBACK STARTED ===");
                System.Diagnostics.Debug.WriteLine($"Target Backup: {backupFileName}");

                // 1. Rollback lock al
                var migrationLock = new MigrationLock(_context);
                var lockResult = migrationLock.AcquireLock();

                if (!lockResult.IsSuccess)
                {
                    result.IsSuccess = false;
                    result.Message = $"Rollback lock alınamadı: {lockResult.Message}";
                    return result;
                }

                try
                {
                    // 2. Mevcut state'i yedekle (rollback başarısız olursa geri dön diye)
                    var safetyBackup = _backupManager.CreateBackup();
                    if (!safetyBackup.IsSuccess)
                    {
                        System.Diagnostics.Debug.WriteLine("⚠ Safety backup oluşturulamadı, devam ediliyor...");
                    }

                    // 3. Backup'tan restore et
                    var restoreResult = _backupManager.RestoreFromBackup(
                        System.IO.Path.Combine(_config.GetBackupDirectory(), backupFileName)
                    );

                    if (!restoreResult.IsSuccess)
                    {
                        result.IsSuccess = false;
                        result.Message = $"Restore başarısız: {restoreResult.Message}";
                        _logger.LogError("RollbackToBackup", new Exception(restoreResult.Message));
                        return result;
                    }

                    // 4. Database versiyonunu oku (restore edilen backup'tan)
                    string restoredVersion = GetRestoredVersionFromBackup(backupFileName);

                    // 5. Log kaydı yap
                    _logger.LogOperation(
                        "Rollback",
                        $"Database geri döndürüldü: Backup={backupFileName}, Version={restoredVersion}",
                        isSuccess: true
                    );

                    result.IsSuccess = true;
                    result.Message = $"Rollback başarılı. Restored version: {restoredVersion}";
                    result.RestoredVersion = restoredVersion;
                    result.RestoredFrom = backupFileName;

                    System.Diagnostics.Debug.WriteLine($"✓ Rollback successful to version: {restoredVersion}");
                    System.Diagnostics.Debug.WriteLine("=== ROLLBACK COMPLETED ===\n");
                }
                finally
                {
                    // Lock'u bırak
                    migrationLock.ReleaseLock();
                }

                return result;
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"Rollback hatası: {ex.Message}";
                _logger.LogError("RollbackToBackup", ex);
                System.Diagnostics.Debug.WriteLine($"✗ Rollback failed: {ex.Message}\n");
                return result;
            }
        }

        /// <summary>
        /// Belirtilen backup'tan version bilgisini alır
        /// </summary>
            private string GetRestoredVersionFromBackup(string backupFileName)
            {
                try
                {
                    // Attempt to infer version/time from backup filename which is created as:
                    // turizm_backup_yyyyMMdd_HHmmss_fff.bak
                    if (string.IsNullOrEmpty(backupFileName))
                        return "Unknown";

        var name = System.IO.Path.GetFileNameWithoutExtension(backupFileName);
                    const string prefix = "turizm_backup_";
                    if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var timestampPart = name.Substring(prefix.Length);
                        if (DateTime.TryParseExact(timestampPart, "yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt))
                        {
                            return $"Backup taken at {dt:yyyy-MM-dd HH:mm:ss.fff}";
                        }
                    }

                    return "Unknown";
                }
                catch
                {
                    return "Unknown";
                }
            }

        /// <summary>
        /// Tüm mevcut backup'ları döndürür
        /// </summary>
        public List<BackupInfo> GetAvailableBackups()
        {
            return _backupManager.GetAllBackups()
                .Select(f => new BackupInfo
                {
                    FileName = f.Name,
                    Size = f.Length,
                    CreatedAt = f.CreationTime,
                    LastModified = f.LastWriteTime,
                    FullPath = f.FullName
                })
                .ToList();
        }

        /// <summary>
        /// En son backup'ı döndürür
        /// </summary>
        public BackupInfo GetLatestBackup()
        {
            var latest = _backupManager.GetLastBackup();
            if (latest == null)
                return null;

            return new BackupInfo
            {
                FileName = latest.Name,
                Size = latest.Length,
                CreatedAt = latest.CreationTime,
                LastModified = latest.LastWriteTime,
                FullPath = latest.FullName
            };
        }

        /// <summary>
        /// Rollback'ın mümkün olup olmadığını kontrol eder
        /// </summary>
        public bool CanRollback()
        {
            if (!_config.IsRollbackEnabled())
                return false;

            var backups = GetAvailableBackups();
            // Need at least two backups (current + previous) to rollback to a previous state
            return backups.Count > 1;
        }

        /// <summary>
        /// Rollback'ın neden mümkün olmadığını açıklar
        /// </summary>
        public string GetRollbackAvailabilityReason()
        {
            if (!_config.IsRollbackEnabled())
                return "Rollback özelliği configuration'da devre dışı bırakılmış";

            var backups = GetAvailableBackups();
            if (backups.Count == 0)
                return "Hiç backup bulunmuyor";
            if (backups.Count == 1)
                return "Sadece tek bir backup mevcut; rollback için en az iki backup gerekir";

return "Rollback mümkün";
        }
    }

    /// <summary>
    /// Rollback işlem sonuçlarını tutan sınıf
    /// </summary>
    public class RollbackResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public string RestoredVersion { get; set; }
        public string RestoredFrom { get; set; }
        public DateTime RolledBackAt { get; set; } = DateTime.Now;

        public override string ToString()
        {
            return $"[{(IsSuccess ? "SUCCESS" : "FAILED")}] {Message}";
        }
    }
}

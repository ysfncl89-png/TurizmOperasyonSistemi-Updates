using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using Microsoft.EntityFrameworkCore;
using TurizmOperasyonSistemi.Data;

namespace TurizmOperasyonSistemi.Services
{
    /// <summary>
    /// Database migration ve versiyonlama işlemlerini yönetir (Enterprise-grade)
    /// </summary>
    public class MigrationService
    {
        private readonly AppDbContext _context;
        private readonly DatabaseValidator _validator;
        private readonly BackupManager _backupManager;
        private readonly MigrationLock _migrationLock;
        private readonly MigrationLogger _logger;
        private readonly ConfigurationProvider _config;
        private static readonly string CurrentVersionString = GetApplicationVersion();

        public MigrationService(AppDbContext context, string dbPath = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _config = ConfigurationProvider.Instance;

            // ConfigurationProvider'dan dbPath al
            if (string.IsNullOrEmpty(dbPath))
                dbPath = _config.GetDatabasePath();

            _validator = new DatabaseValidator(_context);
            _backupManager = new BackupManager(dbPath);
            _migrationLock = new MigrationLock(_context);
            _logger = new MigrationLogger(_context, dbPath);
        }

        /// <summary>
        /// Uygulamanın mevcut versiyon numarasını döndürür (AssemblyVersion'dan)
        /// </summary>
        public static string GetApplicationVersion()
        {
            try
            {
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                if (version == null) return "1.0.0.0";
                var s = version.ToString();
                // Treat 0.0.0.0 as unset and return sensible default
                if (string.IsNullOrEmpty(s) || s == "0.0.0.0") return "1.0.0.0";
                return s;
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
                _logger.LogError("GetDatabaseVersion", ex);
                return null;
            }
        }

        /// <summary>
        /// Public wrapper: Eksik migration'ları kontrol eder ve çalıştırır. Uzun sürebilecek migration işlemleri için yapılandırılabilir bir timeout uygular.
        /// Eğer timeout aşılırsa işlem iptal edilmiş kabul edilir ve false döndürülür.
        /// </summary>
        public bool ApplyMigrations()
        {
            int timeoutSeconds = _config.GetMigrationTimeoutSeconds();

            try
            {
                var task = System.Threading.Tasks.Task.Run(() => ApplyMigrationsInternal());
                bool completed = task.Wait(TimeSpan.FromSeconds(timeoutSeconds));
                if (!completed)
                {
                    System.Diagnostics.Debug.WriteLine($"✗ Migration process timed out after {timeoutSeconds}s");
                    _logger.LogError("ApplyMigrations", new TimeoutException($"Migration timed out after {timeoutSeconds} seconds"));
                    return false;
                }

                return task.Result;
            }
            catch (AggregateException aex)
            {
                System.Diagnostics.Debug.WriteLine($"✗ Migration task threw: {aex.Flatten().Message}");
                _logger.LogMigrationFailed(CurrentVersionString, aex.Flatten(), TimeSpan.Zero);
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"✗ Migration wrapper failed: {ex.Message}");
                _logger.LogMigrationFailed(CurrentVersionString, ex, TimeSpan.Zero);
                return false;
            }
        }

        /// <summary>
        /// Internal: Eski ApplyMigrations içeriği buraya taşındı. Bu metot doğrudan uzun işlemler gerçekleştirir.
        /// </summary>
        private bool ApplyMigrationsInternal()
        {
            var stopwatch = Stopwatch.StartNew();
            string currentDbVersion = null;

            try
            {
                _logger.LogMigrationStarted(CurrentVersionString, GetDatabaseVersion());
                System.Diagnostics.Debug.WriteLine("=== Database Migration Process Started (Enterprise-Grade) ===");
                System.Diagnostics.Debug.WriteLine($"Current Application Version: {CurrentVersionString}");

                // 1. PRE-MIGRATION VALIDATION
                System.Diagnostics.Debug.WriteLine("\n► Step 1: Pre-Migration Validation");
                string dbPath = _config.GetDatabasePath();
                var validationResult = _validator.ValidateBeforeMigration(dbPath);
                _logger.LogValidation("Pre-Migration Checks", validationResult);

                if (!validationResult.IsValid)
                {
                    System.Diagnostics.Debug.WriteLine($"✗ Validation failed: {validationResult.Message}");
                    _logger.LogError("ApplyMigrations", new Exception(validationResult.Message));
                    return false;
                }
                System.Diagnostics.Debug.WriteLine("✓ All validations passed");

                // 2. ACQUIRE MIGRATION LOCK
                System.Diagnostics.Debug.WriteLine("\n► Step 2: Acquiring Migration Lock");
                var lockResult = _migrationLock.AcquireLock();
                _logger.LogLock("AcquireLock", lockResult);

                if (!lockResult.IsSuccess)
                {
                    System.Diagnostics.Debug.WriteLine($"✗ Lock acquisition failed: {lockResult.Message}");
                    _logger.LogError("ApplyMigrations", new Exception(lockResult.Message));
                    return false;
                }
                System.Diagnostics.Debug.WriteLine($"✓ Lock acquired: {lockResult.LockId}");

                try
                {
                    // 3. CREATE BACKUP (only if enabled in config)
                    System.Diagnostics.Debug.WriteLine("\n► Step 3: Creating Database Backup (if enabled)");

                    if (_config.IsBackupOnStartupEnabled())
                    {
                        var backupResult = _backupManager.CreateBackup();
                        _logger.LogBackup("Pre-Migration Backup", backupResult);

                        if (!backupResult.IsSuccess)
                        {
                            System.Diagnostics.Debug.WriteLine($"⚠ Backup warning: {backupResult.Message}");
                            // Backup başarısız olsa bile devam etmek isterseniz yorum yapın
                            // return false;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"✓ Backup created: {FormatBytes(backupResult.BackupSize)}");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("→ Backup on startup is disabled by configuration; skipping pre-migration backup.");
                        _logger.LogBackup("Pre-Migration Backup (skipped)", new BackupResult { IsSuccess = true, Message = "Skipped due to configuration" });
                    }

                    // 4. GET DATABASE VERSION
                    currentDbVersion = GetDatabaseVersion();
                    System.Diagnostics.Debug.WriteLine($"\n► Step 4: Database Version Check");
                    System.Diagnostics.Debug.WriteLine($"  Current DB Version: {currentDbVersion ?? "None"}");

                    // 5. ENSURE DATABASE EXISTS (migration-safe)
                    System.Diagnostics.Debug.WriteLine("\n► Step 5: Ensuring Database Exists (migration-safe)");
                    // Avoid Database.EnsureCreated() because it bypasses EF Migrations history and may cause schema drift
                    // Use migrations to create the database when absent, and rely on pending migrations for upgrades.
                    bool dbFileExists = true;
                    try
                    {
                        var dbPathLocal = dbPath;
                        dbFileExists = !string.IsNullOrEmpty(dbPathLocal) && System.IO.File.Exists(dbPathLocal);
                    }
                    catch
                    {
                        dbFileExists = true; // conservative fallback
                    }

                    if (!dbFileExists)
                    {
                        System.Diagnostics.Debug.WriteLine("Database file not found; creating database using migrations.");
                        try
                        {
                            _context.Database.Migrate();
                            System.Diagnostics.Debug.WriteLine("✓ Database created via migrations");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"✗ Creating database via migrations failed: {ex.Message}");
                            _logger.LogError("CreateDatabaseViaMigrate", ex);
                            throw;
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("✓ Database exists; will apply pending migrations if any");
                    }

                    // 6. CHECK PENDING MIGRATIONS
                    System.Diagnostics.Debug.WriteLine("\n► Step 6: Checking Pending Migrations");
                    var pendingMigrations = _context.Database.GetPendingMigrations().ToList();
                    bool migrationsWereApplied = false;

                    if (pendingMigrations.Any())
                    {
                        System.Diagnostics.Debug.WriteLine($"Found {pendingMigrations.Count} pending migrations:");
                        foreach (var migration in pendingMigrations)
                        {
                            System.Diagnostics.Debug.WriteLine($"  - {migration}");
                        }

                        // 7. APPLY MIGRATIONS WITH RETRY LOGIC
                        System.Diagnostics.Debug.WriteLine("\n► Step 7: Applying Migrations (with Retry)");
                        if (!ApplyMigrationsWithRetry(pendingMigrations))
                        {
                            System.Diagnostics.Debug.WriteLine("✗ Migration application failed, attempting restore");
                            var restoreResult = _backupManager.RestoreFromLastBackup();
                            _logger.LogBackup("Post-Migration Restore", new BackupResult 
                            { 
                                IsSuccess = restoreResult.IsSuccess, 
                                Message = restoreResult.Message 
                            });

                            if (restoreResult.IsSuccess)
                            {
                                System.Diagnostics.Debug.WriteLine($"✓ Database restored from backup");
                            }
                            return false;
                        }

                        System.Diagnostics.Debug.WriteLine($"✓ All {pendingMigrations.Count} migrations applied successfully");
                        migrationsWereApplied = true;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("✓ No pending migrations found");
                    }

                    // 8. Ensure custom tables
                    System.Diagnostics.Debug.WriteLine("\n► Step 8: Ensuring Custom Tables");
                    EnsureAllCustomTablesExists();
                    System.Diagnostics.Debug.WriteLine("✓ Custom tables ensured");

                    // 9. UPDATE DATABASE VERSION
                    System.Diagnostics.Debug.WriteLine("\n► Step 9: Updating Database Version");
                    UpdateDatabaseVersion(CurrentVersionString);
                    // Also increment manifest DbVersion and update checksum/app version
                    // Only increment when migrations were actually applied or when the DB file was created during this run.
                    try
                    {
                        if (migrationsWereApplied || !dbFileExists)
                        {
                            var manifestService = new ManifestService();
                            var manifest = manifestService.IncrementDbVersion(Environment.MachineName);
                            System.Diagnostics.Debug.WriteLine($"✓ Manifest updated: DbVersion={manifest?.DbVersion}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("→ No manifest increment required (no migrations applied and DB existed). ");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Manifest update failed after migrations: {ex.Message}");
                    }
                    System.Diagnostics.Debug.WriteLine($"✓ Database version updated to: {CurrentVersionString}");

                    stopwatch.Stop();
                    _logger.LogMigrationCompleted(CurrentVersionString, pendingMigrations, stopwatch.Elapsed);
                    System.Diagnostics.Debug.WriteLine($"\n✓ Migration process completed successfully in {stopwatch.Elapsed.TotalSeconds:F2}s");
                    System.Diagnostics.Debug.WriteLine("=== Database Migration Process Completed Successfully (ENTERPRISE) ===\n");

                    return true;
                }
                finally
                {
                    // Release lock
                    _migrationLock.ReleaseLock();
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                System.Diagnostics.Debug.WriteLine($"\n✗ Migration process failed after {stopwatch.Elapsed.TotalSeconds:F2}s");
                System.Diagnostics.Debug.WriteLine($"Exception: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                _logger.LogMigrationFailed(CurrentVersionString, ex, stopwatch.Elapsed);

                // Try to restore from backup
                try
                {
                    System.Diagnostics.Debug.WriteLine("\n⚠ Attempting to restore from backup...");
                    var restoreResult = _backupManager.RestoreFromLastBackup();
                    if (restoreResult.IsSuccess)
                    {
                        System.Diagnostics.Debug.WriteLine("✓ Database successfully restored from backup");
                    }
                }
                catch { }

                System.Diagnostics.Debug.WriteLine("=== Database Migration Process Failed (ENTERPRISE) ===\n");
                return false;
            }
        }

        /// <summary>
        /// Migration'ları retry logic ile uygular
        /// </summary>
        private bool ApplyMigrationsWithRetry(List<string> pendingMigrations)
        {
            int attempt = 0;
            int maxAttempts = _config.GetMaxRetryAttempts();
            int retryDelayMs = _config.GetRetryDelayMs();

            while (attempt < maxAttempts)
            {
                attempt++;

                try
                {
                    System.Diagnostics.Debug.WriteLine($"  Attempting to apply migrations (Attempt {attempt}/{maxAttempts})...");
                    _context.Database.Migrate();

                    // Log each migration
                    foreach (var migration in pendingMigrations)
                    {
                        LogMigration(migration);
                    }

                    System.Diagnostics.Debug.WriteLine($"  ✓ Migrations applied successfully");
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"  ✗ Attempt {attempt} failed: {ex.Message}");

                    if (attempt < maxAttempts)
                    {
                        int delayMs = retryDelayMs * attempt; // Exponential backoff
                        System.Diagnostics.Debug.WriteLine($"  Retrying in {delayMs}ms...");
                        System.Threading.Thread.Sleep(delayMs);
                    }
                }
            }

            return false;
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
                    return; // Tablo henüz oluşturulmamış
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
                    System.Diagnostics.Debug.WriteLine($"  Database version recorded: {version}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateDatabaseVersion Error: {ex.Message}");
                _logger.LogError("UpdateDatabaseVersion", ex);
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
                // Ensure Persons table has newly added columns for older DBs
                try { _context.EnsureNationalityColumnExists(); } catch { }
                // Removed runtime ensures for Aciklama/Musaitlik as columns are being removed in this release

                System.Diagnostics.Debug.WriteLine("All custom tables ensured.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EnsureAllCustomTablesExists Error: {ex.Message}");
                _logger.LogError("EnsureAllCustomTablesExists", ex);
            }
        }

        /// <summary>
        /// Byte cinsinden boyutu insan tarafından okunabilir formata dönüştürür
        /// </summary>
        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }
    }
}

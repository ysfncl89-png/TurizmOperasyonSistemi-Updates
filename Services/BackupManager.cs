using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.Data.Sqlite;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Services
{
    public class BackupManager
    {
        public const string BACKUP_PREFIX = "turizm_backup_";
        public const string BACKUP_EXTENSION = ".bak";

        private readonly string _dbPath;
        private readonly string _backupDirectory;
        private readonly ConfigurationProvider _config;

        public BackupManager(string dbPath = null)
        {
            _config = ConfigurationProvider.Instance;
            _dbPath = string.IsNullOrEmpty(dbPath) ? _config.GetDatabasePath() : dbPath;
            _backupDirectory = string.IsNullOrEmpty(_config.GetBackupDirectory())
                ? Path.Combine(AppContext.BaseDirectory, "backups")
                : _config.GetBackupDirectory();
        }

        private string CreateZipForBackup(string backupPath)
        {
            try
            {
                if (!File.Exists(backupPath)) return null;
                string baseName = Path.GetFileNameWithoutExtension(backupPath);
                string zipName = baseName + ".zip";
                string zipPath = Path.Combine(_backupDirectory, zipName);
                // create a zip containing the backup file at root
                using (var z = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    z.CreateEntryFromFile(backupPath, Path.GetFileName(backupPath), CompressionLevel.Optimal);
                }
                return zipPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreateZipForBackup hata: {ex.Message}");
                return null;
            }
        }

        private void PushBackupToOneDrive(string zipPath)
        {
            try
            {
                var oneDriveBase = _config.GetOneDriveBaseDir();
                if (string.IsNullOrEmpty(oneDriveBase) || !File.Exists(zipPath)) return;

                string backupsDir = Path.Combine(oneDriveBase, "Backups");
                var now = DateTime.Now;
                string year = now.ToString("yyyy");
                string month = now.ToString("MM");
                string targetDir = Path.Combine(backupsDir, year, month);
                Directory.CreateDirectory(targetDir);

                string targetFileName = Path.GetFileName(zipPath);
                string targetPath = Path.Combine(targetDir, targetFileName);

                // If target exists, add timestamp
                if (File.Exists(targetPath))
                {
                    string stamped = Path.GetFileNameWithoutExtension(targetFileName) + "_" + DateTime.Now.ToString("yyyyMMddHHmmss") + Path.GetExtension(targetFileName);
                    targetPath = Path.Combine(targetDir, stamped);
                }

                File.Copy(zipPath, targetPath);
                System.Diagnostics.Debug.WriteLine($"Backup pushed to OneDrive: {targetPath}");

                // After pushing to Backups, also update per-client latest and aggregate manifest
                try
                {
                    var clientId = _config.GetClientId();
                    var clientDir = Path.Combine(oneDriveBase, "Database", clientId);
                    Directory.CreateDirectory(clientDir);

                    // compute zip checksum
                    string zipChecksum = null;
                    try
                    {
                        using (var fs = File.OpenRead(targetPath))
                        using (var sha = SHA256.Create())
                        {
                            var hash = sha.ComputeHash(fs);
                            zipChecksum = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                        }
                    }
                    catch { zipChecksum = null; }

                    // determine db file timestamp from local DB file
                    DateTime? dbFileTs = null;
                    try { dbFileTs = File.GetLastWriteTimeUtc(_dbPath); } catch { dbFileTs = null; }

                    // copy zip as latest per-client
                    string clientLatest = Path.Combine(clientDir, "turizm_latest.zip");
                    try { File.Copy(targetPath, clientLatest, true); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Copy to per-client latest failed: {ex.Message}"); }

                    // Build manifest entry
                    var manifest = new DbManifest
                    {
                        DbVersion = 0,
                        AppVersion = string.Empty,
                        CreatedAt = DateTime.UtcNow,
                        LastModified = DateTime.UtcNow,
                        MachineId = Environment.MachineName,
                        User = Environment.UserName,
                        Checksum = ComputeChecksumOfZipInnerDb(targetPath) ?? string.Empty,
                        DbSize = new FileInfo(targetPath).Length,
                        BackupVersion = 0,
                        BackupCreatedAt = DateTime.UtcNow,
                        DbFileTimestamp = dbFileTs,
                        ZipChecksum = zipChecksum
                    };

                    // Write per-client manifest atomically
                    try
                    {
                        var perClientManifestPath = Path.Combine(clientDir, "manifest.json");
                        AtomicWriteJson(perClientManifestPath, manifest, 3, 200);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Per-client manifest write failed: {ex.Message}");
                    }

                    // Update aggregate manifest
                    try
                    {
                        var aggPath = Path.Combine(oneDriveBase, "manifest.json");
                        Dictionary<string, DbManifest> agg = new Dictionary<string, DbManifest>();
                        if (File.Exists(aggPath))
                        {
                            try
                            {
                                var existing = File.ReadAllText(aggPath);
                                agg = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, DbManifest>>(existing) ?? new Dictionary<string, DbManifest>();
                            }
                            catch { agg = new Dictionary<string, DbManifest>(); }
                        }

                        agg[clientId] = manifest;
                        AtomicWriteJson(aggPath, agg, 5, 300);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Aggregate manifest update failed: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Post-push per-client/manifest actions failed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PushBackupToOneDrive hata: {ex.Message}");
            }
        }

        // Helper to compute checksum of inner DB within zip
        private string ComputeChecksumOfZipInnerDb(string zipPath)
        {
            try
            {
                using (var z = ZipFile.OpenRead(zipPath))
                {
                    var entry = z.Entries.FirstOrDefault(e => e.Name.Equals("turizm.db", StringComparison.OrdinalIgnoreCase) || e.Name.Equals("turizm.sqlite", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase));
                    if (entry == null) return null;
                    using (var s = entry.Open())
                    using (var ms = new MemoryStream())
                    using (var sha = SHA256.Create())
                    {
                        s.CopyTo(ms);
                        ms.Position = 0;
                        var hash = sha.ComputeHash(ms);
                        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    }
                }
            }
            catch { return null; }
        }

        // Use SyncService's atomic writer by composing a small helper
        private void AtomicWriteJson(string path, object obj, int retries = 3, int delayMs = 200)
        {
            try
            {
                var sync = new SyncService();
                sync.AtomicWriteJsonPublic(path, obj, retries, delayMs);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AtomicWriteJson wrapper failed: {ex.Message}");
            }
        }

        public BackupResult CreateBackup()
        {
            var result = new BackupResult();
                // Cleanup stale .tmp backup files from previous interrupted operations (older than 10 minutes)
                CleanupStaleTempFiles();
                // Ensure any SQLite WAL/SHM sidecar files are not left in a locked state by attempting a brief checkpoint
                TryCheckpointWAL();
                // Attempt to recover recent .tmp backups (move to final if valid)
                RecoverTempBackups();
            try
            {
                if (!Directory.Exists(_backupDirectory)) Directory.CreateDirectory(_backupDirectory);
                if (!File.Exists(_dbPath))
                {
                    result.IsSuccess = false;
                    result.Message = $"Database dosyası bulunamadı: {_dbPath}";
                    return result;
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                string backupFileName = $"{BACKUP_PREFIX}{timestamp}{BACKUP_EXTENSION}";
                string backupPath = Path.Combine(_backupDirectory, backupFileName);
                // Create temp file in system TEMP to avoid antivirus / explorer locking the backups folder
                string tempFileName = backupFileName + ".tmp";
                string tempPath = Path.Combine(Path.GetTempPath(), tempFileName);
                string tempShardWal = tempPath + "-wal";
                string tempShardShm = tempPath + "-shm";

                try
                {
                    string ext = Path.GetExtension(_dbPath) ?? string.Empty;
                        if (ext.Equals(".db", StringComparison.OrdinalIgnoreCase) || ext.Equals(".sqlite", StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(tempPath)) File.Delete(tempPath);
                        // Ensure WAL/SHM are flushed by opening a read-write connection briefly before backup
                        try
                        {
                            using (var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadWrite"))
                            {
                                conn.Open();
                                // brief no-op to flush WAL
                            }
                        }
                        catch { }

                        using (var source = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly"))
                        {
                            source.Open();
                            // Try VACUUM INTO to produce a standalone DB file without WAL sidecars (preferred)
                            try
                            {
                                using (var cmd = source.CreateCommand())
                                {
                                    // Escape single quotes in path
                                    var safePath = tempPath.Replace("'", "''");
                                    cmd.CommandText = $"VACUUM INTO '{safePath}';";
                                    cmd.ExecuteNonQuery();
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"VACUUM INTO failed: {ex.Message} - falling back to BackupDatabase API");
                                // Fallback: use BackupDatabase API into a destination connection
                                try
                                {
                                    using (var destination = new SqliteConnection($"Data Source={tempPath}"))
                                    {
                                        destination.Open();
                                        try
                                        {
                                            using (var cmd2 = destination.CreateCommand())
                                            {
                                                cmd2.CommandText = "PRAGMA journal_mode=DELETE;";
                                                cmd2.ExecuteNonQuery();
                                            }
                                        }
                                        catch { }

                                        source.BackupDatabase(destination);

                                        try
                                        {
                                            using (var cmd3 = destination.CreateCommand())
                                            {
                                                cmd3.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                                                cmd3.ExecuteNonQuery();
                                            }
                                        }
                                        catch { }

                                        destination.Close();
                                    }
                                }
                                catch (Exception ex2)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Fallback BackupDatabase failed: {ex2.Message}");
                                }
                            }
                        }

                        // give the OS a moment to flush handles before moving file
                        Thread.Sleep(100);
                        // move with retry to tolerate transient locks
                        bool moved = false;
                        int moveAttempts = 0;
                        while (!moved && moveAttempts < 5)
                        {
                            try
                            {
                                File.Move(tempPath, backupPath);
                                moved = true;
                            }
                            catch (IOException ex)
                            {
                                moveAttempts++;
                                System.Diagnostics.Debug.WriteLine($"Move attempt {moveAttempts} failed: {ex.Message}");
                                Thread.Sleep(200 * moveAttempts);
                            }
                        }
                        if (!moved)
                        {
                            System.Diagnostics.Debug.WriteLine($"Move retries failed, attempting copy-with-flush fallback: {tempPath} -> {backupPath}");
                            try
                            {
                                CopyFileWithFlush(tempPath, backupPath);
                                try { File.Delete(tempPath); } catch { }
                                moved = true;
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Fallback copy failed: {ex.Message} - HResult:{ex.HResult}");
                                // leave temp file for RecoverTempBackups or cleanup later
                            }
                        }
                    }
                    else
                    {
                        if (File.Exists(tempPath)) File.Delete(tempPath);
                        File.Copy(_dbPath, tempPath, true);
                        // move with retry to tolerate transient locks for non-sqlite files
                        bool moved2 = false;
                        int moveAttempts2 = 0;
                        while (!moved2 && moveAttempts2 < 5)
                        {
                            try
                            {
                                File.Move(tempPath, backupPath);
                                moved2 = true;
                            }
                            catch (IOException)
                            {
                                moveAttempts2++;
                                Thread.Sleep(200 * moveAttempts2);
                            }
                        }
                        if (!moved2)
                        {
                            System.Diagnostics.Debug.WriteLine($"Move retries failed for non-sqlite fallback, attempting copy-with-flush: {tempPath} -> {backupPath}");
                            try
                            {
                                CopyFileWithFlush(tempPath, backupPath);
                                try { File.Delete(tempPath); } catch { }
                                moved2 = true;
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Non-sqlite fallback copy failed: {ex.Message}");
                            }
                        }
                    }

                    long size = new FileInfo(backupPath).Length;
                    if (size == 0)
                    {
                        result.IsSuccess = false;
                        result.Message = "Backup dosyası boş oluştu.";
                        try { File.Delete(backupPath); } catch { }
                        return result;
                    }

                    try
                    {
                        using (var fs = File.OpenRead(backupPath))
                        using (var sha = SHA256.Create())
                        {
                            var hash = sha.ComputeHash(fs);
                            var hashHex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                            File.WriteAllText(backupPath + ".sha256", hashHex);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Backup integrity hesaplama başarısız: {ex.Message}");
                    }

                    result.IsSuccess = true;
                    result.Message = $"Backup başarıyla oluşturuldu: {backupFileName}";
                    result.BackupPath = backupPath;
                    result.BackupFileName = backupFileName;
                    result.BackupSize = size;
                    System.Diagnostics.Debug.WriteLine($"Backup oluşturuldu: {backupPath}");
                }
                catch (Exception ex)
                {
                    result.IsSuccess = false;
                    result.Message = $"Backup işlemi başarısız: {ex.Message}";
                    result.Exception = ex;
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                    return result;
                }

                if (result.IsSuccess && _config.IsCompressionEnabled())
                {
                    try
                    {
                        string compressedPath = backupPath + ".gz";
                        using (var originalFileStream = File.OpenRead(backupPath))
                        using (var compressedFileStream = File.Create(compressedPath))
                        using (var compressor = new GZipStream(compressedFileStream, CompressionLevel.Optimal))
                        {
                            originalFileStream.CopyTo(compressor);
                        }

                        try
                        {
                            using (var fs = File.OpenRead(compressedPath))
                            using (var sha = SHA256.Create())
                            {
                                var hash = sha.ComputeHash(fs);
                                var hashHex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                                File.WriteAllText(compressedPath + ".sha256", hashHex);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Compression: integrity hesaplama başarısız: {ex.Message}");
                        }

                        try { if (File.Exists(backupPath)) File.Delete(backupPath); } catch { }
                        try { if (File.Exists(backupPath + ".sha256")) File.Delete(backupPath + ".sha256"); } catch { }

                        result.BackupPath = compressedPath;
                        result.BackupFileName = Path.GetFileName(compressedPath);
                        result.BackupSize = new FileInfo(compressedPath).Length;
                        System.Diagnostics.Debug.WriteLine($"Backup sıkıştırıldı: {compressedPath}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Backup sıkıştırma başarısız: {ex.Message}");
                    }
                }

                // Eğer OneDrive konfigüre edilmişse, backup zip'i OneDrive/Backups/YYYY/MM/ altına kopyala
                try
                {
                    var oneDriveBase = _config.GetOneDriveBaseDir();
                    if (!string.IsNullOrEmpty(oneDriveBase) && result.IsSuccess && File.Exists(result.BackupPath))
                    {
                        string zipPath = CreateZipForBackup(result.BackupPath);
                        if (!string.IsNullOrEmpty(zipPath))
                        {
                            PushBackupToOneDrive(zipPath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"OneDrive push sırasında hata: {ex.Message}");
                }

                // helper: copy with Flush to avoid atomic move issues on some systems/processes
                void CopyFileWithFlush(string source, string destination)
                {
                    using (var inFs = File.OpenRead(source))
                    using (var outFs = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        inFs.CopyTo(outFs);
                        outFs.Flush(true);
                    }
                }

                CleanupOldBackups();
                return result;
            }
            catch (Exception ex)
            {
                return new BackupResult { IsSuccess = false, Message = $"Beklenmeyen hata: {ex.Message}", Exception = ex };
            }
        }

        public RestoreResult RestoreFromBackup(string backupPath)
        {
            var result = new RestoreResult();
            try
            {
                if (!File.Exists(backupPath))
                {
                    result.IsSuccess = false;
                    result.Message = $"Backup dosyası bulunamadı: {backupPath}";
                    return result;
                }

                bool isCompressed = backupPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
                string shaPath = backupPath + ".sha256";
                if (File.Exists(shaPath))
                {
                    try
                    {
                        using (var fs = File.OpenRead(backupPath))
                        using (var sha = SHA256.Create())
                        {
                            var hash = sha.ComputeHash(fs);
                            var hashHex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                            var expected = File.ReadAllText(shaPath).Trim().ToLowerInvariant();
                            if (!string.Equals(hashHex, expected, StringComparison.OrdinalIgnoreCase))
                            {
                                result.IsSuccess = false;
                                result.Message = "Backup dosyası bütünlük kontrolü başarısız (sha mismatch).";
                                return result;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        result.IsSuccess = false;
                        result.Message = $"Backup integrity doğrulaması sırasında hata: {ex.Message}";
                        return result;
                    }
                }

                string sourceToCopy = backupPath;
                string tempDecompressed = null;
                try
                {
                    if (isCompressed)
                    {
                        tempDecompressed = Path.Combine(_backupDirectory, $"decompressed_{Guid.NewGuid()}{BACKUP_EXTENSION}");
                        using (var inFile = File.OpenRead(backupPath))
                        using (var gzip = new GZipStream(inFile, CompressionMode.Decompress))
                        using (var outFile = File.Create(tempDecompressed))
                        {
                            gzip.CopyTo(outFile);
                        }
                        sourceToCopy = tempDecompressed;
                        if (!QuickValidateDatabaseFile(sourceToCopy))
                        {
                            result.IsSuccess = false;
                            result.Message = "Decompressed backup PRAGMA integrity_check failed.";
                            try { if (File.Exists(tempDecompressed)) File.Delete(tempDecompressed); } catch { }
                            return result;
                        }
                    }

                    string brokenDbPath = _dbPath + ".broken_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    try
                    {
                        if (File.Exists(_dbPath))
                        {
                            File.Move(_dbPath, brokenDbPath);
                            System.Diagnostics.Debug.WriteLine($"Bozuk database dosyası taşındı: {brokenDbPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.IsSuccess = false;
                        result.Message = $"Mevcut database dosyası taşınamadı: {ex.Message}";
                        result.Exception = ex;
                        return result;
                    }

                    try
                    {
                        File.Copy(sourceToCopy, _dbPath, true);
                        result.IsSuccess = true;
                        result.Message = $"Database başarıyla restore edildi: {Path.GetFileName(backupPath)}";
                        result.RestoredFromBackup = Path.GetFileName(backupPath);
                        System.Diagnostics.Debug.WriteLine($"Database restore edildi: {_dbPath}");
                        try { File.Delete(brokenDbPath); } catch { }
                    }
                    catch (Exception ex)
                    {
                        result.IsSuccess = false;
                        result.Message = $"Backup dosyası kopyalanamadı: {ex.Message}";
                        result.Exception = ex;
                        try { if (File.Exists(brokenDbPath)) File.Move(brokenDbPath, _dbPath); } catch { }
                        return result;
                    }
                    finally
                    {
                        try { if (tempDecompressed != null && File.Exists(tempDecompressed)) File.Delete(tempDecompressed); } catch { }
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    result.IsSuccess = false;
                    result.Message = $"Restore işlemi sırasında hata: {ex.Message}";
                    result.Exception = ex;
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"Restore işlemi sırasında hata: {ex.Message}";
                result.Exception = ex;
                return result;
            }
        }

        // Restore last available backup (helper for MigrationService)
        public RestoreResult RestoreFromLastBackup()
        {
            try
            {
                var last = GetLastBackup();
                if (last == null)
                {
                    return new RestoreResult { IsSuccess = false, Message = "No backup files found" };
                }

                return RestoreFromBackup(last.FullName);
            }
            catch (Exception ex)
            {
                return new RestoreResult { IsSuccess = false, Message = $"RestoreFromLastBackup failed: {ex.Message}", Exception = ex };
            }
        }

        public List<FileInfo> GetAllBackups()
        {
            try
            {
                if (!Directory.Exists(_backupDirectory)) return new List<FileInfo>();
                var patterns = new[] { BACKUP_PREFIX + "*" + BACKUP_EXTENSION, BACKUP_PREFIX + "*" + BACKUP_EXTENSION + ".gz" };
                var files = new List<FileInfo>();
                foreach (var pattern in patterns) files.AddRange(Directory.GetFiles(_backupDirectory, pattern).Select(p => new FileInfo(p)));
                var backupFiles = files.GroupBy(f => f.FullName).Select(g => g.First()).OrderByDescending(f => f.CreationTime).ToList();
                return backupFiles;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Backup dosyalarını listeleme hatası: {ex.Message}");
                return new List<FileInfo>();
            }
        }

        public FileInfo GetLastBackup()
        {
            return GetAllBackups().FirstOrDefault();
        }

        public bool DeleteBackup(string backupFileName)
        {
            try
            {
                string backupPath = Path.Combine(_backupDirectory, backupFileName);
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                    System.Diagnostics.Debug.WriteLine($"Backup silindi: {backupFileName}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Backup silinirken hata: {ex.Message}");
                return false;
            }
        }

        public long GetBackupDirectorySize()
        {
            try
            {
                if (!Directory.Exists(_backupDirectory)) return 0;
                return Directory.GetFiles(_backupDirectory).Select(p => new FileInfo(p).Length).Sum();
            }
            catch { return 0; }
        }

        private bool QuickValidateDatabaseFile(string filePath)
        {
            try
            {
                using (var conn = new SqliteConnection($"Data Source={filePath};Mode=ReadOnly"))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "PRAGMA integrity_check(1);";
                        var checkResult = cmd.ExecuteScalar()?.ToString() ?? string.Empty;
                        return checkResult.Trim().Equals("ok", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        // Remove stale temporary backup files left from interrupted runs (e.g. *.bak.tmp)
        private void CleanupStaleTempFiles()
                {
                    try
                    {
                        if (!Directory.Exists(_backupDirectory)) return;
                        var pattern = "*" + BACKUP_EXTENSION + ".tmp"; // matches e.g. turizm_backup_... .bak.tmp
                        var files = Directory.GetFiles(_backupDirectory, pattern);
                        var threshold = DateTime.Now.AddMinutes(-10); // older than 10 minutes considered stale
                        foreach (var f in files)
                        {
                            try
                            {
                                var fi = new FileInfo(f);
                                if (fi.CreationTime < threshold)
                                {
                                    fi.Delete();
                                    System.Diagnostics.Debug.WriteLine($"Stale temp backup deleted: {fi.FullName}");
                                }
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"CleanupStaleTempFiles error: {ex.Message}");
                    }
                }

                // Try to checkpoint WAL to flush WAL/SHM files and reduce chance of leftover sidecars
                private void TryCheckpointWAL()
                {
                    try
                    {
                        if (!File.Exists(_dbPath)) return;
                        using (var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadWrite"))
                        {
                            conn.Open();
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"TryCheckpointWAL error: {ex.Message}");
                    }
                }
        private void RecoverTempBackups()
        {
            try
            {
                if (!Directory.Exists(_backupDirectory)) return;
                var pattern = "*" + BACKUP_EXTENSION + ".tmp";
                var files = Directory.GetFiles(_backupDirectory, pattern);
                foreach (var f in files)
                {
                    try
                    {
                        // If temp file seems valid, try to quick-validate and move to final
                        if (QuickValidateDatabaseFile(f))
                        {
                            var final = f.Replace(".tmp", "");
                            if (!File.Exists(final))
                            {
                                File.Move(f, final);
                                System.Diagnostics.Debug.WriteLine($"Recovered temp backup to final: {final}");
                                continue;
                            }
                        }
                        // Not valid or final exists -> delete stale temp
                        try { File.Delete(f); } catch { }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RecoverTempBackups error: {ex.Message}");
            }
        }

        private void CleanupOldBackups()
        {
            try
            {
                if (!Directory.Exists(_backupDirectory)) return;
                var allBackups = GetAllBackups();
                int maxBackups = _config.GetMaxBackups();
                int retentionDays = _config.GetBackupRetentionDays();
                if (retentionDays > 0)
                {
                    var threshold = DateTime.Now.AddDays(-retentionDays);
                    var toDelete = allBackups.Where(f => f.CreationTime < threshold).ToList();
                    foreach (var b in toDelete) try { b.Delete(); } catch { }
                }
                if (maxBackups > 0 && allBackups.Count > maxBackups)
                {
                    var deleteList = allBackups.Skip(maxBackups).ToList();
                    foreach (var b in deleteList) try { b.Delete(); } catch { }
                }
                long maxSizeBytes = (long)_config.GetMaxBackupSizeMB() * 1024 * 1024;
                if (maxSizeBytes > 0)
                {
                    long dirSize = Directory.GetFiles(_backupDirectory).Select(p => new FileInfo(p).Length).Sum();
                    var ordered = GetAllBackups().OrderBy(f => f.CreationTime).ToList();
                    int idx = 0;
                    while (dirSize > maxSizeBytes && idx < ordered.Count)
                    {
                        try
                        {
                            var b = ordered[idx];
                            long len = b.Length;
                            b.Delete();
                            dirSize -= len;
                        }
                        catch { }
                        idx++;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Backup temizleme hatası: {ex.Message}");
            }
        }
    }

    public class BackupResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public string BackupPath { get; set; }
        public string BackupFileName { get; set; }
        public long BackupSize { get; set; }
        public Exception Exception { get; set; }
        public override string ToString() => $"[{(IsSuccess ? "SUCCESS" : "FAILED")}] {Message} - Size: {BackupSize} bytes";
    }

    public class RestoreResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public string RestoredFromBackup { get; set; }
        public Exception Exception { get; set; }
        public override string ToString() => $"[{(IsSuccess ? "SUCCESS" : "FAILED")}] {Message}";
    }

    public class BackupInfo
    {
        public string FileName { get; set; }
        public long Size { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastModified { get; set; }
        public string FullPath { get; set; }
        public override string ToString() => $"{FileName} ({Size} bytes) - Created: {CreatedAt:yyyy-MM-dd HH:mm:ss}";
    }
}

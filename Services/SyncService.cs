using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Services
{
    public class SyncService
    {
        private readonly ConfigurationProvider _config = ConfigurationProvider.Instance;
        private readonly string _oneDriveBase;
        private readonly string _localBase;
        private readonly string _manifestFileName = "manifest.json";
        private readonly string _syncStateFileName = "syncstate.json";

        public SyncService()
        {
            _oneDriveBase = _config.GetOneDriveBaseDir();
            _localBase = _config.GetDefaultBaseDir();
        }

        // Attempts to find a more recent turizm DB in common OneDrive locations and restore it over local DB.
        private bool TryRestoreFromOneDriveLatest()
        {
            try
            {
                var localDb = GetLocalDbPath();
                DateTime localTs = File.Exists(localDb) ? File.GetLastWriteTimeUtc(localDb) : DateTime.MinValue;

                // 1) direct folder: OneDrive/turizm_latest/turizm.db
                var oneDriveLatestDir = Path.Combine(_oneDriveBase, "turizm_latest");
                var directRemoteDb = Path.Combine(oneDriveLatestDir, "turizm.db");
                if (File.Exists(directRemoteDb))
                {
                    var remoteTs = File.GetLastWriteTimeUtc(directRemoteDb);
                    if (remoteTs > localTs)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(localDb) ?? string.Empty);
                        if (File.Exists(localDb))
                        {
                            var archived = localDb + ".backup." + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                            try { File.Move(localDb, archived); } catch { }
                        }
                        CopyWithRetries(directRemoteDb, localDb, 3, 200);
                        WriteSyncState(new SyncState { LastSyncAt = DateTime.UtcNow, LastSyncMachine = Environment.MachineName, LastSyncDbVersion = ComputeLocalDbVersion(), Status = "RestoredFromOneDrive_turizm_latest_direct" });
                        return true;
                    }
                }

                // helper to attempt restore from zip: returns true if restored
                bool TryRestoreFromZip(string zipPath)
                {
                    try
                    {
                        if (!File.Exists(zipPath)) return false;
                        using (var z = System.IO.Compression.ZipFile.OpenRead(zipPath))
                        {
                            var entry = z.Entries.FirstOrDefault(e => e.Name.Equals("turizm.db", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".db", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase));
                            if (entry == null) return false;
                            DateTime entryTs = DateTime.MinValue;
                            try { entryTs = entry.LastWriteTime.UtcDateTime; } catch { }
                            if (entryTs == DateTime.MinValue)
                            {
                                // fallback to zip file timestamp
                                entryTs = File.GetLastWriteTimeUtc(zipPath);
                            }
                            if (entryTs <= localTs) return false;

                            // extract entry to temp and copy over local
                            var tmp = Path.Combine(Path.GetTempPath(), $"turizm_restore_{Guid.NewGuid():N}.db");
                            try
                            {
                                entry.ExtractToFile(tmp, true);
                                Directory.CreateDirectory(Path.GetDirectoryName(localDb) ?? string.Empty);
                                if (File.Exists(localDb))
                                {
                                    var archived = localDb + ".backup." + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                                    try { File.Move(localDb, archived); } catch { }
                                }
                                CopyWithRetries(tmp, localDb, 3, 200);
                                WriteSyncState(new SyncState { LastSyncAt = DateTime.UtcNow, LastSyncMachine = Environment.MachineName, LastSyncDbVersion = ComputeLocalDbVersion(), Status = "RestoredFromOneDrive_turizm_latest_zip" });
                                try { File.Delete(tmp); } catch { }
                                return true;
                            }
                            catch (Exception ex)
                            {
                                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                                System.Diagnostics.Debug.WriteLine($"Restore from zip failed: {ex.Message}");
                                return false;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"TryRestoreFromZip error: {ex.Message}");
                    }
                    return false;
                }

                // 2) root zip: OneDrive/turizm_latest.zip
                var rootZip = Path.Combine(_oneDriveBase, "turizm_latest.zip");
                if (TryRestoreFromZip(rootZip)) return true;

                // 3) per-client Database/*/turizm_latest.zip
                var dbRoot = Path.Combine(_oneDriveBase, "Database");
                if (Directory.Exists(dbRoot))
                {
                    var clientDirs = Directory.GetDirectories(dbRoot);
                    // prefer newest zip by LastWriteTimeUtc
                    var candidateZips = new List<string>();
                    foreach (var cd in clientDirs)
                    {
                        var p1 = Path.Combine(cd, "turizm_latest.zip");
                        if (File.Exists(p1)) candidateZips.Add(p1);
                        else
                        {
                            var any = Directory.GetFiles(cd, "turizm_latest*.zip");
                            if (any != null && any.Length > 0) candidateZips.AddRange(any);
                        }
                    }
                    // order by file write time descending
                    foreach (var zip in candidateZips.OrderByDescending(p => File.GetLastWriteTimeUtc(p)))
                    {
                        if (TryRestoreFromZip(zip)) return true;
                    }
                }

                // 4) Backups folder search for recent zip that might contain turizm.db
                var backupsRoot = Path.Combine(_oneDriveBase, "Backups");
                if (Directory.Exists(backupsRoot))
                {
                    var zips = Directory.GetFiles(backupsRoot, "*.zip", SearchOption.AllDirectories)
                        .OrderByDescending(p => File.GetLastWriteTimeUtc(p)).ToList();
                    foreach (var zip in zips)
                    {
                        if (Path.GetFileName(zip).IndexOf("turizm", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (TryRestoreFromZip(zip)) return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TryRestoreFromOneDriveLatest exception: {ex.Message}");
            }
            return false;
        }

        public async Task PerformShutdownSyncAsync()
        {
            // Ensure we have OneDrive configured; still perform local backup even if not
            string dbPath = _config.GetDatabasePath();

            try
            {
                // Try to flush WAL and close connections gracefully
                try
                {
                    // Attempt checkpoint
                    using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Mode=ReadWrite"))
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
                    System.Diagnostics.Debug.WriteLine($"WAL checkpoint failed during shutdown: {ex.Message}");
                }

                // Give the runtime a moment to finalize any pending disposals
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(200).ConfigureAwait(false);

                // Create a local backup (BackupManager handles zip + optional OneDrive push)
                try
                {
                    var backupManager = new BackupManager(dbPath);
                    var backupResult = backupManager.CreateBackup();
                    System.Diagnostics.Debug.WriteLine($"Shutdown backup result: {backupResult}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Shutdown backup failed: {ex.Message}");
                }

                // Update local manifest metadata (checksum, size, lastmodified)
                try
                {
                    var manifestService = new ManifestService();
                    manifestService.UpdateMetadata();

                    // Push manifest to OneDrive (copy manifest.json and syncstate)
                    if (!string.IsNullOrEmpty(_oneDriveBase))
                    {
                        try
                        {
                            Directory.CreateDirectory(_oneDriveBase);
                            var localManifest = Path.Combine(_localBase, "manifest.json");
                            var remoteManifest = Path.Combine(_oneDriveBase, "manifest.json");
                            if (File.Exists(localManifest)) File.Copy(localManifest, remoteManifest, true);

                            var localSync = Path.Combine(_localBase, "syncstate.json");
                            var remoteSync = Path.Combine(_oneDriveBase, "syncstate.json");
                            if (File.Exists(localSync)) File.Copy(localSync, remoteSync, true);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Push manifest to OneDrive failed: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Manifest update on shutdown failed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PerformShutdownSyncAsync error: {ex.Message}");
            }
        }

        public async Task StartInitialSyncAsync()
        {
            // Eğer OneDrive yoksa atla
            if (string.IsNullOrEmpty(_oneDriveBase))
                return;

            // Wait for OneDrive to stabilize (basic polling)
            await WaitForOneDriveReadyAsync().ConfigureAwait(false);

            // Try robust restore from OneDrive "turizm_latest" (support turizm.db or turizm_latest.zip in common locations)
            try
            {
                if (TryRestoreFromOneDriveLatest())
                {
                    // restored successfully, stop further sync decisions
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"turizm_latest restore attempt failed: {ex.Message}");
            }

            var localManifest = ReadLocalManifest();

            // Read aggregate remote manifests (per-client) from OneDrive root
            Dictionary<string, DbManifest> remoteAggregate = null;
            try
            {
                var aggPath = Path.Combine(_oneDriveBase, _manifestFileName);
                if (File.Exists(aggPath))
                {
                    var aggJson = File.ReadAllText(aggPath);
                    remoteAggregate = JsonSerializer.Deserialize<Dictionary<string, DbManifest>>(aggJson);
                }
            }
            catch { remoteAggregate = null; }

            if (localManifest == null && (remoteAggregate == null || remoteAggregate.Count == 0))
                return; // hiçbir manifest yoksa işlem yok

            try
            {
                if (remoteAggregate != null && remoteAggregate.Count > 0)
                {
                    // find the remote entry with highest DbVersion
                    var best = remoteAggregate.OrderByDescending(kv => kv.Value?.DbVersion ?? 0).First();
                    var remoteClientId = best.Key;
                    var remoteManifest = best.Value;

                    if (remoteManifest != null && (localManifest == null || remoteManifest.DbVersion > localManifest.DbVersion))
                    {
                        // Remote daha yeni -> restore to local from remote client's folder
                        RestoreDatabaseFromOneDrive(remoteClientId);
                        WriteSyncState(new SyncState { LastSyncAt = DateTime.UtcNow, LastSyncMachine = remoteManifest.MachineId, LastSyncDbVersion = remoteManifest.DbVersion, Status = "Restored from OneDrive" });
                        return;
                    }

                    if (localManifest != null && (remoteManifest == null || localManifest.DbVersion > remoteManifest.DbVersion))
                    {
                        // Local daha yeni -> push backup to OneDrive
                        await PushLocalDatabaseToOneDriveAsync().ConfigureAwait(false);
                        WriteSyncState(new SyncState { LastSyncAt = DateTime.UtcNow, LastSyncMachine = Environment.MachineName, LastSyncDbVersion = localManifest.DbVersion, Status = "Pushed to OneDrive" });
                        return;
                    }

                    if (localManifest != null && remoteManifest != null && localManifest.DbVersion == remoteManifest.DbVersion)
                    {
                        // Aynı versiyon ama checksum mismatch ise uyar
                        var localChecksum = ComputeChecksum(GetLocalDbPath());
                        if (!string.Equals(localChecksum, remoteManifest.Checksum, StringComparison.OrdinalIgnoreCase))
                        {
                            WriteSyncState(new SyncState { LastSyncAt = DateTime.UtcNow, LastSyncMachine = Environment.MachineName, LastSyncDbVersion = localManifest.DbVersion, Status = "ChecksumMismatch", Details = "Local and remote checksums differ" });
                        }
                        return;
                    }
                }
                else if (localManifest != null)
                {
                    // No remote manifests found but local exists -> push
                    await PushLocalDatabaseToOneDriveAsync().ConfigureAwait(false);
                    WriteSyncState(new SyncState { LastSyncAt = DateTime.UtcNow, LastSyncMachine = Environment.MachineName, LastSyncDbVersion = localManifest.DbVersion, Status = "Pushed to OneDrive" });
                }

            }
            catch (Exception ex)
            {
                WriteSyncState(new SyncState { LastSyncAt = DateTime.UtcNow, LastSyncMachine = Environment.MachineName, LastSyncDbVersion = localManifest?.DbVersion ?? -1, Status = "Error", Details = ex.Message });
            }
        }

        public async Task WaitForOneDriveReadyAsync(int retries = 10, int delayMs = 1000)
        {
            if (string.IsNullOrEmpty(_oneDriveBase))
                return;

            string remoteManifestPath = Path.Combine(_oneDriveBase, _manifestFileName);
            int stableCount = 0;
            long lastSize = -1;

            for (int i = 0; i < retries; i++)
            {
                try
                {
                    if (!File.Exists(remoteManifestPath))
                    {
                await Task.Delay(delayMs).ConfigureAwait(false);
                        continue;
                    }

                    var fi = new FileInfo(remoteManifestPath);
                    if (fi.Length == lastSize)
                    {
                        stableCount++;
                    }
                    else
                    {
                        stableCount = 0;
                        lastSize = fi.Length;
                    }

                    if (stableCount >= 2)
                        return;
                }
                catch
                {
                    // ignore and retry
                }

                await Task.Delay(delayMs).ConfigureAwait(false);
            }
        }

        private DbManifest ReadLocalManifest()
        {
            try
            {
                var path = Path.Combine(_localBase, _manifestFileName);
                if (!File.Exists(path)) return null;
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<DbManifest>(json);
            }
            catch { return null; }
        }



        private void WriteLocalManifest(DbManifest manifest)
        {
            try
            {
                Directory.CreateDirectory(_localBase);
                var path = Path.Combine(_localBase, _manifestFileName);
                var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { }
        }

        private void WriteSyncState(SyncState state)
        {
            try
            {
                Directory.CreateDirectory(_localBase);
                var path = Path.Combine(_localBase, _syncStateFileName);
                var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { }
        }

        private string GetLocalDbPath()
        {
            return _config.GetDatabasePath();
        }

        private string GetRemoteDbPath()
        {
            if (string.IsNullOrEmpty(_oneDriveBase)) return null;
            return Path.Combine(_oneDriveBase, Path.GetFileName(GetLocalDbPath()));
        }

        private void RestoreDatabaseFromOneDrive(string remoteClientId)
        {
            try
            {
                var clientDir = Path.Combine(_oneDriveBase, "Database", remoteClientId);
                // Prefer per-client latest zip
                string zipPath = null;
                try
                {
                    if (Directory.Exists(clientDir))
                    {
                        var zips = Directory.GetFiles(clientDir, "*.zip").Select(p => new FileInfo(p)).OrderByDescending(f => f.LastWriteTimeUtc).ToList();
                        if (zips.Any()) zipPath = zips.First().FullName;
                    }
                }
                catch { zipPath = null; }

                // fallback: search OneDrive Backups for client-specific zips
                if (zipPath == null)
                {
                    try
                    {
                        var backupsRoot = Path.Combine(_oneDriveBase, "Backups");
                        if (Directory.Exists(backupsRoot))
                        {
                            var zips = Directory.GetFiles(backupsRoot, "*.zip", SearchOption.AllDirectories)
                                .Where(p => Path.GetFileName(p).Contains(remoteClientId) || true)
                                .Select(p => new FileInfo(p)).OrderByDescending(f => f.LastWriteTimeUtc).ToList();
                            if (zips.Any()) zipPath = zips.First().FullName;
                        }
                    }
                    catch { zipPath = null; }
                }

                if (zipPath == null) return;

                // Extract zip to temp and copy contained turizm.db to local
                var extractDir = Path.Combine(Path.GetTempPath(), "turizm_restore_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(extractDir);
                try
                {
                    ZipFile.ExtractToDirectory(zipPath, extractDir);
                    var extractedDb = Directory.GetFiles(extractDir, "turizm.db", SearchOption.AllDirectories).FirstOrDefault();
                    if (extractedDb == null) return;

                    var local = GetLocalDbPath();
                    Directory.CreateDirectory(Path.GetDirectoryName(local) ?? string.Empty);
                    if (File.Exists(local))
                    {
                        string archived = local + ".backup." + DateTime.Now.ToString("yyyyMMddHHmmss");
                        File.Move(local, archived);
                    }
                    File.Copy(extractedDb, local, true);
                }
                finally
                {
                    try { Directory.Delete(extractDir, true); } catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Restore error: {ex.Message}");
            }
        }

        private async Task PushLocalDatabaseToOneDriveAsync()
        {
            var remoteRoot = _oneDriveBase;
            var local = GetLocalDbPath();

            try
            {
                if (string.IsNullOrEmpty(remoteRoot)) return;

                var clientId = _config.GetClientId();
                var remoteClientDir = Path.Combine(remoteRoot, "Database", clientId);
                Directory.CreateDirectory(remoteClientDir);

                // create manifest from local
                var manifest = new DbManifest
                {
                    DbVersion = ComputeLocalDbVersion(),
                    AppVersion = "",
                    CreatedAt = DateTime.UtcNow,
                    LastModified = DateTime.UtcNow,
                    MachineId = Environment.MachineName,
                    User = Environment.UserName,
                    Checksum = ComputeChecksum(local) ?? string.Empty,
                    DbSize = new FileInfo(local).Length,
                    BackupVersion = 0
                };

                // Create zip of local DB to temp
                string tempZip = CreateTempZipFromFile(local, "turizm.db");
                if (tempZip == null)
                {
                    System.Diagnostics.Debug.WriteLine("Creating temp zip failed; aborting push.");
                    return;
                }

                // Copy zip to OneDrive Backups/YYYY/MM
                try
                {
                    var now = DateTime.Now;
                    string backupsDir = Path.Combine(remoteRoot, "Backups", now.ToString("yyyy"), now.ToString("MM"));
                    Directory.CreateDirectory(backupsDir);
                    string backupFileName = $"turizm_{manifest.DbVersion}_{now:yyyyMMdd_HHmmss}.zip";
                    string backupTarget = Path.Combine(backupsDir, backupFileName);
                    CopyWithRetries(tempZip, backupTarget, 3, 200);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Copy to OneDrive backups failed: {ex.Message}");
                }

                // Also copy zip to per-client folder as latest
                try
                {
                    string clientZipTarget = Path.Combine(remoteClientDir, "turizm_latest.zip");
                    CopyWithRetries(tempZip, clientZipTarget, 3, 200);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Copy to per-client folder failed: {ex.Message}");
                }

                // Write per-client manifest atomically
                try
                {
                    var perClientManifestPath = Path.Combine(remoteClientDir, _manifestFileName);
                    AtomicWriteJson(perClientManifestPath, manifest, 3, 200);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Write per-client manifest failed: {ex.Message}");
                }

                // Update aggregate manifest at OneDrive root atomically
                try
                {
                    var aggPath = Path.Combine(remoteRoot, _manifestFileName);
                    Dictionary<string, DbManifest> agg = new Dictionary<string, DbManifest>();
                    if (File.Exists(aggPath))
                    {
                        try
                        {
                            var existing = File.ReadAllText(aggPath);
                            agg = JsonSerializer.Deserialize<Dictionary<string, DbManifest>>(existing) ?? new Dictionary<string, DbManifest>();
                        }
                        catch { agg = new Dictionary<string, DbManifest>(); }
                    }

                    agg[clientId] = manifest;
                    AtomicWriteJson(aggPath, agg, 5, 300);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Updating aggregate manifest failed: {ex.Message}");
                }

                try { File.Delete(tempZip); } catch { }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Push to OneDrive failed: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        private string CreateTempZipFromFile(string sourceFile, string entryName)
        {
            try
            {
                if (!File.Exists(sourceFile)) return null;
                string tempZip = Path.Combine(Path.GetTempPath(), $"turizm_{Guid.NewGuid()}.zip");
                using (var z = ZipFile.Open(tempZip, ZipArchiveMode.Create))
                {
                    z.CreateEntryFromFile(sourceFile, entryName, CompressionLevel.Optimal);
                }
                // Ensure temp zip includes file metadata timestamp by setting entry's LastWriteTime
                try
                {
                    using (var z = ZipFile.Open(tempZip, ZipArchiveMode.Update))
                    {
                        var e = z.Entries.FirstOrDefault();
                        if (e != null)
                        {
                            e.LastWriteTime = File.GetLastWriteTimeUtc(sourceFile);
                        }
                    }
                }
                catch { }
                return tempZip;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreateTempZipFromFile error: {ex.Message}");
                return null;
            }
        }

        // Compute checksum of the inner DB file in a zip without extracting fully
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

        private void CopyWithRetries(string source, string dest, int retries = 3, int delayMs = 200)
        {
            int attempt = 0;
            while (attempt < retries)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? string.Empty);
                    File.Copy(source, dest, true);
                    return;
                }
                catch (IOException)
                {
                    attempt++;
                    Thread.Sleep(delayMs * attempt);
                }
            }
            // final attempt
            File.Copy(source, dest, true);
        }

        private void AtomicWriteJson<T>(string path, T obj, int retries = 3, int delayMs = 200)
        {
            var dir = Path.GetDirectoryName(path) ?? string.Empty;
            Directory.CreateDirectory(dir);
            string tempPath = path + ".tmp." + Guid.NewGuid().ToString("N");
            string json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(tempPath, json);

            int attempt = 0;
            while (true)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        // use Replace for atomic overwrite
                        string backup = path + ".bak";
                        File.Replace(tempPath, path, backup);
                        try { File.Delete(backup); } catch { }
                    }
                    else
                    {
                        File.Move(tempPath, path);
                    }
                    break;
                }
                catch (IOException)
                {
                    attempt++;
                    if (attempt >= retries) throw;
                    Thread.Sleep(delayMs * attempt);
                }
            }
        }

        private long ComputeLocalDbVersion()
        {
            // Simple heuristic: use manifest DbVersion if exists, else 0.
            var m = ReadLocalManifest();
            return m?.DbVersion ?? 0;
        }

        private string ComputeChecksum(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;
                using var sha = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                var hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch { return null; }
        }

        // Expose AtomicWriteJson for other services (e.g., BackupManager) by making it public
        public void AtomicWriteJsonPublic<T>(string path, T obj, int retries = 3, int delayMs = 200)
        {
            AtomicWriteJson(path, obj, retries, delayMs);
        }
    }
}

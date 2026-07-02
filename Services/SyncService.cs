using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
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
                var remote = Path.Combine(_oneDriveBase, "Database", remoteClientId, Path.GetFileName(GetLocalDbPath()));
                var local = GetLocalDbPath();
                if (!File.Exists(remote)) return;
                // Ensure DB not open by app. We'll copy and overwrite local.
                Directory.CreateDirectory(Path.GetDirectoryName(local) ?? string.Empty);
                // Backup existing local
                if (File.Exists(local))
                {
                    string archived = local + ".backup." + DateTime.Now.ToString("yyyyMMddHHmmss");
                    File.Move(local, archived);
                }
                File.Copy(remote, local, true);
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

                // Copy DB into per-client folder
                var remoteDb = Path.Combine(remoteClientDir, Path.GetFileName(local));
                File.Copy(local, remoteDb, true);

                // Write per-client manifest in that folder
                var perClientManifestPath = Path.Combine(remoteClientDir, _manifestFileName);
                var perJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(perClientManifestPath, perJson);

                // Update aggregate manifest at OneDrive root: Dictionary<ClientId, DbManifest>
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
                    var aggJson = JsonSerializer.Serialize(agg, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(aggPath, aggJson);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Updating aggregate manifest failed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Push to OneDrive failed: {ex.Message}");
            }

            await Task.CompletedTask;
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
    }
}

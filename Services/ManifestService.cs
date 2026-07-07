using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Services
{
    public class ManifestService
    {
        private readonly ConfigurationProvider _config = ConfigurationProvider.Instance;
        private readonly string _localBase;
        private readonly string _manifestPath;
        private readonly object _fileLock = new object();

        public ManifestService()
        {
            _localBase = _config.GetDefaultBaseDir();
            _manifestPath = Path.Combine(_localBase, "manifest.json");
        }

        public DbManifest LoadManifest()
        {
            try
            {
                if (!File.Exists(_manifestPath)) return null;
                var json = File.ReadAllText(_manifestPath);
                // Try to deserialize into current DbManifest shape
                try
                {
                    var manifest = JsonSerializer.Deserialize<DbManifest>(json);
                    // Backwards-compat: older code wrote 'Version' property instead of 'DbVersion'.
                    // If DbVersion is zero but JSON contains 'Version', map it.
                    if (manifest != null && manifest.DbVersion == 0)
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("Version", out var v) && v.ValueKind == JsonValueKind.Number)
                            {
                                if (v.TryGetInt64(out var ver)) manifest.DbVersion = ver;
                            }
                        }
                        catch { }
                    }

                    return manifest;
                }
                catch
                {
                    // Fallback: try to parse legacy manifest with 'Version' field
                    try
                    {
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        var legacy = new DbManifest();
                        if (root.TryGetProperty("Version", out var ver) && ver.ValueKind == JsonValueKind.Number)
                        {
                            if (ver.TryGetInt64(out var v)) legacy.DbVersion = v;
                        }

                        if (root.TryGetProperty("MachineId", out var mid) && mid.ValueKind == JsonValueKind.String)
                            legacy.MachineId = mid.GetString();
                        if (root.TryGetProperty("LastModified", out var lm) && lm.ValueKind == JsonValueKind.String && DateTime.TryParse(lm.GetString(), out var dt))
                            legacy.LastModified = dt;
                        if (root.TryGetProperty("Hash", out var h) && h.ValueKind == JsonValueKind.String)
                            legacy.Checksum = h.GetString();

                        return legacy;
                    }
                    catch
                    {
                        return null;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        public void UpdateMetadata()
        {
            try
            {
                lock (_fileLock)
                {
                    var manifest = LoadManifest() ?? new DbManifest();
                    manifest.LastModified = DateTime.UtcNow;
                    manifest.AppVersion = GetAppVersion();
                    manifest.MachineId = Environment.MachineName;
                    manifest.User = Environment.UserName;

                    try
                    {
                        var dbPath = _config.GetDatabasePath();
                        if (File.Exists(dbPath))
                        {
                            manifest.Checksum = ComputeChecksum(dbPath) ?? manifest.Checksum;
                            manifest.DbSize = new FileInfo(dbPath).Length;
                            manifest.DbFileTimestamp = File.GetLastWriteTimeUtc(dbPath);
                            // Not setting BackupCreatedAt/ZipChecksum here; they will be set by BackupManager when pushing zips
                        }
                    }
                    catch { }

                    SaveManifest(manifest);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateMetadata error: {ex.Message}");
            }
        }

        public void SaveManifest(DbManifest manifest)
        {
            try
            {
                Directory.CreateDirectory(_localBase);
                var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
                lock (_fileLock)
                {
                    File.WriteAllText(_manifestPath, json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveManifest error: {ex.Message}");
            }
        }

        public DbManifest IncrementDbVersion(string machineId = null)
        {
            try
            {
                lock (_fileLock)
                {
                    var manifest = LoadManifest() ?? new DbManifest
                    {
                        DbVersion = 0,
                        AppVersion = GetAppVersion(),
                        CreatedAt = DateTime.UtcNow,
                        MachineId = machineId ?? Environment.MachineName,
                        User = Environment.UserName
                    };

                    manifest.DbVersion++;
                    manifest.LastModified = DateTime.UtcNow;
                    manifest.AppVersion = GetAppVersion();
                    manifest.MachineId = machineId ?? Environment.MachineName;
                    manifest.User = Environment.UserName;

                    // Update checksum and size
                    try
                    {
                        var dbPath = _config.GetDatabasePath();
                        if (File.Exists(dbPath))
                        {
                            manifest.Checksum = ComputeChecksum(dbPath) ?? manifest.Checksum;
                            manifest.DbSize = new FileInfo(dbPath).Length;
                        }
                    }
                    catch { }

                    SaveManifest(manifest);
                    return manifest;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"IncrementDbVersion error: {ex.Message}");
                return null;
            }
        }

        private string ComputeChecksum(string filePath)
        {
            try
            {
                using var sha = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                var hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch { return null; }
        }

        private string GetAppVersion()
        {
            try
            {
                var version = Assembly.GetEntryAssembly()?.GetName().Version ?? Assembly.GetExecutingAssembly().GetName().Version;
                var s = version?.ToString();
                if (string.IsNullOrEmpty(s) || s == "0.0.0.0") return "1.0.0.0";
                return s;
            }
            catch { return "1.0.0.0"; }
        }

        // Static helper for callers that don't have an instance
        public static string GetAppVersionSafe()
        {
            try
            {
                var version = Assembly.GetEntryAssembly()?.GetName().Version ?? Assembly.GetExecutingAssembly().GetName().Version;
                var s = version?.ToString();
                return string.IsNullOrEmpty(s) ? "1.0.0.0" : s;
            }
            catch { return string.Empty; }
        }
    }
}

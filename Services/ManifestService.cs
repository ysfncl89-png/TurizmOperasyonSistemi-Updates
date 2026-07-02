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
                return JsonSerializer.Deserialize<DbManifest>(json);
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
                return version?.ToString() ?? string.Empty;
            }
            catch { return string.Empty; }
        }
    }
}

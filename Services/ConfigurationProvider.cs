using System;
using System.IO;
using System.Text.Json;

namespace TurizmOperasyonSistemi.Services
{
    /// <summary>
    /// appsettings.json dosyasından konfigürasyon okuyan servis
    /// </summary>
    public class ConfigurationProvider
    {
        private static ConfigurationProvider _instance;
        private readonly string _configPath;
        private JsonElement _configRoot;

        private ConfigurationProvider(string configPath = null)
        {
            // Eğer path belirtilmemişse, default path kullan
            _configPath = configPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "appsettings.json");
            Load();
        }

        /// <summary>
        /// Singleton instance'ı döndürür
        /// </summary>
        public static ConfigurationProvider Instance => 
            _instance ??= new ConfigurationProvider();

        /// <summary>
        /// Özel config path'i ile singleton oluştur
        /// </summary>
        public static void Initialize(string configPath)
        {
            _instance = new ConfigurationProvider(configPath);
        }

        /// <summary>
        /// Configuration dosyasını yükler
        /// </summary>
        private void Load()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    System.Diagnostics.Debug.WriteLine($"Config dosyası bulunamadı: {_configPath}");
                    System.Diagnostics.Debug.WriteLine("Default değerleri kullanılıyor...");
                    _configRoot = CreateDefaultConfig();
                    return;
                }

                string json = File.ReadAllText(_configPath);
                _configRoot = System.Text.Json.JsonDocument.Parse(json).RootElement;
                System.Diagnostics.Debug.WriteLine($"Config yüklendi: {_configPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Config yükleme hatası: {ex.Message}");
                System.Diagnostics.Debug.WriteLine("Default değerleri kullanılıyor...");
                _configRoot = CreateDefaultConfig();
            }
        }

        /// <summary>
        /// Database path'ini döndürür
        /// </summary>
        public string GetDatabasePath()
        {
            return GetString("Database:Path", @"C:\TurizmDB\turizm.db");
        }

        /// <summary>
        /// Backup directory'sini döndürür
        /// </summary>
        public string GetBackupDirectory()
        {
            string dbPath = GetDatabasePath();
            string defaultBackupDir = Path.Combine(Path.GetDirectoryName(dbPath), "backups");
            return GetString("Backup:Directory", defaultBackupDir);
        }

        /// <summary>
        /// Log directory'sini döndürür
        /// </summary>
        public string GetLogDirectory()
        {
            string dbPath = GetDatabasePath();
            string defaultLogDir = Path.Combine(Path.GetDirectoryName(dbPath), "logs");
            return GetString("Logging:Directory", defaultLogDir);
        }

        /// <summary>
        /// Maksimum backup sayısını döndürür
        /// </summary>
        public int GetMaxBackups()
        {
            return GetInt("Backup:MaxBackups", 10);
        }

        /// <summary>
        /// Migration lock timeout süresini (saniye) döndürür
        /// </summary>
        public int GetLockTimeoutSeconds()
        {
            return GetInt("Migration:LockTimeoutSeconds", 300);
        }

        /// <summary>
        /// Migration retry deneme sayısını döndürür
        /// </summary>
        public int GetMaxRetryAttempts()
        {
            return GetInt("Migration:MaxRetryAttempts", 3);
        }

        /// <summary>
        /// Retry delay (ms) döndürür
        /// </summary>
        public int GetRetryDelayMs()
        {
            return GetInt("Migration:RetryDelayMs", 1000);
        }

        /// <summary>
        /// Startup'ta health check yapılacak mı
        /// </summary>
        public bool IsHealthCheckEnabled()
        {
            return GetBool("HealthCheck:EnableOnStartup", true);
        }

        /// <summary>
        /// Health check için gereken minimum disk alanını döndürür (MB)
        /// </summary>
        public int GetMinimumDiskSpaceMB()
        {
            return GetInt("HealthCheck:MinimumDiskSpaceRequired_MB", 10);
        }

        /// <summary>
        /// Startup'ta migration dashboard gösterilecek mi
        /// </summary>
        public bool IsMigrationDashboardEnabled()
        {
            return GetBool("Dashboard:ShowMigrationDashboard", true);
        }

        /// <summary>
        /// Rollback feature aktif mi
        /// </summary>
        public bool IsRollbackEnabled()
        {
            return GetBool("Migration:AllowRollback", true);
        }

        /// <summary>
        /// Startup'ta backup alınacak mı
        /// </summary>
        public bool IsBackupOnStartupEnabled()
        {
            return GetBool("Database:BackupOnStartup", true);
        }

        /// <summary>
        /// Log directory lock timeout dakikasını döndürür
        /// </summary>
        public int GetMaxLogFileSizeMB()
        {
            return GetInt("Logging:MaxLogFileSizeMB", 5);
        }

        /// <summary>
        /// Maksimum log dosya sayısını döndürür
        /// </summary>
        public int GetMaxLogFiles()
        {
            return GetInt("Logging:MaxLogFiles", 10);
        }

        /// <summary>
        /// String değer oku
        /// </summary>
        private string GetString(string path, string defaultValue)
        {
            try
            {
                var parts = path.Split(':');
                JsonElement current = _configRoot;

                foreach (var part in parts)
                {
                    if (current.TryGetProperty(part, out var next))
                    {
                        current = next;
                    }
                    else
                    {
                        return defaultValue;
                    }
                }

                return current.GetString() ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Integer değer oku
        /// </summary>
        private int GetInt(string path, int defaultValue)
        {
            try
            {
                string strValue = GetString(path, defaultValue.ToString());
                return int.TryParse(strValue, out int result) ? result : defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Boolean değer oku
        /// </summary>
        private bool GetBool(string path, bool defaultValue)
        {
            try
            {
                string strValue = GetString(path, defaultValue.ToString());
                return bool.TryParse(strValue, out bool result) ? result : defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Default konfigürasyon oluştur (fallback)
        /// </summary>
        private JsonElement CreateDefaultConfig()
        {
            string json = @"{
  ""Database"": {
    ""Path"": ""C:\\TurizmDB\\turizm.db"",
    ""EnableBackup"": true,
    ""EnableLogging"": true,
    ""EnableLocking"": true,
    ""BackupOnStartup"": true
  },
  ""Backup"": {
    ""Directory"": ""C:\\TurizmDB\\backups"",
    ""MaxBackups"": 10
  },
  ""Migration"": {
    ""LockTimeoutSeconds"": 300,
    ""MaxRetryAttempts"": 3,
    ""RetryDelayMs"": 1000,
    ""AllowRollback"": true
  },
  ""Logging"": {
    ""Directory"": ""C:\\TurizmDB\\logs"",
    ""MaxLogFileSizeMB"": 5,
    ""MaxLogFiles"": 10
  },
  ""HealthCheck"": {
    ""EnableOnStartup"": true
  },
  ""Dashboard"": {
    ""ShowMigrationDashboard"": true
  }
}";
            return System.Text.Json.JsonDocument.Parse(json).RootElement;
        }

        /// <summary>
        /// Tüm konfigürasyon'u debug output'a yazır
        /// </summary>
        public void LogConfiguration()
        {
            System.Diagnostics.Debug.WriteLine("\n╔════════════════════════════════════════╗");
            System.Diagnostics.Debug.WriteLine("║     CONFIGURATION LOADED               ║");
            System.Diagnostics.Debug.WriteLine("╚════════════════════════════════════════╝");
            System.Diagnostics.Debug.WriteLine($"Database Path: {GetDatabasePath()}");
            System.Diagnostics.Debug.WriteLine($"Backup Directory: {GetBackupDirectory()}");
            System.Diagnostics.Debug.WriteLine($"Log Directory: {GetLogDirectory()}");
            System.Diagnostics.Debug.WriteLine($"Lock Timeout: {GetLockTimeoutSeconds()}s");
            System.Diagnostics.Debug.WriteLine($"Max Backups: {GetMaxBackups()}");
            System.Diagnostics.Debug.WriteLine($"Health Check Enabled: {IsHealthCheckEnabled()}");
            System.Diagnostics.Debug.WriteLine($"Dashboard Enabled: {IsMigrationDashboardEnabled()}");
            System.Diagnostics.Debug.WriteLine($"Rollback Enabled: {IsRollbackEnabled()}");
            System.Diagnostics.Debug.WriteLine("════════════════════════════════════════\n");
        }
    }
}

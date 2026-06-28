using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

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

                // Eğer config dosyasında hâlâ C:\TurizmDB kullanılıyorsa ve OneDrive bulunuyorsa,
                // dosyaları OneDrive altına taşıyacak migration'ı dene.

                try
                {
                    MigrateToOneDriveIfNeeded();
                }
                catch (Exception mex)
                {
                    System.Diagnostics.Debug.WriteLine($"OneDrive migration sırasında hata: {mex.Message}");
                }
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
        /// Varsayılan olarak OneDrive yüklüyse OneDrive\TurizmDB\turizm.db kullanılır; değilse C:\TurizmDB\turizm.db
        /// </summary>
        public string GetDatabasePath()
        {
            string defaultDb = Path.Combine(GetDefaultBaseDir(), "turizm.db");
            return GetString("Database:Path", defaultDb);
        }

        /// <summary>
        /// OneDrive veya fallback base dizinini döndürür
        /// </summary>
        private string GetDefaultBaseDir()
        {
            string oneDrive = Environment.GetEnvironmentVariable("OneDrive")
                              ?? Environment.GetEnvironmentVariable("OneDriveCommercial")
                              ?? Environment.GetEnvironmentVariable("OneDriveConsumer");

            if (!string.IsNullOrEmpty(oneDrive))
            {
                return Path.Combine(oneDrive, "TurizmDB");
            }

            return @"C:\\TurizmDB";
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
        /// Maksimum backup dizini toplam boyutunu döndürür (MB)
        /// </summary>
        public int GetMaxBackupSizeMB()
        {
            return GetInt("Backup:MaxBackupSizeMB", 500);
        }

        /// <summary>
        /// Compression (gzip) etkin mi
        /// </summary>
        public bool IsCompressionEnabled()
        {
            return GetBool("Backup:CompressionEnabled", false);
        }

        /// <summary>
        /// Yedek saklama süresi (gün). Bu değerden eski yedekler cleanup sırasında silinecek.
        /// </summary>
        public int GetBackupRetentionDays()
        {
            return GetInt("Backup:RetentionDays", 10);
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
        /// OneDrive yüklüyse varsayılan dizin OneDrive\TurizmDB olur, yoksa C:\TurizmDB kullanılır.
        /// </summary>
        private JsonElement CreateDefaultConfig()
        {
            // OneDrive environment değişkenlerini kontrol et
            string oneDrive = Environment.GetEnvironmentVariable("OneDrive")
                              ?? Environment.GetEnvironmentVariable("OneDriveCommercial")
                              ?? Environment.GetEnvironmentVariable("OneDriveConsumer");

            string baseDir = !string.IsNullOrEmpty(oneDrive)
                ? Path.Combine(oneDrive, "TurizmDB")
                : @"C:\\TurizmDB";

            var configObj = new
            {
                Database = new
                {
                    Path = Path.Combine(baseDir, "turizm.db"),
                    EnableBackup = true,
                    EnableLogging = true,
                    EnableLocking = true,
                    BackupOnStartup = true
                },
                Backup = new
                {
                    Directory = Path.Combine(baseDir, "backups"),
                    MaxBackups = 10,
                    MaxBackupSizeMB = 500,
                    CompressionEnabled = false,
                    RetentionDays = 10
                },
                Migration = new
                {
                    LockTimeoutSeconds = 300,
                    MaxRetryAttempts = 3,
                    RetryDelayMs = 1000,
                    AllowRollback = true
                },
                Logging = new
                {
                    Directory = Path.Combine(baseDir, "logs"),
                    MaxLogFileSizeMB = 5,
                    MaxLogFiles = 10
                },
                HealthCheck = new { EnableOnStartup = true },
                Dashboard = new { ShowMigrationDashboard = true }
            };

            string json = System.Text.Json.JsonSerializer.Serialize(configObj, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            return System.Text.Json.JsonDocument.Parse(json).RootElement;
        }

        /// <summary>
        /// Eğer kullanıcı daha önce C:\TurizmDB kullandıysa ve OneDrive mevcutsa,
        /// dosyaları OneDrive altına taşı ve appsettings.json içindeki yolları güncelle.
        /// Bu taşıma işleminde var olan dosyalar korunur; çakışma durumunda hedefe zaman damgası eklenir.
        /// </summary>
        private void MigrateToOneDriveIfNeeded()
        {
            try
            {
                string oneDrive = Environment.GetEnvironmentVariable("OneDrive")
                                  ?? Environment.GetEnvironmentVariable("OneDriveCommercial")
                                  ?? Environment.GetEnvironmentVariable("OneDriveConsumer");

                if (string.IsNullOrEmpty(oneDrive))
                    return; // OneDrive yüklü değilse çık

                string oneDriveBase = Path.Combine(oneDrive, "TurizmDB");

                string currentDb = GetDatabasePath();
                if (string.IsNullOrEmpty(currentDb))
                    return;

                string currentRoot = Path.GetDirectoryName(currentDb) ?? string.Empty;
                if (!currentRoot.StartsWith(@"C:\\TurizmDB", StringComparison.OrdinalIgnoreCase))
                    return; // Zaten farklı bir yerdeyse taşıma gerekmiyor

                // Oluşturulacak hedef yollar
                Directory.CreateDirectory(oneDriveBase);
                string targetDbPath = Path.Combine(oneDriveBase, Path.GetFileName(currentDb));
                string currentBackup = GetBackupDirectory();
                string targetBackup = Path.Combine(oneDriveBase, "backups");
                string currentLog = GetLogDirectory();
                string targetLog = Path.Combine(oneDriveBase, "logs");

                // Taşıma işlemleri
                if (File.Exists(currentDb))
                {
                    SafeMoveFile(currentDb, targetDbPath);
                }

                if (Directory.Exists(currentBackup))
                {
                    MoveDirectoryContents(currentBackup, targetBackup);
                }

                if (Directory.Exists(currentLog))
                {
                    MoveDirectoryContents(currentLog, targetLog);
                }

                // appsettings.json varsa içindeki yolları güncelle
                if (File.Exists(_configPath))
                {
                    try
                    {
                        string json = File.ReadAllText(_configPath);
                        var node = JsonNode.Parse(json) as JsonObject ?? new JsonObject();

                        // Database.Path
                        if (!node.ContainsKey("Database")) node["Database"] = new JsonObject();
                        ((JsonObject)node["Database"]) ["Path"] = targetDbPath;

                        // Backup.Directory
                        if (!node.ContainsKey("Backup")) node["Backup"] = new JsonObject();
                        ((JsonObject)node["Backup"])["Directory"] = targetBackup;

                        // Logging.Directory
                        if (!node.ContainsKey("Logging")) node["Logging"] = new JsonObject();
                        ((JsonObject)node["Logging"])["Directory"] = targetLog;

                        string updated = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(_configPath, updated);

                        // Reload config root from updated file
                        _configRoot = JsonDocument.Parse(updated).RootElement;
                        System.Diagnostics.Debug.WriteLine("Konfigürasyon yolları OneDrive'a taşındı ve appsettings.json güncellendi.");
                    }
                    catch (Exception ux)
                    {
                        System.Diagnostics.Debug.WriteLine($"Konfigürasyon güncellenemedi: {ux.Message}");
                    }
                }

            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MigrateToOneDriveIfNeeded hata: {ex.Message}");
            }
        }

        private void MoveDirectoryContents(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceDir, dir);
                Directory.CreateDirectory(Path.Combine(targetDir, relative));
            }

            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceDir, file);
                var dest = Path.Combine(targetDir, relative);
                SafeMoveFile(file, dest);
            }
        }

        private void SafeMoveFile(string sourceFile, string destFile)
        {
            try
            {
                var destDir = Path.GetDirectoryName(destFile);
                if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

                if (File.Exists(destFile))
                {
                    // Var olan dosyayı koru; yeni taşıma olan dosyaya zaman damgası ekle
                    string stamped = Path.GetFileNameWithoutExtension(destFile) + "_" + DateTime.Now.ToString("yyyyMMddHHmmss") + Path.GetExtension(destFile);
                    var stampedPath = Path.Combine(Path.GetDirectoryName(destFile) ?? string.Empty, stamped);
                    File.Move(sourceFile, stampedPath);
                }
                else
                {
                    File.Move(sourceFile, destFile);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SafeMoveFile hata: {ex.Message}");
            }
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

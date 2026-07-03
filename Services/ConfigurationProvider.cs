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
        /// Uygulamanın çalışma dizinini döndürür (LocalAppData altında).
        /// SQLite ana veritabanı, yedek ve loglar her zaman LocalAppData içinde tutulacaktır.
        /// OneDrive sadece yedek/senkronizasyon hedefine dönüşümlü olarak kullanılacaktır,
        /// ancak veritabanı asla OneDrive içinde çalıştırılmayacaktır.
        /// </summary>
        public string GetDefaultBaseDir()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            // Uygulamaya özel klasör
            return Path.Combine(localAppData, "TurizmOperasyonSistemi");
        }

        /// <summary>
        /// Eğer OneDrive kurulmuşsa OneDrive altındaki uygulama bazlı dizini döndürür, yoksa null döner.
        /// Bu dizin yalnızca senkronize edilecek yedeklerin/manifestlerin kopyalanması için kullanılabilir;
        /// veritabanı çalıştırılmaz.
        /// </summary>
        public string GetOneDriveBaseDir()
        {
            string oneDrive = Environment.GetEnvironmentVariable("OneDrive")
                              ?? Environment.GetEnvironmentVariable("OneDriveCommercial")
                              ?? Environment.GetEnvironmentVariable("OneDriveConsumer");

            if (string.IsNullOrEmpty(oneDrive))
                return null;

            return Path.Combine(oneDrive, "TurizmDB");
        }

        /// <summary>
        /// Returns a persistent client id for this installation (GUID stored in LocalAppData).
        /// Used to identify this machine's uploads in OneDrive to avoid name collisions.
        /// </summary>
        public string GetClientId()
        {
            try
            {
                var baseDir = GetDefaultBaseDir();
                Directory.CreateDirectory(baseDir);
                var idPath = Path.Combine(baseDir, "client.id");
                if (File.Exists(idPath))
                {
                    var content = File.ReadAllText(idPath).Trim();
                    if (Guid.TryParse(content, out var _)) return content;
                }

                var guid = Guid.NewGuid().ToString();
                File.WriteAllText(idPath, guid);
                return guid;
            }
            catch
            {
                return Guid.NewGuid().ToString();
            }
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
        /// Migration işlemi için maksimum süre (saniye). Eğer migration bu süreyi aşarsa işlem "timeout" kabul edilir.
        /// </summary>
        public int GetMigrationTimeoutSeconds()
        {
            return GetInt("Migration:TimeoutSeconds", 600);
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

            // Default çalışma dizini artık LocalAppData altında olmalıdır (uygulamaya özel).
            string baseDir = GetDefaultBaseDir();

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
                Update = new
                {
                    Enabled = true,
                    ManifestUrl = "",
                    CheckIntervalSeconds = 86400
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
        /// Update mekanizmasının aktif olup olmadığını döndürür
        /// </summary>
        public bool IsUpdateEnabled()
        {
            return GetBool("Update:Enabled", true);
        }

        /// <summary>
        /// Update manifest (latest.json) URL'si
        /// </summary>
        public string GetUpdateManifestUrl()
        {
            return GetString("Update:ManifestUrl", string.Empty);
        }

        /// <summary>
        /// Update kontrol sıklığı (saniye)
        /// </summary>
        public int GetUpdateCheckIntervalSeconds()
        {
            return GetInt("Update:CheckIntervalSeconds", 86400);
        }

        // Additional update-related configuration
        public string GetUpdateChannel()
        {
            return GetString("Update:Channel", "stable");
        }

        public bool IsAutoDownloadEnabled()
        {
            return GetBool("Update:AutoDownload", true);
        }

        public bool IsAutoInstallEnabled()
        {
            return GetBool("Update:AutoInstall", false);
        }

        public int GetUpdateRetryCount()
        {
            return GetInt("Update:RetryCount", 3);
        }

        public int GetUpdateCheckIntervalHours()
        {
            return GetInt("Update:CheckIntervalHours", 24);
        }

        /// <summary>
        /// <summary>
        /// Mevcut eski konumlardan (ör. C:\TurizmDB veya OneDrive) LocalAppData altındaki
        /// uygulama dizinine veri taşıma / kopyalama işlemi yapar.
        /// Önemli: Veritabanı asla OneDrive içinde çalıştırılmayacak. Eğer eski konumda DB varsa,
        /// bu metot onu kopyalar ve appsettings.json içindeki yolları LocalAppData yollarına günceller.
        /// Kaynak konumdaki dosyalar korunur (kopyalanır, taşınmaz).
        /// </summary>
        private void MigrateToOneDriveIfNeeded()
        {
            try
            {
                string oneDriveBase = GetOneDriveBaseDir();
                string localBase = GetDefaultBaseDir();

                string currentDb = GetDatabasePath();
                if (string.IsNullOrEmpty(currentDb))
                    return;

                string currentRoot = Path.GetDirectoryName(currentDb) ?? string.Empty;

                // Eğer zaten LocalAppData içindeyse hiçbir işlem yok
                if (currentRoot.StartsWith(localBase, StringComparison.OrdinalIgnoreCase))
                    return;

                // Potansiyel eski kaynaklar: OneDrive veya C:\TurizmDB
                bool fromOneDrive = !string.IsNullOrEmpty(oneDriveBase) && currentRoot.StartsWith(oneDriveBase, StringComparison.OrdinalIgnoreCase);
                bool fromLegacyC = currentRoot.StartsWith(@"C:\\TurizmDB", StringComparison.OrdinalIgnoreCase);

                if (!fromOneDrive && !fromLegacyC)
                    return; // Bilinmeyen bir konumdan geliyorsa müdahale etmiyoruz

                // Hedef lokasyonları oluştur
                Directory.CreateDirectory(localBase);
                string targetDbPath = Path.Combine(localBase, Path.GetFileName(currentDb));
                string currentBackup = GetBackupDirectory();
                string targetBackup = Path.Combine(localBase, "backups");
                string currentLog = GetLogDirectory();
                string targetLog = Path.Combine(localBase, "logs");

                // Veritabanını güvenli şekilde kopyala (varsa karşılaştırma yap)
                if (File.Exists(currentDb))
                {
                    try
                    {
                        // Eğer kaynak dosya uygulama kurulumu dizinindeyse (installer tarafından yerleştirilmiş)
                        // mevcut local DB'yi KESİNLİKLE overwrite etmeyelim. Bunun yerine kaynak dosyayı arşivleyelim.
                        var appBaseDir = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
                        bool sourceIsAppInstall = false;
                        try
                        {
                            sourceIsAppInstall = currentDb.StartsWith(appBaseDir, StringComparison.OrdinalIgnoreCase)
                                               || Path.GetDirectoryName(currentDb)?.StartsWith(appBaseDir, StringComparison.OrdinalIgnoreCase) == true;
                        }
                        catch { sourceIsAppInstall = false; }

                        if (!File.Exists(targetDbPath))
                        {
                            // Hedef yoksa güvenli şekilde kopyala
                            File.Copy(currentDb, targetDbPath);
                            System.Diagnostics.Debug.WriteLine($"Database kopyalandı: {currentDb} -> {targetDbPath}");
                        }
                        else
                        {
                            if (sourceIsAppInstall)
                            {
                                // Installer'ın getirdiği DB, zaten var olan local DB'yi değiştirmesin.
                                try
                                {
                                    var srcDir = Path.GetDirectoryName(currentDb) ?? appBaseDir;
                                    string stamped = Path.GetFileNameWithoutExtension(currentDb) + "_installer_" + DateTime.Now.ToString("yyyyMMddHHmmss") + Path.GetExtension(currentDb);
                                    var archived = Path.Combine(srcDir, stamped);
                                    File.Move(currentDb, archived);
                                    System.Diagnostics.Debug.WriteLine($"Installer kaynaklı DB bulundu; mevcut local DB korunacak. Installer DB arşivlendi: {archived}");
                                }
                                catch (Exception mvEx)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Installer DB arşivleme başarısız: {mvEx.Message}");
                                }
                            }
                            else
                            {
                                var srcInfo = new FileInfo(currentDb);
                                var dstInfo = new FileInfo(targetDbPath);
                                if (srcInfo.LastWriteTimeUtc > dstInfo.LastWriteTimeUtc)
                                {
                                    // Önce mevcut local dosyayı arşivle
                                    string stamped = Path.GetFileNameWithoutExtension(targetDbPath) + "_" + DateTime.Now.ToString("yyyyMMddHHmmss") + Path.GetExtension(targetDbPath);
                                    var archived = Path.Combine(Path.GetDirectoryName(targetDbPath) ?? string.Empty, stamped);
                                    File.Move(targetDbPath, archived);
                                    File.Copy(currentDb, targetDbPath);
                                    System.Diagnostics.Debug.WriteLine($"Local DB güncellendi: kaynak daha yeni, arşivlendi -> {archived}");
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine("Local DB zaten daha yeni veya aynı; kopyalama atlandı.");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Database kopyalama hatası: {ex.Message}");
                    }
                }

                // Yedek ve log dizinlerini kopyala (kaynak korunur)
                try
                {
                    if (Directory.Exists(currentBackup))
                    {
                        CopyDirectoryContents(currentBackup, targetBackup);
                    }

                    if (Directory.Exists(currentLog))
                    {
                        CopyDirectoryContents(currentLog, targetLog);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Dizin kopyalama hatası: {ex.Message}");
                }

                // appsettings.json varsa içindeki yolları LocalAppData içindeki hedeflere güncelle
                if (File.Exists(_configPath))
                {
                    try
                    {
                        string json = File.ReadAllText(_configPath);
                        var node = JsonNode.Parse(json) as JsonObject ?? new JsonObject();

                        // Database.Path
                        if (!node.ContainsKey("Database")) node["Database"] = new JsonObject();
                        ((JsonObject)node["Database"])["Path"] = targetDbPath;

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
                        System.Diagnostics.Debug.WriteLine("Konfigürasyon yolları LocalAppData'ya kopyalandı ve appsettings.json güncellendi.");
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

        private void CopyDirectoryContents(string sourceDir, string targetDir)
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
                if (!File.Exists(dest))
                {
                    try
                    {
                        File.Copy(file, dest);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Dosya kopyalanamadı: {file} -> {dest} : {ex.Message}");
                    }
                }
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

using System.Configuration;
using System.Data;
using System.Windows;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Services;
using TurizmOperasyonSistemi.Windows;
using System.Text.Json;
using System.Linq;
using TurizmOperasyonSistemi.Models;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace TurizmOperasyonSistemi
{
    /// <summary>
    /// Interaction logic for App.xaml - Enterprise-grade database migration startup
    /// </summary>
    public partial class App : Application
    {
        private AppDbContext _context;
        // SyncService removed: OneDrive <-> LocalAppData automatic sync disabled
        private TurizmOperasyonSistemi.Services.BackgroundServiceManager _backgroundServiceManager;
        private bool _startupMigrationsSucceeded = true;

        public App()
        {
            InitializeComponent();
            // Register global exception handlers to capture runtime errors and avoid silent exits
            try
            {
                RegisterGlobalExceptionHandlers();
            }
            catch { }
        }

        private async Task SyncLocalFromOneDriveIfRemoteIsNewer(ConfigurationProvider config, string localDbPath, string localBase)
        {
            try
            {
                var oneDriveBase = config.GetOneDriveBaseDir();
                if (string.IsNullOrEmpty(oneDriveBase))
                {
                    System.Diagnostics.Debug.WriteLine("Sync: OneDrive not configured or not found.");
                    return;
                }

                var remoteManifestPath = Path.Combine(oneDriveBase, "Manifest.json");
                var localManifestPath = Path.Combine(localBase, "Manifest.json");

                if (!File.Exists(remoteManifestPath))
                {
                    System.Diagnostics.Debug.WriteLine("Sync: remote manifest not found.");
                    return;
                }

                long remoteVersion = 0;
                string remoteHash = null;
                try
                {
                    var txt = File.ReadAllText(remoteManifestPath);
                    using var doc = JsonDocument.Parse(txt);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("Version", out var v) && v.TryGetInt64(out var vv)) remoteVersion = vv;
                    if (root.TryGetProperty("Hash", out var h)) remoteHash = h.GetString();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Sync: failed to read remote manifest: {ex.Message}");
                    return;
                }

                long localVersion = 0;
                string localHash = null;
                if (File.Exists(localManifestPath))
                {
                    try
                    {
                        var txt = File.ReadAllText(localManifestPath);
                        using var doc = JsonDocument.Parse(txt);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("Version", out var v) && v.TryGetInt64(out var lv)) localVersion = lv;
                        if (root.TryGetProperty("Hash", out var h)) localHash = h.GetString();
                    }
                    catch { }
                }

                // Decide if remote is newer: remoteVersion > localVersion OR (remoteVersion == localVersion && remoteHash != localHash)
                bool remoteIsNewer = false;
                if (remoteVersion > localVersion) remoteIsNewer = true;
                else if (remoteVersion == localVersion && !string.Equals(remoteHash ?? string.Empty, localHash ?? string.Empty, StringComparison.OrdinalIgnoreCase)) remoteIsNewer = true;

                if (!remoteIsNewer)
                {
                    System.Diagnostics.Debug.WriteLine($"Sync: remote not newer (remote v{remoteVersion}, local v{localVersion}). Skipping.");
                    return;
                }

                // Remote is newer -> copy remote DB over local DB atomically
                var remoteDbPath = Path.Combine(oneDriveBase, Path.GetFileName(localDbPath));
                if (!File.Exists(remoteDbPath))
                {
                    System.Diagnostics.Debug.WriteLine("Sync: remote DB file not found.");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"Sync: remote is newer (v{remoteVersion}). Replacing local DB with remote DB.");

                // Ensure any local DB connections closed: best-effort (at startup there should be none)
                // Copy remote DB to a temp file in local folder, then replace
                Directory.CreateDirectory(Path.GetDirectoryName(localDbPath) ?? localBase);
                var temp = Path.Combine(Path.GetDirectoryName(localDbPath) ?? localBase, Path.GetFileName(localDbPath) + ".tmp." + Guid.NewGuid().ToString("N"));
                try
                {
                    File.Copy(remoteDbPath, temp, true);
                    // Retry replace to tolerate locks
                    int attempts = 0;
                    bool replaced = false;
                    while (!replaced && attempts < 5)
                    {
                        try
                        {
                            if (File.Exists(localDbPath))
                            {
                                var backupOld = localDbPath + ".bak." + Guid.NewGuid().ToString("N");
                                File.Replace(temp, localDbPath, backupOld);
                                try { File.Delete(backupOld); } catch { }
                            }
                            else
                            {
                                File.Move(temp, localDbPath);
                            }
                            replaced = true;
                        }
                        catch (IOException)
                        {
                            attempts++;
                            System.Threading.Thread.Sleep(200 * attempts);
                        }
                    }
                    if (!replaced)
                    {
                        // Last resort: try copy with overwrite
                        File.Copy(remoteDbPath, localDbPath, true);
                    }

                    // Update local manifest to match remote manifest
                    try
                    {
                        File.Copy(remoteManifestPath, localManifestPath, true);
                        System.Diagnostics.Debug.WriteLine("Sync: local manifest updated from OneDrive.");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Sync: failed to update local manifest: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Sync: failed to replace local DB: {ex.Message}");
                    try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SyncLocalFromOneDriveIfRemoteIsNewer failed: {ex.Message}");
            }
        }

        private async Task UploadDbAndManifestToOneDriveAsync(string currentHash, string localManifestPath, string localDbPath, long version, string oneDriveBase)
        {
            try
            {
                var destDir = oneDriveBase; // ConfigurationProvider already includes subfolder name
                Directory.CreateDirectory(destDir);

                // Show a small progress window to inform the user and keep it visible until upload completes
                Window progressWnd = null;
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    progressWnd = new Window
                    {
                        Title = "Senkronizasyon",
                        Content = new TextBlock { Text = "SQLite bağlantısının kesilmesini ve dosyalarınızın bulut ortamına yüklenmesini bekleyin...", Margin = new Thickness(20), TextWrapping = TextWrapping.Wrap, Width = 400 },
                        SizeToContent = SizeToContent.WidthAndHeight,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen,
                        ResizeMode = ResizeMode.NoResize,
                        Topmost = true
                    };
                    progressWnd.Show();
                });

                // Perform upload on a background thread but ensure file operations use synchronous IO
                await Task.Run(() =>
                {
                    try
                    {
                        // Create a consistent snapshot using BackupManager
                        var bm = new BackupManager(localDbPath);
                        var res = bm.CreateBackup();
                        if (!res.IsSuccess)
                        {
                            System.Diagnostics.Debug.WriteLine($"Upload: backup creation failed: {res.Message}");
                        }

                        string sourceDbForUpload = res.IsSuccess && !string.IsNullOrEmpty(res.BackupPath) ? res.BackupPath : localDbPath;

                        // Determine target paths
                        var targetDbPath = Path.Combine(destDir, Path.GetFileName(localDbPath)); // e.g., turizm.db
                        var targetManifestPath = Path.Combine(destDir, Path.GetFileName(localManifestPath));

                        // Copy DB to dest atomically
                        try
                        {
                            var tempDb = Path.Combine(destDir, Path.GetFileName(localDbPath) + ".tmp." + Guid.NewGuid().ToString("N"));
                            File.Copy(sourceDbForUpload, tempDb, true);
                            if (File.Exists(targetDbPath))
                            {
                                var backupOld = targetDbPath + ".bak";
                                File.Replace(tempDb, targetDbPath, backupOld);
                                try { File.Delete(backupOld); } catch { }
                            }
                            else
                            {
                                File.Move(tempDb, targetDbPath);
                            }
                            System.Diagnostics.Debug.WriteLine($"Upload: DB copied to OneDrive: {targetDbPath}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Upload: DB copy failed: {ex.Message}");
                        }

                        // Copy manifest atomically
                        try
                        {
                            var tempMan = Path.Combine(destDir, Path.GetFileName(localManifestPath) + ".tmp." + Guid.NewGuid().ToString("N"));
                            File.Copy(localManifestPath, tempMan, true);
                            if (File.Exists(targetManifestPath))
                            {
                                var backupOld = targetManifestPath + ".bak";
                                File.Replace(tempMan, targetManifestPath, backupOld);
                                try { File.Delete(backupOld); } catch { }
                            }
                            else
                            {
                                File.Move(tempMan, targetManifestPath);
                            }
                            System.Diagnostics.Debug.WriteLine($"Upload: Manifest copied to OneDrive: {targetManifestPath}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Upload: Manifest copy failed: {ex.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Upload: unexpected error: {ex.Message}");
                    }
                }).ConfigureAwait(false);

                // Close progress window
                Application.Current?.Dispatcher.Invoke(() => { try { progressWnd?.Close(); } catch { } });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UploadDbAndManifestToOneDriveAsync failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Eğer database dosyası yoksa oluşturur ve basit bir Manifest.json dosyası yazar.
        /// Manifest formatı (kullanıcının istediği):
        /// {
        ///   "MachineId": "A-PC",
        ///   "Version": 1,
        ///   "LastModified": "2026-07-05T18:22:00",
        ///   "Hash": "..."
        /// }
        /// </summary>
        private void EnsureLocalDatabaseAndManifest(string dbPath, string localBase)
        {
            try
            {
                var dbDir = Path.GetDirectoryName(dbPath) ?? localBase;
                Directory.CreateDirectory(dbDir);

                bool created = false;
                if (!File.Exists(dbPath) || new FileInfo(dbPath).Length == 0)
                {
                    try
                    {
                        var recovery = new BackupManager(dbPath).RestoreFromLastBackup();
                        if (recovery.IsSuccess)
                        {
                            System.Diagnostics.Debug.WriteLine($"Recovered database from backup before creating a new database: {recovery.RestoredFromBackup}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Database backup recovery skipped: {ex.Message}");
                    }
                }

                if (!File.Exists(dbPath))
                {
                    // Create an empty SQLite database by opening a connection
                    try
                    {
                        var cs = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
                        using var conn = new SqliteConnection(cs);
                        conn.Open();
                        conn.Close();
                        created = true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to create sqlite file: {ex.Message}");
                        // Try to ensure a zero-length file as fallback
                        try { File.WriteAllBytes(dbPath, Array.Empty<byte>()); created = true; } catch { }
                    }
                }

                // Compute hash (SHA256) of the DB file if exists
                string hash = string.Empty;
                try
                {
                    if (File.Exists(dbPath))
                    {
                        using var sha = SHA256.Create();
                        using var fs = File.OpenRead(dbPath);
                        var h = sha.ComputeHash(fs);
                        hash = BitConverter.ToString(h).Replace("-", "").ToLowerInvariant();
                    }
                }
                catch { }

                // Write Manifest.json in local base directory
                try
                {
                    // Write manifest using current DbManifest shape to avoid format mismatch
                    Directory.CreateDirectory(localBase);
                    var manifestPath = Path.Combine(localBase, "manifest.json");
                    var manifest = new TurizmOperasyonSistemi.Models.DbManifest
                    {
                        // Start manifest version at 1 for fresh installations to avoid 0/undefined state
                        DbVersion = 1,
                        AppVersion = TurizmOperasyonSistemi.Services.ManifestService.GetAppVersionSafe(),
                        CreatedAt = DateTime.UtcNow,
                        LastModified = DateTime.UtcNow,
                        MachineId = Environment.MachineName,
                        Checksum = hash,
                        DbSize = File.Exists(dbPath) ? new FileInfo(dbPath).Length : 0
                    };
                    var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(manifestPath, json);
                    if (created)
                    {
                        System.Diagnostics.Debug.WriteLine($"Created local DB and manifest: {dbPath}, {manifestPath}");
                        // Also ensure DatabaseVersions table exists and seed initial 1.0.0.0 entry so DatabaseVersion is not empty
                        try
                        {
                            var cs = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
                            using var conn = new SqliteConnection(cs);
                            conn.Open();
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS DatabaseVersions (
                                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                    Version TEXT,
                                    CreatedAt TEXT,
                                    Description TEXT
                                );";
                                cmd.ExecuteNonQuery();

                                cmd.CommandText = "SELECT COUNT(1) FROM DatabaseVersions;";
                                var count = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
                                if (count == 0)
                                {
                                    cmd.CommandText = "INSERT INTO DatabaseVersions (Version, CreatedAt, Description) VALUES (@v,@c,@d);";
                                    cmd.Parameters.AddWithValue("@v", "1.0.0.0");
                                    cmd.Parameters.AddWithValue("@c", DateTime.UtcNow.ToString("o"));
                                    cmd.Parameters.AddWithValue("@d", "Initial database version on first run");
                                    cmd.ExecuteNonQuery();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to seed initial DatabaseVersions: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to write Manifest.json: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EnsureLocalDatabaseAndManifest top-level error: {ex.Message}");
            }
            }

        private void RegisterGlobalExceptionHandlers()
        {
            // UI thread exceptions
            this.DispatcherUnhandledException += (s, ev) =>
            {
                try
                {
                    LogUnhandledException(ev.Exception, "DispatcherUnhandledException");
                }
                catch { }
                // mark handled so app does not crash immediately; user can decide to continue
                ev.Handled = true;
            };

            // Non-UI thread exceptions
            AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
            {
                try
                {
                    var ex = ev.ExceptionObject as Exception;
                    LogUnhandledException(ex, "AppDomain.UnhandledException");
                }
                catch { }
            };

            // Unobserved task exceptions
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, ev) =>
            {
                try
                {
                    LogUnhandledException(ev.Exception, "TaskScheduler.UnobservedTaskException");
                }
                catch { }
                ev.SetObserved();
            };
        }

        private void LogUnhandledException(Exception ex, string source)
        {
            try
            {
                string logDir;
                try { logDir = Services.ConfigurationProvider.Instance.GetLogDirectory(); } catch { logDir = Path.GetTempPath(); }
                Directory.CreateDirectory(logDir);
                var file = Path.Combine(logDir, $"unhandled_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")}.log");
                using (var sw = new StreamWriter(file, false))
                {
                    sw.WriteLine($"Source: {source}");
                    sw.WriteLine($"TimeUtc: {DateTime.UtcNow:O}");
                    if (ex != null)
                    {
                        sw.WriteLine(ex.ToString());
                    }
                    else
                    {
                        sw.WriteLine("Exception object was null or not an Exception");
                    }
                }

                // Show a concise message to the user on UI thread
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        MessageBox.Show($"Beklenmeyen bir hata oluştu ({source}). Detaylar log dosyasına kaydedildi:\n{file}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    catch { }
                });
            }
            catch { }
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Enterprise startup: initialize configuration, run health checks, and apply migrations using configured paths
            try
            {
                var config = Services.ConfigurationProvider.Instance;
                config.LogConfiguration();

                // Velopack is the sole application update path. It is intentionally checked
                // before database startup so a successful update can restart the old process
                // without opening the main window. Network failures are non-blocking.
                try
                {
                    var updateApplied = await new VelopackUpdateService().CheckAndApplyAsync().ConfigureAwait(true);
                    if (updateApplied)
                    {
                        Shutdown();
                        return;
                    }
                }
                catch (Exception updateEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Velopack startup check failed: {updateEx.Message}");
                }

                // Database synchronization is disabled; the local database remains authoritative.

                string dbPath = config.GetDatabasePath();
                // Ensure database file and simple Manifest.json exist on first run
                try
                {
                    EnsureLocalDatabaseAndManifest(dbPath, config.GetDefaultBaseDir());
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"EnsureLocalDatabaseAndManifest error: {ex.Message}");
                }

                System.Diagnostics.Debug.WriteLine("\n╔══════════════════════════════════════════════════════╗");
                System.Diagnostics.Debug.WriteLine("║         APPLICATION STARTUP (ENTERPRISE)              ║");
                System.Diagnostics.Debug.WriteLine("╚══════════════════════════════════════════════════════╝");
                System.Diagnostics.Debug.WriteLine($"Tarih/Saat: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                System.Diagnostics.Debug.WriteLine($"Uygulama Versiyonu: {MigrationService.GetApplicationVersion()}");
                System.Diagnostics.Debug.WriteLine($"Veritabanı Yolu: {dbPath}");

                using (var context = new AppDbContext())
                {
                    // ENTERPRISE-GRADE MigrationService - use configured db path
                    var migrationService = new MigrationService(context, dbPath);

                    // 1. Veritabanı versiyonunu oku
                    string currentDbVersion = migrationService.GetDatabaseVersion();
                    System.Diagnostics.Debug.WriteLine($"\nMevcut Database Versiyonu: {currentDbVersion ?? "Hiç"}");

                    // Optionally run health checks before applying migrations
                    if (config.IsHealthCheckEnabled())
                    {
                        try
                        {
                            var healthService = new HealthCheckService(context);
                            var report = healthService.RunCompleteHealthCheck();
                            System.Diagnostics.Debug.WriteLine($"HealthCheck: {report}");
                            if (!report.IsHealthy)
                            {
                                System.Diagnostics.Debug.WriteLine("⚠ Health checks reported issues. Proceeding with caution.");
                            }
                        }
                        catch (Exception hcEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"⚠ HealthCheck failed: {hcEx.Message}");
                        }
                    }

                    // 2. Migration'ları uygula (eksik olanlar otomatikman çalışacak)
                    bool migrationsSucceeded = migrationService.ApplyMigrations();
                    _startupMigrationsSucceeded = migrationsSucceeded;

                    if (!migrationsSucceeded)
                    {
                        System.Diagnostics.Debug.WriteLine("\n⚠ UYARI: Migration işlemi sırasında hata oluştu.");
                        System.Diagnostics.Debug.WriteLine("⚠ Bazı özellikler doğru çalışmayabilir.");
                        System.Diagnostics.Debug.WriteLine($"⚠ Lütfen log dosyalarını inceleyiniz: {config.GetLogDirectory()}");
                        MessageBox.Show(
                            "Veritabanı güncellemesi tamamlanamadı. Veri güvenliği için uygulama açılmadı. Veritabanı ve yedek dosyalarını silmeyin.",
                            "Veritabanı güncelleme hatası",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        Shutdown(1);
                        return;
                    }

                    // 3. Tüm custom tablolar için ensure metodlarını çalıştır
                    System.Diagnostics.Debug.WriteLine("\n► Custom tablolar kontrol ediliyorum...");
                    migrationService.EnsureAllCustomTablesExists();
                    System.Diagnostics.Debug.WriteLine("✓ Custom tablolar hazır");

                    // 4. Diğer ön başlatma işlemleri
                    System.Diagnostics.Debug.WriteLine("\n► Ek şema ayarlamaları yapılıyor...");
                    context.EnsureNationalityColumnExists();
                    // Yeni sütun için ensure - Aciklama
                    context.EnsureAciklamaColumnExists();
                    context.EnsureRegistrationPassportArrivedColumnExists();
                    context.EnsureRegistrationStatusChoiceColumnsExist();
                    context.EnsureSyncMetadataColumnsExist();
                    context.NormalizeRegistrationNullValues();
                    // Seed default AppSettings on first run for many lookup types
                    try
                    {
                        var seeds = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();

                        var loginSetting = context.AppSettings.FirstOrDefault(x => x.Type == "EnableUserLogin");
                        if (loginSetting == null)
                        {
                            context.AppSettings.Add(new AppSetting { Type = "EnableUserLogin", Value = "false", SortOrder = 0, IsActive = true });
                            context.SaveChanges();
                        }

                        seeds["Cinsiyet"] = new System.Collections.Generic.List<string> { "Erkek", "Kadın" };
                        seeds["Yaş Aralığı"] = new System.Collections.Generic.List<string> { "Bebek", "Çocuk", "Yetişkin" };
                        seeds["Kan Grubu"] = new System.Collections.Generic.List<string> { "A+", "A-", "B+", "B-", "AB+", "AB-", "0+", "0-" };
                        seeds["Pasaport Türü"] = new System.Collections.Generic.List<string> { "Bordo", "Yeşil", "Gri", "Siyah" };

                        // Dönem: aylık seçenekler Temmuz 2026'dan Aralık 2126'ya kadar
                        var turkishMonths = new[] { "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };
                        var donemList = new System.Collections.Generic.List<string>();
                        var startYear = 2026; var startMonth = 7; // Temmuz 2026
                        var endYear = 2126; var endMonth = 12; // Aralık 2126
                        int year = startYear; int month = startMonth;
                        while (year < endYear || (year == endYear && month <= endMonth))
                        {
                            var name = $"{turkishMonths[month - 1]} {year}";
                            donemList.Add(name);
                            month++;
                            if (month > 12) { month = 1; year++; }
                        }
                        seeds["Dönem"] = donemList;

                        seeds["Vize Talebi"] = new System.Collections.Generic.List<string> { "Vize Alacağız", "Vizeyi Kendisi Alacak" };
                        seeds["Kargo Talebi"] = new System.Collections.Generic.List<string> { "Kargo Gönderilecek", "Kargo Almayacak", "Kargo Gönderildi", "Elden Teslim Alacak", "Elden Teslim Aldı" };
                        seeds["Bilet Talebi"] = new System.Collections.Generic.List<string> { "Biletini Alacağız", "Bileti Kendisi Alacak" };
                        seeds["Aktarma Var mı?"] = new System.Collections.Generic.List<string> { "Var", "Yok" };
                        seeds["Pasaportu Geldi mi ?"] = new System.Collections.Generic.List<string> { "Evet", "Hayır", "Pasaport Vermeyecek" };
                        seeds["Bilet Durumu"] = new System.Collections.Generic.List<string> { "Bileti geldi", "Bileti gelmedi", "Bileti Alınıyor", "Bileti Kendisi Alacak" };
                        seeds["Vize Durumu"] = new System.Collections.Generic.List<string> { "Vize Çıktı", "Vize Çıkmadı", "Vizesi Alınıyor", "Vizeyi Kendisi Alacak", "Pasaport Bekleniyor" };
                        seeds["Uyruk"] = new System.Collections.Generic.List<string> { "TÜRKİYE", "YABANCI" };
                        seeds["Ödeme Yöntemi"] = new System.Collections.Generic.List<string> { "Havale/EFT", "Nakit/Elden Teslim", "Kredi Kartı /Banka Kartı", "Çek", "Senet", "Mail Order" };
                        seeds["Para Birimi"] = new System.Collections.Generic.List<string> { "Dolar $", "Euro €", "Pound £ (İngiliz Sterlini)", "Türk Lirası ₺", "Suudi Riyali (SAR)" };
                        seeds["RoomType"] = new System.Collections.Generic.List<string> { "SINGLE", "DBL", "TWIN", "TRPL", "QUAD", "QUINT" };

                        // Türkiye'nin 81 ili
                        var provinces = new System.Collections.Generic.List<string>
                        {
                            "Adana","Adıyaman","Afyonkarahisar","Ağrı","Amasya","Ankara","Antalya","Artvin","Aydın","Balıkesir","Bilecik","Bingöl","Bitlis","Bolu","Burdur","Bursa","Çanakkale","Çankırı","Çorum","Denizli","Diyarbakır","Edirne","Elazığ","Erzincan","Erzurum","Eskişehir","Gaziantep","Giresun","Gümüşhane","Hakkari","Hatay","Isparta","Mersin","İstanbul","İzmir","Kars","Kastamonu","Kayseri","Kırklareli","Kırşehir","Kocaeli","Konya","Kütahya","Malatya","Manisa","Kahramanmaraş","Mardin","Muğla","Muş","Nevşehir","Niğde","Ordu","Rize","Sakarya","Samsun","Siirt","Sinop","Sivas","Tekirdağ","Tokat","Trabzon","Tunceli","Şanlıurfa","Uşak","Van","Yozgat","Zonguldak","Aksaray","Bayburt","Karaman","Kırıkkale","Batman","Şırnak","Bartın","Ardahan","Iğdır","Yalova","Karabük","Kilis","Osmaniye","Düzce"
                        };

                        seeds["Aktarma Şehri"] = provinces;
                        seeds["Katılım Yapılacak Şehir"] = provinces;

                        // Apply seeds: for each Type, if no existing entries with that Type, add values with incremental SortOrder
                        foreach (var kv in seeds)
                        {
                            var type = kv.Key;
                            var values = kv.Value;
                            bool exists = context.AppSettings.Any(x => x.Type == type);
                            if (exists) continue;

                            int sort = 1;
                            foreach (var val in values)
                            {
                                context.AppSettings.Add(new AppSetting { Type = type, Value = val, SortOrder = sort, IsActive = true });
                                sort++;
                            }
                        }

                        // Save all added settings
                        context.SaveChanges();
                        System.Diagnostics.Debug.WriteLine("Seeded default AppSettings for multiple lookup types.");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"AppSettings multi-seed failed: {ex.Message}");
                    }
                    System.Diagnostics.Debug.WriteLine("✓ Şema ayarlamaları tamamlandı");
                }

                // 5. Seed missing PermissionsJson values based on role defaults for existing users
                System.Diagnostics.Debug.WriteLine("\n► Kullanıcı izinleri kontrol ediliyor...");
                try
                {
                    UserPermissionSeeder.EnsureUserPermissionsSeeded();
                    System.Diagnostics.Debug.WriteLine("✓ Kullanıcı izinleri tamamlandı");
                }
                catch (Exception permEx)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠ Uyarı: UserPermissionSeeder başarısız: {permEx.Message}");
                }

                System.Diagnostics.Debug.WriteLine("\n╔══════════════════════════════════════════════════════╗");
                System.Diagnostics.Debug.WriteLine("║    ✓ UYGULAMA BAŞARILI BAŞLATILDI (ENTERPRISE)        ║");
                System.Diagnostics.Debug.WriteLine("╚══════════════════════════════════════════════════════╝\n");

                // Start background services manager (Sync, Update, Notification, ...)
                try
                {
                    _backgroundServiceManager = new BackgroundServiceManager();

                    // Sync background service removed (OneDrive sync disabled)

                    await _backgroundServiceManager.StartAllAsync();
                    // Velopack performs the only application update check during startup.
                    // The former manifest/zip/INNO Setup notification path is intentionally not registered.
                }
                catch (Exception bgEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to start background services: {bgEx.Message}");
                }

                CurrentUser.Instance.SetUser(new User
                {
                    Id = -1,
                    Username = "System",
                    PasswordHash = string.Empty,
                    Role = "admin",
                    PermissionsJson = "[\"all\"]",
                    CreatedAt = DateTime.Now
                });

                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("\n╔══════════════════════════════════════════════════════╗");
                System.Diagnostics.Debug.WriteLine("║    ✗ UYGULAMA BAŞLATMA HATASI (KRITIK)               ║");
                System.Diagnostics.Debug.WriteLine("╚══════════════════════════════════════════════════════╝");
                System.Diagnostics.Debug.WriteLine($"Zaman: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                System.Diagnostics.Debug.WriteLine($"Hata Mesajı: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Exception Type: {ex.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"İç Hata: {ex.InnerException.Message}");
                }

                System.Diagnostics.Debug.WriteLine($"\nLog dosyaları burası: {Services.ConfigurationProvider.Instance.GetLogDirectory()}");
                System.Diagnostics.Debug.WriteLine("===================================================\n");

                // User'a detaylı hata mesajı göster
                string errorMessage = FormatErrorMessage(ex);

                MessageBox.Show(
                    errorMessage,
                    "Uygulama Başlatma Hatası",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                // Uygulamayı kapat
                this.Shutdown(1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                // On exit: only create backup / update manifest if DB changed since last manifest hash
                var config = Services.ConfigurationProvider.Instance;
                var dbPath = config.GetDatabasePath();
                var localBase = config.GetDefaultBaseDir();
                string manifestPath = Path.Combine(localBase, "Manifest.json");

                // Create a consistent snapshot first and compute hash from it
                BackupResult res = null;
                try
                {
                    var backupManager = new BackupManager(dbPath);
                    try { res = backupManager.CreateBackup(); if (res == null) System.Diagnostics.Debug.WriteLine("OnExit: backup returned null"); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"OnExit: backup creation failed: {ex.Message}"); }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"OnExit: failed to initialize BackupManager: {ex.Message}");
                }

                string sourceForHash = res != null && res.IsSuccess && !string.IsNullOrEmpty(res.BackupPath) ? res.BackupPath : dbPath;
                string currentHash = null;
                try
                {
                    if (File.Exists(sourceForHash))
                    {
                        using var fs = File.OpenRead(sourceForHash);
                        using var sha = SHA256.Create();
                        var h = sha.ComputeHash(fs);
                        currentHash = BitConverter.ToString(h).Replace("-", "").ToLowerInvariant();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"OnExit: failed to compute DB hash from snapshot: {ex.Message}");
                }

                string existingHash = null;
                long existingVersion = 0;
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        var txt = File.ReadAllText(manifestPath);
                        using var doc = JsonDocument.Parse(txt);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("Hash", out var hElem)) existingHash = hElem.GetString();
                        if (root.TryGetProperty("Version", out var vElem) && vElem.TryGetInt64(out var vv)) existingVersion = vv;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"OnExit: failed to read existing manifest: {ex.Message}");
                    }
                }

                bool changed = !string.Equals(currentHash ?? string.Empty, existingHash ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                if (!changed)
                {
                    System.Diagnostics.Debug.WriteLine("OnExit: database unchanged since last manifest; skipping backup/upload and manifest update.");
                }
                else
                {
                    try
                    {
                        string sourceDbForUpload = sourceForHash;

                        // update simple Manifest.json according to requested format
                        try
                        {
                            Directory.CreateDirectory(localBase);
                            var newVersion = existingVersion + 1;
                            var manifestObj = new
                            {
                                MachineId = Environment.MachineName,
                                Version = newVersion,
                                LastModified = DateTime.UtcNow.ToString("o"),
                                Hash = currentHash
                            };
                            var opts = new JsonSerializerOptions { WriteIndented = true };
                            var json = JsonSerializer.Serialize(manifestObj, opts);
                            File.WriteAllText(manifestPath, json);
                            System.Diagnostics.Debug.WriteLine($"OnExit: manifest updated (v{newVersion})");

                            // If OneDrive configured, copy DB + manifest to OneDrive folder and wait until done
                            try
                            {
                                var oneDriveBase = config.GetOneDriveBaseDir();
                                if (!string.IsNullOrEmpty(oneDriveBase))
                                {
                                    Directory.CreateDirectory(oneDriveBase);

                                    // Create progress window
                                    Window progressWnd = null;
                                    Application.Current?.Dispatcher.Invoke(() =>
                                    {
                                        progressWnd = new Window
                                        {
                                            Title = "Senkronizasyon",
                                            Content = new TextBlock { Text = "SQLite bağlantısının kesilmesini ve dosyalarınızın bulut ortamına yüklenmesini bekleyin...", Margin = new Thickness(20), TextWrapping = TextWrapping.Wrap, Width = 400 },
                                            SizeToContent = SizeToContent.WidthAndHeight,
                                            WindowStartupLocation = WindowStartupLocation.CenterScreen,
                                            ResizeMode = ResizeMode.NoResize,
                                            Topmost = true
                                        };
                                    });

                                    Exception taskEx = null;
                                    var copyTask = Task.Run(() =>
                                    {
                                        try
                                        {
                                            var targetDbPath = Path.Combine(oneDriveBase, Path.GetFileName(dbPath));
                                            var tempDb = Path.Combine(oneDriveBase, Path.GetFileName(dbPath) + ".tmp." + Guid.NewGuid().ToString("N"));
                                            File.Copy(sourceDbForUpload, tempDb, true);
                                            if (File.Exists(targetDbPath))
                                            {
                                                var backupOld = targetDbPath + ".bak";
                                                File.Replace(tempDb, targetDbPath, backupOld);
                                                try { File.Delete(backupOld); } catch { }
                                            }
                                            else
                                            {
                                                File.Move(tempDb, targetDbPath);
                                            }

                                            var targetManifestPath = Path.Combine(oneDriveBase, Path.GetFileName(manifestPath));
                                            var tempMan = Path.Combine(oneDriveBase, Path.GetFileName(manifestPath) + ".tmp." + Guid.NewGuid().ToString("N"));
                                            File.Copy(manifestPath, tempMan, true);
                                            if (File.Exists(targetManifestPath))
                                            {
                                                var backupOld = targetManifestPath + ".bak";
                                                File.Replace(tempMan, targetManifestPath, backupOld);
                                                try { File.Delete(backupOld); } catch { }
                                            }
                                            else
                                            {
                                                File.Move(tempMan, targetManifestPath);
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            taskEx = ex;
                                        }
                                        finally
                                        {
                                            try { progressWnd?.Dispatcher.Invoke(() => { try { progressWnd.Close(); } catch { } }); } catch { }
                                        }
                                    });

                                    // Show dialog and wait until background copy closes it
                                    Application.Current?.Dispatcher.Invoke(() =>
                                    {
                                        try { progressWnd?.ShowDialog(); } catch { }
                                    });

                                    if (taskEx != null) System.Diagnostics.Debug.WriteLine($"OnExit: OneDrive copy task failed: {taskEx.Message}");
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"OnExit: OneDrive upload failed: {ex.Message}");
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"OnExit: failed to update manifest: {ex.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"OnExit: backup/update failed: {ex.Message}");
                    }
                }
            }
            catch { }

            base.OnExit(e);
        }

        /// <summary>
        /// Hata mesajını kullanıcı dostu formata dönüştürür
        /// </summary>
        private string FormatErrorMessage(Exception ex)
        {
            string message = "Uygulama başlatılırken kritik bir hata oluştu.\n\n";

            // Check for specific error types
            if (ex.Message.Contains("database") || ex.Message.Contains("Database"))
            {
                // Use configured paths for user-friendly messages
                string cfgDbPath = Services.ConfigurationProvider.Instance.GetDatabasePath();
                string cfgDbDir = System.IO.Path.GetDirectoryName(cfgDbPath) ?? Services.ConfigurationProvider.Instance.GetDefaultBaseDir();

                message += "▸ Veritabanı Sorunu Tespit Edildi\n\n";
                message += "Çözüm girisinde:\n";
                message += $"1. {cfgDbDir} klasörünün yazılabilir olup olmadığını kontrol edin\n";
                message += "2. Disk alanını kontrol edin (en az 50 MB boş alan)\n";
                message += "3. Veritabanı dosyasının kilitli olup olmadığını kontrol edin\n";
                message += "4. Uygulamayı Administrator olarak çalıştırmayı deneyin\n";
            }
            else if (ex.Message.Contains("access") || ex.Message.Contains("permission"))
            {
                message += "▸ İzin/Erişim Sorunu Tespit Edildi\n\n";
                message += "Çözüm önerileri:\n";
                message += "1. Uygulamayı Administrator olarak çalıştırın\n";
                string cfgDbPath2 = Services.ConfigurationProvider.Instance.GetDatabasePath();
                string cfgDbDir2 = System.IO.Path.GetDirectoryName(cfgDbPath2) ?? Services.ConfigurationProvider.Instance.GetDefaultBaseDir();
                message += $"2. {cfgDbDir2} klasörüne yazma izni kontrol edin\n";
                message += "3. Virüs tarama yazılımını geçici olarak devre dışı bırakın\n";
            }
            else if (ex.Message.Contains("disk") || ex.Message.Contains("space"))
            {
                message += "▸ Disk Alanı Sorunu Tespit Edildi\n\n";
                message += "Çözüm önerileri:\n";
                message += "1. C: sürücüsünde en az 50 MB boş alan oluşturun\n";
                message += "2. Gereksiz dosyaları silin\n";
                message += "3. Disk temizliği yapın\n";
            }
            else
            {
                message += $"Hata Detayı: {ex.Message}\n\n";
                message += "Çözüm önerileri:\n";
                message += "1. Uygulamayı yeniden başlatın\n";
                message += "2. Uygulamayı Administrator olarak çalıştırın\n";
                message += "3. Uygulamayı kaldırıp yeniden kurun\n";
            }

            message += "\n────────────────────────────\n";
            string logDir2 = Services.ConfigurationProvider.Instance.GetLogDirectory();
            message += $"Log dosyası: {logDir2}{System.IO.Path.DirectorySeparatorChar}migration_*.log\n";
            message += "Sorun devam ederse, lütfen log dosyasını teknik desteğe gönderin.";

            return message;
        }
    }

}

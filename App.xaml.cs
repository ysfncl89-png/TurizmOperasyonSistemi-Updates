using System.Configuration;
using System.Data;
using System.Windows;
using System.IO;
using Microsoft.EntityFrameworkCore;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Services;
using TurizmOperasyonSistemi.Windows;
using System.Text.Json;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi
{
    /// <summary>
    /// Interaction logic for App.xaml - Enterprise-grade database migration startup
    /// </summary>
    public partial class App : Application
    {
        private AppDbContext _context;
        private TurizmOperasyonSistemi.Services.SyncService _syncService;
        private TurizmOperasyonSistemi.Services.BackgroundServiceManager _backgroundServiceManager;

        public App()
        {
            InitializeComponent();
        }

        private UpdateInfo TryExtractUpdateInfo(object payload)
        {
            try
            {
                if (payload == null) return null;
                if (payload is UpdateInfo ui) return ui;

                if (payload is Models.Notification dbn)
                {
                    if (!string.IsNullOrEmpty(dbn.PayloadJson))
                    {
                        try
                        {
                            var info = JsonSerializer.Deserialize<UpdateInfo>(dbn.PayloadJson);
                            return info;
                        }
                        catch { }
                    }
                    return null;
                }

                // anonymous object case: try to read properties
                var t = payload.GetType();
                var verProp = t.GetProperty("Info") ?? t.GetProperty("info");
                if (verProp != null)
                {
                    var inner = verProp.GetValue(payload);
                    if (inner is UpdateInfo u2) return u2;
                    if (inner != null)
                    {
                        try { return JsonSerializer.Deserialize<UpdateInfo>(JsonSerializer.Serialize(inner)); } catch { }
                    }
                }

                // try direct mapping
                try
                {
                    var ser = JsonSerializer.Serialize(payload);
                    return JsonSerializer.Deserialize<UpdateInfo>(ser);
                }
                catch { }

                return null;
            }
            catch { return null; }
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Enterprise startup: initialize configuration, run health checks, and apply migrations using configured paths
            try
            {
                var config = Services.ConfigurationProvider.Instance;
                config.LogConfiguration();

                // Note: initial sync moved to background services to avoid blocking startup (handled by BackgroundServiceManager)
                _syncService = new SyncService();

                string dbPath = config.GetDatabasePath();

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

                    if (!migrationsSucceeded)
                    {
                        System.Diagnostics.Debug.WriteLine("\n⚠ UYARI: Migration işlemi sırasında hata oluştu.");
                        System.Diagnostics.Debug.WriteLine("⚠ Bazı özellikler doğru çalışmayabilir.");
                        System.Diagnostics.Debug.WriteLine($"⚠ Lütfen log dosyalarını inceleyiniz: {config.GetLogDirectory()}");
                    }

                    // 3. Tüm custom tablolar için ensure metodlarını çalıştır
                    System.Diagnostics.Debug.WriteLine("\n► Custom tablolar kontrol ediliyorum...");
                    migrationService.EnsureAllCustomTablesExists();
                    System.Diagnostics.Debug.WriteLine("✓ Custom tablolar hazır");

                    // 4. Diğer ön başlatma işlemleri
                    System.Diagnostics.Debug.WriteLine("\n► Ek şema ayarlamaları yapılıyor...");
                    context.EnsureNationalityColumnExists();
                    context.EnsureRegistrationPassportArrivedColumnExists();
                    context.EnsureRegistrationStatusChoiceColumnsExist();
                    context.NormalizeRegistrationNullValues();
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

                    // Register Sync background service
                    var syncBg = new SyncBackgroundService(_syncService);
                    _backgroundServiceManager.RegisterService(syncBg);

                    // Register Update background service
                    var updateChecker = new UpdateCheckerService();
                    var updateDownloader = new UpdateDownloadService();
                    var updateInstaller = new UpdateInstallerService();
                    var updateBg = new UpdateBackgroundService(updateChecker, updateDownloader, updateInstaller);
                    _backgroundServiceManager.RegisterService(updateBg);

                    // Start all registered background services
                    await _backgroundServiceManager.StartAllAsync().ConfigureAwait(false);
                    // Subscribe to update notifications for minimal UI interactions
                    try
                    {
                        NotificationCenter.Subscribe((key, payload) =>
                        {
                            try
                            {
                                if (key == "update.available" || key == "update.migration.required" || key == "update.both.required")
                                {
                                    var info = TryExtractUpdateInfo(payload);
                                    if (info == null) return;

                                    // If auto-download disabled, ask user
                                    if (!Services.ConfigurationProvider.Instance.IsAutoDownloadEnabled())
                                    {
                                        Application.Current?.Dispatcher.Invoke(async () =>
                                        {
                                            try
                                            {
                                                var msg = key == "update.migration.required"
                                                    ? $"Veritabanı güncellemesi bulundu.\n\nSürüm: {info.Version}\nHedef DB versiyonu: {info.MinimumDatabaseVersion?.ToString() ?? "N/A"}\n\nNotlar: {info.ReleaseNotes}\n\nİndirilsin mi?"
                                                    : key == "update.both.required"
                                                        ? $"Uygulama ve veritabanı güncellemesi bulundu.\n\nSürüm: {info.Version}\nHedef DB versiyonu: {info.MinimumDatabaseVersion?.ToString() ?? "N/A"}\n\nNotlar: {info.ReleaseNotes}\n\nİndirilsin mi?"
                                                        : $"Yeni sürüm {info.Version} bulundu.\n\nNotlar: {info.ReleaseNotes}\n\nİndirilsin mi?";
                                                var res = MessageBox.Show(msg, "Güncelleme Bulundu", MessageBoxButton.YesNo, MessageBoxImage.Information);
                                                if (res == MessageBoxResult.Yes)
                                                {
                                                    var downloader = new UpdateDownloadService();
                                                    var dl = await downloader.DownloadAsync(info).ConfigureAwait(false);
                                                    if (dl != null && dl.Success)
                                                    {
                                                        NotificationCenter.Publish("update.downloaded", new { Info = info, Path = dl.FilePath }, persistToDb: true);
                                                        // Prompt to install
                                                        var installRes = MessageBox.Show("İndirme tamamlandı. Şimdi kurulumu başlatmak ister misiniz?", "Güncelleme İndirildi", MessageBoxButton.YesNo, MessageBoxImage.Question);
                                                        if (installRes == MessageBoxResult.Yes)
                                                        {
                                                            var installer = new UpdateInstallerService();
                                                            var started = await installer.LaunchInstallerAsync(dl.FilePath).ConfigureAwait(false);
                                                            if (started)
                                                            {
                                                                try { Application.Current.Shutdown(); } catch { }
                                                            }
                                                        }
                                                    }
                                                    else
                                                    {
                                                        MessageBox.Show($"Güncelleme indirilemedi: {dl?.ErrorMessage}", "İndirme Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
                                                    }
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                System.Diagnostics.Debug.WriteLine($"Update UI handler error: {ex.Message}");
                                            }
                                        });
                                    }
                                    else
                                    {
                                        // AutoDownload enabled: optionally notify user that download will proceed in background
                                        Application.Current?.Dispatcher.Invoke(() =>
                                        {
                                            try
                                            {
                                                MessageBox.Show(key == "update.migration.required"
                                                    ? "Veritabanı güncellemesi bulundu. İndiriliyor arka planda."
                                                    : key == "update.both.required"
                                                        ? "Uygulama ve veritabanı güncellemesi bulundu. İndiriliyor arka planda."
                                                        : $"Yeni sürüm {info.Version} bulundu. İndiriliyor arka planda.", "Güncelleme", MessageBoxButton.OK, MessageBoxImage.Information);
                                            }
                                            catch { }
                                        });
                                    }
                                }
                                else if (key == "update.downloaded")
                                {
                                    // payload may be anonymous object or Notification model
                                    string path = null;
                                    try
                                    {
                                        if (payload is Models.Notification dbn)
                                        {
                                            if (!string.IsNullOrEmpty(dbn.PayloadJson))
                                            {
                                                using var doc = JsonDocument.Parse(dbn.PayloadJson);
                                                var root = doc.RootElement;
                                                if (root.TryGetProperty("Path", out var p)) path = p.GetString();
                                                else if (root.TryGetProperty("FilePath", out var fp)) path = fp.GetString();
                                            }
                                        }
                                        else
                                        {
                                            // anonymous object with Path property
                                            var pt = payload?.GetType().GetProperty("Path");
                                            if (pt != null) path = pt.GetValue(payload)?.ToString();
                                        }
                                    }
                                    catch { }

                                    if (!string.IsNullOrEmpty(path))
                                    {
                                        Application.Current?.Dispatcher.Invoke(async () =>
                                        {
                                            try
                                            {
                                                var ask = MessageBox.Show("Güncelleme indirildi. Şimdi kurulumu başlatmak ister misiniz?", "Güncelleme Hazır", MessageBoxButton.YesNo, MessageBoxImage.Question);
                                                if (ask == MessageBoxResult.Yes)
                                                {
                                                    var installer = new UpdateInstallerService();
                                                    var started = await installer.LaunchInstallerAsync(path).ConfigureAwait(false);
                                                    if (started)
                                                    {
                                                        try { Application.Current.Shutdown(); } catch { }
                                                    }
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                System.Diagnostics.Debug.WriteLine($"Update downloaded UI error: {ex.Message}");
                                            }
                                        });
                                    }
                                }
                            }
                            catch { }
                        });
                    }
                    catch (Exception subEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to subscribe to notifications: {subEx.Message}");
                    }
                }
                catch (Exception bgEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to start background services: {bgEx.Message}");
                }
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

        protected override async void OnExit(ExitEventArgs e)
        {
            try
            {
                // Ensure we attempt a safe shutdown sync: backup + manifest push
                try
                {
                    if (_syncService != null)
                    {
                        await _syncService.PerformShutdownSyncAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Shutdown sync failed: {ex.Message}");
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
                string cfgDbDir = System.IO.Path.GetDirectoryName(cfgDbPath) ?? "C:\\TurizmDB";

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
                string cfgDbDir2 = System.IO.Path.GetDirectoryName(cfgDbPath2) ?? "C:\\TurizmDB";
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

using System.Configuration;
using System.Data;
using System.Windows;
using System.IO;
using Microsoft.EntityFrameworkCore;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Services;
using TurizmOperasyonSistemi.Windows;

namespace TurizmOperasyonSistemi
{
    /// <summary>
    /// Interaction logic for App.xaml - Enterprise-grade database migration startup
    /// </summary>
    public partial class App : Application
    {
        private AppDbContext _context;
        private TurizmOperasyonSistemi.Services.SyncService _syncService;

        public App()
        {
            InitializeComponent();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Enterprise startup: initialize configuration, run health checks, and apply migrations using configured paths
            try
            {
                var config = Services.ConfigurationProvider.Instance;
                config.LogConfiguration();

                // Başlangıç senkronizasyonu: OneDrive ile manifest karşılaştırması ve gerekirse restore/push
                try
                {
                    _syncService = new SyncService();
                    await _syncService.StartInitialSyncAsync();
                }
                catch (Exception sx)
                {
                    System.Diagnostics.Debug.WriteLine($"Initial sync failed: {sx.Message}");
                }

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

                // Start background update check after UI is available (non-blocking)
                try
                {
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
                    {
                        try
                        {
                            var updateService = new UpdateService();
                            await updateService.StartBackgroundCheckAsync().ConfigureAwait(true);
                        }
                        catch (Exception uex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Background update check failed: {uex.Message}");
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
                catch (Exception dex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to schedule background update check: {dex.Message}");
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

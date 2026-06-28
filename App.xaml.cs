using System.Configuration;
using System.Data;
using System.Windows;
using System.IO;
using Microsoft.EntityFrameworkCore;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Services;

namespace TurizmOperasyonSistemi
{
    /// <summary>
    /// Interaction logic for App.xaml - Enterprise-grade database migration startup
    /// </summary>
    public partial class App : Application
    {
        private const string DB_PATH = @"C:\TurizmDB\turizm.db";

        public App()
        {
            // Database versiyonlama ve migration'ları otomatik ve güvenli şekilde yönet (Enterprise-Grade)
            try
            {
                System.Diagnostics.Debug.WriteLine("\n╔══════════════════════════════════════════════════════╗");
                System.Diagnostics.Debug.WriteLine("║         APPLICATION STARTUP (ENTERPRISE)              ║");
                System.Diagnostics.Debug.WriteLine("╚══════════════════════════════════════════════════════╝");
                System.Diagnostics.Debug.WriteLine($"Tarih/Saat: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                System.Diagnostics.Debug.WriteLine($"Uygulama Versiyonu: {MigrationService.GetApplicationVersion()}");
                System.Diagnostics.Debug.WriteLine($"Veritabanı Yolu: {DB_PATH}");

                using (var context = new AppDbContext())
                {
                    // ENTERPRISE-GRADE MigrationService - Robust, validated, with backup & lock
                    var migrationService = new MigrationService(context, DB_PATH);

                    // 1. Veritabanı versiyonunu oku
                    string currentDbVersion = migrationService.GetDatabaseVersion();
                    System.Diagnostics.Debug.WriteLine($"\nMevcut Database Versiyonu: {currentDbVersion ?? "Hiç"}");

                    // 2. Migration'ları uygula (eksik olanlar otomatikman çalışacak)
                    // Bu proses aşağıdakileri otomatik yapıyor:
                    // ✓ Pre-migration validation (disk alan, yazma izni, vs)
                    // ✓ Database backup'ı oluşturma
                    // ✓ Migration lock alıp eş zamanlı işlemı engelleme
                    // ✓ Retry logic ile geçişi hatalardan korunma
                    // ✓ Hata oluşursa backup'tan oto restore
                    bool migrationsSucceeded = migrationService.ApplyMigrations();

                    if (!migrationsSucceeded)
                    {
                        System.Diagnostics.Debug.WriteLine("\n⚠ UYARI: Migration işlemi sırasında hata oluştu.");
                        System.Diagnostics.Debug.WriteLine("⚠ Bazı özellikler doğru çalışmayabilir.");
                        System.Diagnostics.Debug.WriteLine("⚠ Lütfen log dosyalarını inceleyiniz: C:\\TurizmDB\\logs\\");
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

                System.Diagnostics.Debug.WriteLine("\nLog dosyaları burası: C:\\TurizmDB\\logs\\");
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

        /// <summary>
        /// Hata mesajını kullanıcı dostu formata dönüştürür
        /// </summary>
        private string FormatErrorMessage(Exception ex)
        {
            string message = "Uygulama başlatılırken kritik bir hata oluştu.\n\n";

            // Check for specific error types
            if (ex.Message.Contains("database") || ex.Message.Contains("Database"))
            {
                message += "▸ Veritabanı Sorunu Tespit Edildi\n\n";
                message += "Çözüm girisinde:\n";
                message += "1. C:\\TurizmDB klasörünün yazılabilir olup olmadığını kontrol edin\n";
                message += "2. Disk alanını kontrol edin (en az 50 MB boş alan)\n";
                message += "3. Veritabanı dosyasının kilitli olup olmadığını kontrol edin\n";
                message += "4. Uygulamayı Administrator olarak çalıştırmayı deneyin\n";
            }
            else if (ex.Message.Contains("access") || ex.Message.Contains("permission"))
            {
                message += "▸ İzin/Erişim Sorunu Tespit Edildi\n\n";
                message += "Çözüm önerileri:\n";
                message += "1. Uygulamayı Administrator olarak çalıştırın\n";
                message += "2. C:\\TurizmDB klasörüne yazma izni kontrol edin\n";
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
            message += "Log dosyası: C:\\TurizmDB\\logs\\migration_*.log\n";
            message += "Sorun devam ederse, lütfen log dosyasını teknik desteğe gönderin.";

            return message;
        }
    }

}

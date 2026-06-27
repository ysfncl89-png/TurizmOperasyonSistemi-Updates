using System.Configuration;
using System.Data;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Services;

namespace TurizmOperasyonSistemi
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            // Database versiyonlama ve migration'ları otomatik olarak yönet
            try
            {
                System.Diagnostics.Debug.WriteLine("\n========== APPLICATION STARTUP ==========");
                System.Diagnostics.Debug.WriteLine($"Application Version: {MigrationService.GetApplicationVersion()}");

                using (var context = new AppDbContext())
                {
                    // MigrationService kullanarak veritabanı güncellemesini yönet
                    var migrationService = new MigrationService(context);

                    // 1. Veritabanı versiyonunu oku
                    string currentDbVersion = migrationService.GetDatabaseVersion();
                    System.Diagnostics.Debug.WriteLine($"Current Database Version: {currentDbVersion ?? "None"}");

                    // 2. Migration'ları uygula (eksik olanlar otomatikman çalışacak)
                    bool migrationsSucceeded = migrationService.ApplyMigrations();

                    if (!migrationsSucceeded)
                    {
                        System.Diagnostics.Debug.WriteLine("WARNING: Migration process encountered errors. Some features may not work correctly.");
                    }

                    // 3. Tüm custom tablolar için ensure metodlarını çalıştır
                    migrationService.EnsureAllCustomTablesExists();

                    // Diğer ön başlatma işlemleri
                    context.EnsureNationalityColumnExists();
                    context.EnsureRegistrationPassportArrivedColumnExists();
                    context.EnsureRegistrationStatusChoiceColumnsExist();
                    context.NormalizeRegistrationNullValues();
                }

                // Seed missing PermissionsJson values based on role defaults for existing users
                try
                {
                    UserPermissionSeeder.EnsureUserPermissionsSeeded();
                }
                catch (Exception permEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Warning: UserPermissionSeeder failed: {permEx.Message}");
                }

                System.Diagnostics.Debug.WriteLine("========== APPLICATION STARTED SUCCESSFULLY ==========\n");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("========== CRITICAL ERROR DURING STARTUP ==========");
                System.Diagnostics.Debug.WriteLine($"Database initialization error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                System.Diagnostics.Debug.WriteLine("==============================================\n");

                // User'a hata mesajı göster
                MessageBox.Show(
                    $"Uygulama başlatılırken bir hata oluştu:\n\n{ex.Message}\n\n" +
                    "Lütfen veritabanı bağlantısını kontrol edin ve uygulamayı yeniden başlatın.",
                    "Başlatma Hatası",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

}

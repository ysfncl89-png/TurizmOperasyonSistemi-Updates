using System.Configuration;
using System.Data;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using TurizmOperasyonSistemi.Data;

namespace TurizmOperasyonSistemi
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            // Database migration'larını otomatik olarak çalıştır
            try
            {
                using (var context = new AppDbContext())
                {
                    // Önce veritabanının oluşturulduğundan emin ol
                    context.Database.EnsureCreated();

                    // Ardından migration'ları uygula
                    var pendingMigrations = context.Database.GetPendingMigrations();
                    if (pendingMigrations.Any())
                    {
                        context.Database.Migrate();
                    }

                    // Tüm custom tablolar için ensure metodlarını çalıştır
                    context.EnsureRoomAllocationHistoryTableExists();
                    context.EnsurePaymentsTableExists();
                    context.EnsureNotificationsTableExists();
                    context.EnsureUsersTableExists();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Database initialization error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }

}

using System;
using System.Linq;
using System.Windows;
using TurizmOperasyonSistemi.Services;
using TurizmOperasyonSistemi.Data;

namespace TurizmOperasyonSistemi.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();

            BtnClose.Click += (s, e) => { Close(); };

            try
            {
                var programVersion = ManifestService.GetAppVersionSafe();
                TxtProgramVersion.Text = $"Kurulu Uygulama Sürümü: {programVersion}";
                TxtReleaseTag.Text = $"GitHub Release Etiketi: v{programVersion}";

                try
                {
                    using (var ctx = new AppDbContext())
                    {
                        var migrationService = new MigrationService(ctx);
                        var dbVersion = migrationService.GetDatabaseVersion();
                        var applied = migrationService.GetAppliedMigrations()?.ToList() ?? new System.Collections.Generic.List<string>();

                        var dbDisplay = string.IsNullOrEmpty(dbVersion) || dbVersion == "0.0.0.0" ? "1.0.0.0" : dbVersion;
                        TxtDatabaseVersion.Text = dbVersion != null ? $"Database Sürümü: {dbDisplay}" : "Database sürümü kaydı yok.";
                        TxtMigrationVersion.Text = applied.Any()
                            ? $"Son uygulanan migration: {applied.Last()} (Toplam: {applied.Count})"
                            : "Uygulanan migration yok.";
                    }
                }
                catch (Exception ex)
                {
                    TxtDatabaseVersion.Text = "Veritabanına erişilemedi.";
                    TxtMigrationVersion.Text = "Migration bilgisi alınamadı.";
                    System.Diagnostics.Debug.WriteLine($"AboutWindow DB read error: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AboutWindow init error: {ex.Message}");
            }
        }
    }
}

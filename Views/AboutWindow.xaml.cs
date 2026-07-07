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

            BtnClose.Click += (s, e) => { this.Close(); };

            try
            {
                // Program versiyonu - prefer ManifestService helper which returns a sensible default
                var programVer = TurizmOperasyonSistemi.Services.ManifestService.GetAppVersionSafe();
                TxtProgramVersion.Text = $"Program Versiyonu: {programVer}";

                // Manifest bilgileri
                var manifestService = new ManifestService();
                var manifest = manifestService.LoadManifest();
                if (manifest != null)
                {
                    TxtManifestVersion.Text = $"Manifest Db Versiyonu: {manifest.DbVersion}";
                    var appVer = string.IsNullOrEmpty(manifest.AppVersion) || manifest.AppVersion == "0.0.0.0" ? TurizmOperasyonSistemi.Services.ManifestService.GetAppVersionSafe() : manifest.AppVersion;
                    TxtManifestAppVersion.Text = $"Manifest App Versiyonu: {appVer}";
                }
                else
                {
                    TxtManifestVersion.Text = "Manifest dosyası bulunamadı.";
                    TxtManifestAppVersion.Text = string.Empty;
                }

                // Migration ve Database versiyonları (veritabanına erişilebiliyorsa)
                try
                {
                    using (var ctx = new AppDbContext())
                    {
                        var migrationService = new MigrationService(ctx);
                        var dbVer = migrationService.GetDatabaseVersion();
                        var applied = migrationService.GetAppliedMigrations()?.ToList() ?? new System.Collections.Generic.List<string>();

                        // If DB version is 0.0.0.0 or null, show 1.0.0.0 as sensible default
                        var dbDisplay = string.IsNullOrEmpty(dbVer) || dbVer == "0.0.0.0" ? "1.0.0.0" : dbVer;
                        TxtDatabaseVersion.Text = dbVer != null ? $"Database Versiyonu: {dbDisplay}" : "Database versiyonu kaydı yok.";
                        TxtMigrationVersion.Text = applied.Any() ? $"Uygulanan Migration (son): {applied.Last()} (Toplam: {applied.Count})" : "Uygulanan migration yok.";
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

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
                        var pending = migrationService.GetPendingMigrations()?.ToList() ?? new System.Collections.Generic.List<string>();

                        var dbDisplay = string.IsNullOrEmpty(dbVersion) || dbVersion == "0.0.0.0" ? "1.0.0.0" : dbVersion;
                        TxtDatabaseVersion.Text = dbVersion != null ? $"Database Sürümü: {dbDisplay}" : "Database sürümü kaydı yok.";
                        TxtMigrationVersion.Text = applied.Any()
                            ? $"Son uygulanan migration: {applied.Last()} (Toplam: {applied.Count})"
                            : "Uygulanan migration yok.";

                        // Show pending migrations: if any, display first pending and total count; otherwise say none pending
                        if (pending.Any())
                        {
                            TxtPendingMigrations.Text = pending.Count == 1
                                ? $"Bekleyen migration: {pending.First()}"
                                : $"Bekleyen migration: {pending.First()} (Toplam: {pending.Count})";
                        }
                        else
                        {
                            TxtPendingMigrations.Text = "Bekleyen migration yok.";
                        }
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

            UpdateStatus();
        }

        private void UpdateStatus()
        {
            try
            {
                var cfg = Services.ConfigurationProvider.Instance;
                if (!cfg.IsVelopackUpdatesEnabled() || string.IsNullOrWhiteSpace(cfg.GetVelopackRepositoryUrl()))
                {
                    TxtUpdateStatus.Text = "Güncelleme kapalı";
                    UpdateStatusIcon.Text = "";
                    return;
                }

                TxtUpdateStatus.Text = "GitHub Releases üzerinden başlangıçta kontrol ediliyor";
                UpdateStatusIcon.Text = "↻";
                UpdateStatusIcon.Foreground = System.Windows.Media.Brushes.DeepSkyBlue;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateStatus failed: {ex.Message}");
            }
        }
    }
}

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
            BtnGetLatest.Click += BtnGetLatest_Click;

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
            var state = VelopackUpdateService.CurrentState;
            switch (state)
            {
                case UpdateState.UpToDate:
                    SetUpdateStatus("Program Güncel", "✓", System.Windows.Media.Brushes.LightGreen, false);
                    break;
                case UpdateState.Checking:
                    SetUpdateStatus("GitHub Releases üzerinden kontrol ediliyor", "↻", System.Windows.Media.Brushes.DeepSkyBlue, false);
                    break;
                case UpdateState.Applying:
                    SetUpdateStatus("Güncelleme uygulanıyor...", "↻", System.Windows.Media.Brushes.DeepSkyBlue, false);
                    break;
                case UpdateState.NotChecked:
                    SetUpdateStatus("Güncelleme durumu henüz kontrol edilmedi", "i", System.Windows.Media.Brushes.LightGray, true);
                    break;
                case UpdateState.UpdateAvailable:
                    SetUpdateStatus("Yeni güncelleme bulundu", "!", System.Windows.Media.Brushes.Gold, true);
                    break;
                case UpdateState.Deferred:
                    SetUpdateStatus("Güncelleme ertelendi", "!", System.Windows.Media.Brushes.Gold, true);
                    break;
                case UpdateState.Failed:
                    SetUpdateStatus("Güncelleme kontrol edilemedi", "!", System.Windows.Media.Brushes.Salmon, true);
                    break;
                case UpdateState.Disabled:
                    SetUpdateStatus("Güncelleme kapalı", "", System.Windows.Media.Brushes.LightGray, false);
                    break;
                default:
                    SetUpdateStatus("Güncelleme durumu bilinmiyor", "!", System.Windows.Media.Brushes.LightGray, true);
                    break;
            }
        }

        private void SetUpdateStatus(string text, string icon, System.Windows.Media.Brush color, bool showGetLatest)
        {
            TxtUpdateStatus.Text = text;
            TxtUpdateStatus.Foreground = color;
            UpdateStatusIcon.Text = icon;
            UpdateStatusIcon.Foreground = color;
            BtnGetLatest.Visibility = showGetLatest ? Visibility.Visible : Visibility.Collapsed;
            BtnGetLatest.IsEnabled = showGetLatest;
        }

        private async void BtnGetLatest_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var updateTask = new VelopackUpdateService().CheckAndApplyAsync(forceCheck: true);
                UpdateStatus();
                await updateTask;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Manual update check failed: {ex.Message}");
            }
            finally
            {
                UpdateStatus();
            }
        }
    }
}

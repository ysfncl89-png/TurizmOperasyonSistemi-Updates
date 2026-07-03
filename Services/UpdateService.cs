using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace TurizmOperasyonSistemi.Services
{
    /// <summary>
    /// Simple UpdateService that checks a JSON manifest (latest.json) and downloads the asset if a newer version is available.
    /// This implementation is intentionally minimal: it reads manifest URL from ConfigurationProvider and performs a one-time check.
    /// Full features (checksum, Updater.exe integration, mandatory updates) will be added in next steps.
    /// </summary>
    public class UpdateService
    {
        private readonly ConfigurationProvider _config = ConfigurationProvider.Instance;

        public UpdateService()
        {
        }

        public async Task StartBackgroundCheckAsync()
        {
            try
            {
                if (!_config.IsUpdateEnabled())
                {
                    Debug.WriteLine("UpdateService: update check disabled in configuration.");
                    return;
                }

                var manifestUrl = _config.GetUpdateManifestUrl();
                if (string.IsNullOrEmpty(manifestUrl))
                {
                    Debug.WriteLine("UpdateService: no manifest URL configured; skipping update check.");
                    return;
                }

                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(30);
                    Debug.WriteLine($"UpdateService: fetching manifest {manifestUrl}");
                    var resp = await http.GetAsync(manifestUrl).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                    {
                        Debug.WriteLine($"UpdateService: manifest fetch failed: {resp.StatusCode}");
                        return;
                    }

                    var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var newVersion = root.GetProperty("version").GetString();
                    var downloadUrl = root.GetProperty("url").GetString();

                    if (string.IsNullOrEmpty(newVersion) || string.IsNullOrEmpty(downloadUrl))
                    {
                        Debug.WriteLine("UpdateService: manifest missing required fields.");
                        return;
                    }

                    var current = MigrationService.GetApplicationVersion();
                    if (TryParseVersion(newVersion, out var vNew) && TryParseVersion(current, out var vCur))
                    {
                        if (vNew <= vCur)
                        {
                            Debug.WriteLine($"UpdateService: no newer version. Current={vCur} Latest={vNew}");
                            return;
                        }
                    }
                    else
                    {
                        if (string.Equals(newVersion, current, StringComparison.OrdinalIgnoreCase))
                        {
                            Debug.WriteLine("UpdateService: versions equal by string compare; skipping.");
                            return;
                        }
                    }

                    // Download asset to temp
                    var tempDir = Path.Combine(Path.GetTempPath(), "TurizmUpdater");
                    Directory.CreateDirectory(tempDir);
                    var fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
                    var targetPath = Path.Combine(tempDir, fileName);

                    Debug.WriteLine($"UpdateService: downloading {downloadUrl} -> {targetPath}");
                    using (var downloadResp = await http.GetAsync(downloadUrl).ConfigureAwait(false))
                    {
                        if (!downloadResp.IsSuccessStatusCode)
                        {
                            Debug.WriteLine($"UpdateService: asset download failed: {downloadResp.StatusCode}");
                            return;
                        }

                        using (var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await downloadResp.Content.CopyToAsync(fs).ConfigureAwait(false);
                        }
                    }

                    // Ask user to run installer via UI thread and delegate install to UpdateInstallerService
                    Application.Current?.Dispatcher.Invoke(async () =>
                    {
                        try
                        {
                            var message = $"Yeni sürüm {newVersion} bulundu. İndirildi: {targetPath}\n\nUygulamayı kapatıp kurulumu başlatmak ister misiniz?";
                            var res = MessageBox.Show(message, "Güncelleme Bulundu", MessageBoxButton.YesNo, MessageBoxImage.Information);
                            if (res == MessageBoxResult.Yes)
                            {
                                var installer = new UpdateInstallerService();
                                await installer.LaunchInstallerAsync(targetPath).ConfigureAwait(false);
                            }
                        }
                        catch (Exception uex)
                        {
                            Debug.WriteLine($"UpdateService UI error: {uex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateService error: {ex.Message}");
            }
        }

        private bool TryParseVersion(string ver, out Version v)
        {
            v = null;
            if (string.IsNullOrEmpty(ver)) return false;
            ver = ver.TrimStart('v', 'V');
            return Version.TryParse(ver, out v);
        }
    }
}

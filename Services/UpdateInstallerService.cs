using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace TurizmOperasyonSistemi.Services
{
    /// <summary>
    /// Simple installer helper that launches the downloaded asset or forwards to Updater.exe for zip packages.
    /// </summary>
    public class UpdateInstallerService
    {
        public UpdateInstallerService()
        {
        }

        public async Task LaunchInstallerAsync(string assetPath)
        {
            try
            {
                if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath))
                    throw new FileNotFoundException("Asset not found", assetPath);

                var ext = Path.GetExtension(assetPath).ToLowerInvariant();

                if (ext == ".exe" || ext == ".msi")
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = assetPath,
                        UseShellExecute = true
                    });

                    // Give installer a moment then shutdown the app to allow replacement
                    await Task.Delay(500);
                    Application.Current.Shutdown();
                    return;
                }

                if (ext == ".zip")
                {
                    // Prefer Updater.exe in application tools if present
                    var appDir = AppDomain.CurrentDomain.BaseDirectory;
                    var updaterPath = Path.Combine(appDir, "tools", "Updater", "Updater.exe");
                    if (File.Exists(updaterPath))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = updaterPath,
                            Arguments = $"\"{assetPath}\"",
                            UseShellExecute = true
                        });

                        await Task.Delay(500);
                        Application.Current.Shutdown();
                        return;
                    }

                    // Fallback: open folder for user to run manually
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Path.GetDirectoryName(assetPath),
                        UseShellExecute = true
                    });

                    MessageBox.Show("Güncellenen paket zip olarak indirildi. Lütfen Updater.exe ile açın veya manuel olarak içeriği uygulama dizinine yerleştirin.",
                        "Güncelleme Hazır", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Unknown extension: just start it
                Process.Start(new ProcessStartInfo
                {
                    FileName = assetPath,
                    UseShellExecute = true
                });

                await Task.Delay(500);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateInstallerService error: {ex.Message}");
                MessageBox.Show($"Güncelleyici başlatılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

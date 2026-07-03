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

        /// <summary>
        /// Launches installer/updater for the given asset path. Returns true on success (process started).
        /// This method does NOT show UI; it only starts the external process and returns the result.
        /// </summary>
        public Task<bool> LaunchInstallerAsync(string assetPath)
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

                    // Caller decides when to shutdown the app
                    return Task.FromResult(true);
                }

                if (ext == ".zip")
                {
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

                        return Task.FromResult(true);
                    }

                    // Fallback: open folder for user
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Path.GetDirectoryName(assetPath),
                        UseShellExecute = true
                    });

                    return Task.FromResult(true);
                }

                // Unknown extension: just start it
                Process.Start(new ProcessStartInfo
                {
                    FileName = assetPath,
                    UseShellExecute = true
                });

                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateInstallerService error: {ex.Message}");
                return Task.FromResult(false);
            }
        }
    }
}

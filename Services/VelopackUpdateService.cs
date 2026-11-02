using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Velopack;
using Velopack.Sources;

namespace TurizmOperasyonSistemi.Services
{
    /// <summary>
    /// Checks and applies application updates from a public GitHub Releases repository.
    /// Database and LocalAppData update manifests are deliberately outside this flow.
    /// </summary>
    public sealed class VelopackUpdateService
    {
        private readonly ConfigurationProvider _configuration = ConfigurationProvider.Instance;

        public async Task<bool> CheckAndApplyAsync(CancellationToken cancellationToken = default)
        {
#if DEBUG
            return false;
#else
            if (!_configuration.IsVelopackUpdatesEnabled())
                return false;

            var repositoryUrl = _configuration.GetVelopackRepositoryUrl();
            if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var repositoryUri)
                || repositoryUri.Scheme != Uri.UriSchemeHttps
                || !IsGitHubRepository(repositoryUri))
            {
                return false;
            }

            if (ShouldSkipCheck())
                return false;

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var source = new GithubSource(
                    repositoryUri.AbsoluteUri.TrimEnd('/'),
                    accessToken: null,
                    prerelease: _configuration.AllowVelopackPrerelease());
                var manager = new UpdateManager(source);
                var release = await manager.CheckForUpdatesAsync().ConfigureAwait(true);
                SaveCheckTime();

                if (release is null)
                    return false;

                var version = release.TargetFullRelease.Version.ToString();
                var notes = release.TargetFullRelease.NotesMarkdown;
                var message = string.IsNullOrWhiteSpace(notes)
                    ? $"TOS Lite için yeni bir sürüm bulundu: {version}. Güncelleme indirilsin ve uygulama yeniden başlatılsın mı?"
                    : $"TOS Lite için yeni bir sürüm bulundu: {version}.\n\n{notes}\n\nGüncelleme indirilsin ve uygulama yeniden başlatılsın mı?";

                var answer = MessageBox.Show(
                    message,
                    "TOS Lite güncellemesi",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (answer != MessageBoxResult.Yes)
                    return false;

                cancellationToken.ThrowIfCancellationRequested();
                await manager.DownloadUpdatesAsync(release).ConfigureAwait(true);
                manager.ApplyUpdatesAndRestart(release);
                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                Debug.WriteLine($"Velopack update check skipped: {ex.Message}");
                ClearCheckTime();
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Velopack update failed: {ex.Message}");
                ClearCheckTime();
                MessageBox.Show(
                    $"Güncelleme uygulanamadı. Mevcut sürümle devam edilecek.\n\n{ex.Message}",
                    "TOS Lite güncellemesi",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }
#endif
        }

        private bool ShouldSkipCheck()
        {
            var stampPath = CheckTimePath;
            if (!File.Exists(stampPath))
                return false;

            return DateTimeOffset.TryParse(File.ReadAllText(stampPath), out var lastCheck)
                && DateTimeOffset.UtcNow - lastCheck < TimeSpan.FromMinutes(_configuration.GetVelopackCheckIntervalMinutes());
        }

        private void SaveCheckTime()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CheckTimePath) ?? _configuration.GetDefaultBaseDir());
            File.WriteAllText(CheckTimePath, DateTimeOffset.UtcNow.ToString("O"));
        }

        private void ClearCheckTime()
        {
            try
            {
                File.Delete(CheckTimePath);
            }
            catch (IOException)
            {
                // A failed timestamp cleanup must not prevent the application from starting.
            }
        }

        private string CheckTimePath => Path.Combine(
            _configuration.GetDefaultBaseDir(),
            "velopack-update-check-utc.txt");

        private static bool IsGitHubRepository(Uri repositoryUri)
        {
            return string.Equals(repositoryUri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                && repositoryUri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).Length == 2;
        }
    }
}

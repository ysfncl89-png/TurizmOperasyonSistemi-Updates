using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Velopack;
using Velopack.Sources;
using TurizmOperasyonSistemi.Views;

namespace TurizmOperasyonSistemi.Services
{
    public enum UpdateState
    {
        NotChecked,
        Checking,
        UpToDate,
        UpdateAvailable,
        Deferred,
        Applying,
        Failed,
        Disabled
    }

    /// <summary>
    /// Checks and applies application updates from a public GitHub Releases repository.
    /// Database and LocalAppData update manifests are deliberately outside this flow.
    /// </summary>
    public sealed class VelopackUpdateService
    {
        private readonly ConfigurationProvider _configuration = ConfigurationProvider.Instance;
        public static UpdateState CurrentState { get; private set; } = UpdateState.NotChecked;

        public async Task<bool> CheckAndApplyAsync(CancellationToken cancellationToken = default, bool forceCheck = false)
        {
#if DEBUG
            SetState(UpdateState.Disabled);
            return false;
#else
            SetState(UpdateState.Checking);
            string repositoryUrl;
            try
            {
                if (!_configuration.IsVelopackUpdatesEnabled())
                {
                    SetState(UpdateState.Disabled);
                    return false;
                }
                repositoryUrl = _configuration.GetVelopackRepositoryUrl();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Velopack configuration could not be read: {ex.Message}");
                SetState(UpdateState.Failed);
                return false;
            }

            if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var repositoryUri)
                || repositoryUri.Scheme != Uri.UriSchemeHttps
                || !IsGitHubRepository(repositoryUri))
            {
                SetState(UpdateState.Failed);
                return false;
            }

            try
            {
                if (!forceCheck && ShouldSkipCheck())
                {
                    SetState(UpdateState.NotChecked);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Velopack check schedule could not be read: {ex.Message}");
                SetState(UpdateState.Failed);
                ClearCheckTime();
                return false;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = new GithubSource(
                    repositoryUri.AbsoluteUri.TrimEnd('/'),
                    accessToken: null,
                    prerelease: _configuration.AllowVelopackPrerelease());
                var manager = new UpdateManager(source);
                var release = await manager.CheckForUpdatesAsync().ConfigureAwait(true);
                SaveCheckTime();

                if (release is null)
                {
                    SetState(UpdateState.UpToDate);
                    return false;
                }

                SetState(UpdateState.UpdateAvailable);
                var version = release.TargetFullRelease.Version.ToString();
                var notes = release.TargetFullRelease.NotesMarkdown;
                var message = string.IsNullOrWhiteSpace(notes)
                    ? $"TOS Lite için yeni bir sürüm bulundu: {version}. Güncelleme indirilsin ve uygulama yeniden başlatılsın mı?"
                    : $"TOS Lite için yeni bir sürüm bulundu: {version}.\n\n{notes}\n\nGüncelleme indirilsin ve uygulama yeniden başlatılsın mı?";

                var answer = NeonMessageBoxWindow.Show(
                    message,
                    "TOS Lite güncellemesi",
                    showCancel: true,
                    primaryButtonText: "Evet",
                    secondaryButtonText: "Daha sonra");
                if (answer != true)
                {
                    SetState(UpdateState.Deferred);
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();
                SetState(UpdateState.Applying);
                await manager.DownloadUpdatesAsync(release).ConfigureAwait(true);
                manager.ApplyUpdatesAndRestart(release);
                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                Debug.WriteLine($"Velopack update check skipped: {ex.Message}");
                SetState(UpdateState.Failed);
                ClearCheckTime();
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Velopack update failed: {ex.Message}");
                SetState(UpdateState.Failed);
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

        private static void SetState(UpdateState state)
        {
            CurrentState = state;
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

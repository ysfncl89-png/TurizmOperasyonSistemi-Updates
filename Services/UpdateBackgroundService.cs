using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;

namespace TurizmOperasyonSistemi.Services
{
    /// <summary>
    /// Background service that periodically checks for updates using UpdateCheckerService,
    /// optionally downloads via UpdateDownloadService and triggers installer via UpdateInstallerService.
    /// It does not show UI; it publishes notifications via NotificationCenter.
    /// </summary>
    public class UpdateBackgroundService : IBackgroundService
    {
        private readonly UpdateCheckerService _checker;
        private readonly UpdateDownloadService _downloader;
        private readonly UpdateInstallerService _installer;
        private readonly ConfigurationProvider _config = ConfigurationProvider.Instance;
        private CancellationToken _token;
        private Task _runningTask;

        public string Name => "UpdateService";

        public UpdateBackgroundService(UpdateCheckerService checker = null, UpdateDownloadService downloader = null, UpdateInstallerService installer = null)
        {
            var http = new HttpClient();
            _checker = checker ?? new UpdateCheckerService(http);
            _downloader = downloader ?? new UpdateDownloadService(http);
            _installer = installer ?? new UpdateInstallerService();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _token = cancellationToken;
            _runningTask = Task.Run(async () => await RunLoopAsync(_token).ConfigureAwait(false), _token);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_runningTask != null)
                {
                    await Task.WhenAny(_runningTask, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateBackgroundService Stop failed: {ex.Message}");
            }
        }

        private async Task RunLoopAsync(CancellationToken token)
        {
            try
            {
                // Immediate check on start
                await CheckOnceAsync(token).ConfigureAwait(false);

                // Periodic checks
                var intervalHours = Math.Max(1, _config.GetUpdateCheckIntervalHours());
                var delay = TimeSpan.FromHours(intervalHours);

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(delay, token).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException) { break; }

                    await CheckOnceAsync(token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateBackgroundService RunLoop fatal: {ex.Message}");
            }
        }

        private async Task CheckOnceAsync(CancellationToken token)
        {
            if (!_config.IsUpdateEnabled()) return;

            UpdateInfo info = null;
            try
            {
                info = await _checker.CheckForUpdateAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateBackgroundService: checker failed: {ex.Message}");
                return;
            }

            if (info == null) return;

            // channel filter
            var configuredChannel = _config.GetUpdateChannel();
            if (!string.IsNullOrEmpty(info.Channel) && !string.Equals(info.Channel, configuredChannel, StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine($"UpdateBackgroundService: manifest channel '{info.Channel}' does not match configured '{configuredChannel}'");
                return;
            }

            // compare versions and database requirements
            var currentAppVersion = MigrationService.GetApplicationVersion();
            var currentDbVersion = GetCurrentDatabaseVersion();

            var appUpdateAvailable = IsNewerVersion(info.Version, currentAppVersion);
            var migrationRequired = info.MinimumDatabaseVersion.HasValue && currentDbVersion < info.MinimumDatabaseVersion.Value;

            if (!appUpdateAvailable && !migrationRequired)
            {
                Debug.WriteLine($"UpdateBackgroundService: no update required. currentApp={currentAppVersion}, latestApp={info.Version}, currentDb={currentDbVersion}, minDb={info.MinimumDatabaseVersion?.ToString() ?? "N/A"}");
                return;
            }

            var notificationKey = appUpdateAvailable && migrationRequired
                ? "update.both.required"
                : appUpdateAvailable
                    ? "update.available"
                    : "update.migration.required";

            var payload = new
            {
                Info = info,
                CurrentAppVersion = currentAppVersion,
                CurrentDatabaseVersion = currentDbVersion,
                RequiredDatabaseVersion = info.MinimumDatabaseVersion,
                ReleaseType = info.ReleaseType,
                AppUpdateAvailable = appUpdateAvailable,
                MigrationRequired = migrationRequired
            };

            NotificationCenter.Publish(notificationKey, payload, persistToDb: true);

            // AutoDownload
            if (_config.IsAutoDownloadEnabled())
            {
                DownloadResult dl = null;
                try
                {
                    dl = await _downloader.DownloadAsync(info, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"UpdateBackgroundService: download failed: {ex.Message}");
                }

                if (dl != null && dl.Success)
                {
                    NotificationCenter.Publish("update.downloaded", new { Info = info, Path = dl.FilePath, NotificationKey = notificationKey }, persistToDb: true);

                    // AutoInstall
                    if (_config.IsAutoInstallEnabled())
                    {
                        try
                        {
                            var started = await _installer.LaunchInstallerAsync(dl.FilePath).ConfigureAwait(false);
                            if (started)
                            {
                                // Ask main thread to shutdown so installer can replace files
                                Application.Current?.Dispatcher.Invoke(() =>
                                {
                                    try { Application.Current.Shutdown(); } catch { }
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"UpdateBackgroundService: installer start failed: {ex.Message}");
                        }
                    }
                }
                else
                {
                    NotificationCenter.Publish("update.download_failed", new { Info = info, Error = dl?.ErrorMessage, NotificationKey = notificationKey }, persistToDb: true);
                }
            }
        }

        private long GetCurrentDatabaseVersion()
        {
            try
            {
                var manifest = new ManifestService().LoadManifest();
                return manifest?.DbVersion ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        private bool IsNewerVersion(string candidate, string current)
        {
            try
            {
                if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(current)) return false;
                var c1 = candidate.TrimStart('v', 'V');
                var c2 = current.TrimStart('v', 'V');
                if (Version.TryParse(c1, out var v1) && Version.TryParse(c2, out var v2))
                {
                    return v1 > v2;
                }
                return !string.Equals(candidate, current, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
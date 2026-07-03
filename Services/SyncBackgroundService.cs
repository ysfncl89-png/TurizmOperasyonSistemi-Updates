using System;
using System.Threading;
using System.Threading.Tasks;

namespace TurizmOperasyonSistemi.Services
{
    /// <summary>
    /// Background wrapper around SyncService to run initial sync and optionally periodic sync.
    /// It does not show UI; it logs exceptions to Debug output.
    /// </summary>
    public class SyncBackgroundService : IBackgroundService
    {
        private readonly SyncService _syncService;
        private Task _runningTask;
        private CancellationToken _token;

        public string Name => "SyncService";

        public SyncBackgroundService(SyncService syncService)
        {
            _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _token = cancellationToken;
            // Start initial sync in background
            _runningTask = Task.Run(async () =>
            {
                try
                {
                    await _syncService.StartInitialSyncAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SyncBackgroundService Start failed: {ex.Message}");
                }

                // Optionally, keep running periodic sync (not enabled by default)
                // For now, just exit after initial sync
            }, _token);

            return Task.CompletedTask; // return quickly per contract
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Wait for running task to finish or cancellation
                if (_runningTask != null)
                {
                    await Task.WhenAny(_runningTask, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SyncBackgroundService Stop failed: {ex.Message}");
            }
        }
    }
}

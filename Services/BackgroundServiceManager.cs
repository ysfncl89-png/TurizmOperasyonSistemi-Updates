using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TurizmOperasyonSistemi.Services
{
    /// <summary>
    /// Lightweight extensible background service manager.
    /// Register IBackgroundService implementations and call StartAllAsync/StopAllAsync to control them.
    /// The manager isolates failures of individual services and logs exceptions via Debug output.
    /// </summary>
    public class BackgroundServiceManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, IBackgroundService> _services = new();
        private readonly List<Task> _runningTasks = new();
        private CancellationTokenSource _cts = new();

        public BackgroundServiceManager()
        {
        }

        /// <summary>
        /// Register a background service. If a service with the same name exists, it will not be added.
        /// </summary>
        public bool RegisterService(IBackgroundService service)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            return _services.TryAdd(service.Name, service);
        }

        /// <summary>
        /// Unregister a service by name. Returns true if removed.
        /// </summary>
        public bool UnregisterService(string serviceName)
        {
            if (string.IsNullOrEmpty(serviceName)) return false;
            return _services.TryRemove(serviceName, out _);
        }

        /// <summary>
        /// Start all registered services. Each service StartAsync is executed and any exceptions are caught.
        /// This method returns once all services have been asked to start; services may keep running in background.
        /// </summary>
        public async Task StartAllAsync()
        {
            var token = _cts.Token;
            foreach (var svc in _services.Values)
            {
                var service = svc;
                var task = Task.Run(async () =>
                {
                    try
                    {
                        await service.StartAsync(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"BackgroundService '{service.Name}' Start failed: {ex.Message}");
                    }
                }, token);

                _runningTasks.Add(task);
            }

            // Small delay to allow tasks to kick off
            await Task.Delay(10).ConfigureAwait(false);
        }

        /// <summary>
        /// Stop all running services and wait for their completion or until cancellationToken expires.
        /// </summary>
        public async Task StopAllAsync(TimeSpan? timeout = null)
        {
            try
            {
                // signal cancellation
                _cts.Cancel();

                var token = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30)).Token;

                // call StopAsync on each service to allow graceful shutdown
                var stopTasks = _services.Values.Select(s => SafeStopAsync(s, token)).ToArray();
                await Task.WhenAll(stopTasks).ConfigureAwait(false);

                // wait for running tasks to finish
                Task all = Task.WhenAll(_runningTasks);
                await Task.WhenAny(all, Task.Delay(timeout ?? TimeSpan.FromSeconds(30))).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BackgroundServiceManager StopAll failed: {ex.Message}");
            }
            finally
            {
                // reset for potential reuse
                _cts.Dispose();
                _cts = new CancellationTokenSource();
                _runningTasks.Clear();
            }
        }

        private async Task SafeStopAsync(IBackgroundService service, CancellationToken cancellationToken)
        {
            try
            {
                await service.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BackgroundService '{service.Name}' Stop failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns the list of registered service names.
        /// </summary>
        public IEnumerable<string> GetRegisteredServices() => _services.Keys.OrderBy(k => k);

        public void Dispose()
        {
            try
            {
                _cts.Cancel();
            }
            catch { }
            _cts.Dispose();
        }
    }
}

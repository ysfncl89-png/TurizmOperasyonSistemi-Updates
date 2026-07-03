using System.Threading;
using System.Threading.Tasks;

namespace TurizmOperasyonSistemi.Services
{
    /// <summary>
    /// Represents a background service managed by BackgroundServiceManager.
    /// Implementations should perform their long-running work in StartAsync and stop work in StopAsync.
    /// </summary>
    public interface IBackgroundService
    {
        /// <summary>
        /// A short name identifying the service (for logging/status).
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Start the background service. Should return quickly after starting internal tasks.
        /// </summary>
        Task StartAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Request the service to stop and wait for cleanup to complete.
        /// </summary>
        Task StopAsync(CancellationToken cancellationToken);
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace TurizmOperasyonSistemi.Services
{
    /// <summary>
    /// Responsible for downloading update assets and verifying SHA-256 checksum.
    /// Returns a DownloadResult describing success and saved file path.
    /// </summary>
    public class UpdateDownloadService
    {
        private readonly HttpClient _http;
        private readonly ConfigurationProvider _config = ConfigurationProvider.Instance;

        public UpdateDownloadService(HttpClient httpClient = null)
        {
            _http = httpClient ?? new HttpClient();
            _http.Timeout = TimeSpan.FromSeconds(60);
        }

        public async Task<DownloadResult> DownloadAsync(UpdateInfo info, CancellationToken cancellationToken = default)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));

            var result = new DownloadResult { Success = false };

            try
            {
                var retries = Math.Max(1, _config.GetUpdateRetryCount());
                var tempDir = Path.Combine(Path.GetTempPath(), "TurizmUpdater");
                Directory.CreateDirectory(tempDir);

                var uri = new Uri(info.DownloadUrl);
                var fileName = Path.GetFileName(uri.LocalPath);
                if (string.IsNullOrEmpty(fileName)) fileName = "update_asset" + Guid.NewGuid().ToString("N");

                var attempt = 0;
                Exception lastEx = null;

                while (attempt < retries && !cancellationToken.IsCancellationRequested)
                {
                    attempt++;
                    var targetPath = Path.Combine(tempDir, $"{DateTime.UtcNow:yyyyMMddHHmmss}_{attempt}_{fileName}");

                    try
                    {
                        using (var resp = await _http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                        {
                            if (!resp.IsSuccessStatusCode)
                            {
                                lastEx = new Exception($"Download failed with status {resp.StatusCode}");
                                Debug.WriteLine($"UpdateDownloadService: {lastEx.Message}");
                                try { await Task.Delay(500 * attempt, cancellationToken).ConfigureAwait(false); } catch { }
                                continue;
                            }

                            using (var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            using (var fs = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            {
                                await stream.CopyToAsync(fs, 81920, cancellationToken).ConfigureAwait(false);
                            }
                        }

                        // verify checksum if provided
                        if (!string.IsNullOrEmpty(info.Checksum))
                        {
                            var computed = await ComputeSha256Async(targetPath).ConfigureAwait(false);
                            if (!string.Equals(computed, info.Checksum, StringComparison.OrdinalIgnoreCase))
                            {
                                lastEx = new Exception("Checksum mismatch");
                                Debug.WriteLine($"UpdateDownloadService: checksum mismatch (expected {info.Checksum}, got {computed})");
                                try { File.Delete(targetPath); } catch { }
                                try { await Task.Delay(500 * attempt, cancellationToken).ConfigureAwait(false); } catch { }
                                continue; // retry
                            }
                        }

                        // success
                        result.Success = true;
                        result.FilePath = targetPath;
                        return result;
                    }
                    catch (OperationCanceledException)
                    {
                        lastEx = new OperationCanceledException("Download canceled");
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        Debug.WriteLine($"UpdateDownloadService attempt {attempt} failed: {ex.Message}");
                        try { await Task.Delay(500 * attempt, cancellationToken).ConfigureAwait(false); } catch { }
                    }
                }

                result.ErrorMessage = lastEx?.Message ?? "Unknown error";
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateDownloadService fatal: {ex.Message}");
                return new DownloadResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        private static Task<string> ComputeSha256Async(string filePath)
        {
            return Task.Run(() =>
            {
                using (var fs = File.OpenRead(filePath))
                using (var sha = SHA256.Create())
                {
                    var hash = sha.ComputeHash(fs);
                    return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
                }
            });
        }
    }
}

using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;

namespace TurizmOperasyonSistemi.Services
{
    /// <summary>
    /// Responsible only for fetching and parsing update manifest and returning UpdateInfo.
    /// Does not perform downloads, installations or UI interactions.
    /// </summary>
    public class UpdateCheckerService
    {
        private readonly ConfigurationProvider _config = ConfigurationProvider.Instance;
        private readonly HttpClient _http;

        public UpdateCheckerService(HttpClient httpClient = null)
        {
            _http = httpClient ?? new HttpClient();
            _http.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<UpdateInfo> CheckForUpdateAsync()
        {
            try
            {
                if (!_config.IsUpdateEnabled())
                    return null;

                var manifestUrl = _config.GetUpdateManifestUrl();
                if (string.IsNullOrWhiteSpace(manifestUrl))
                    return null;

                HttpResponseMessage resp;
                try
                {
                    resp = await _http.GetAsync(manifestUrl).ConfigureAwait(false);
                }
                catch (HttpRequestException hre)
                {
                    Debug.WriteLine($"UpdateCheckerService: manifest request failed: {hre.Message}");
                    return null;
                }
                catch (TaskCanceledException tce)
                {
                    Debug.WriteLine($"UpdateCheckerService: manifest request timeout: {tce.Message}");
                    return null;
                }

                if (!resp.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"UpdateCheckerService: manifest returned status {resp.StatusCode}");
                    return null;
                }

                var content = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(content))
                {
                    Debug.WriteLine("UpdateCheckerService: manifest empty");
                    return null;
                }

                JsonDocument doc;
                try
                {
                    doc = JsonDocument.Parse(content);
                }
                catch (JsonException je)
                {
                    Debug.WriteLine($"UpdateCheckerService: invalid manifest json: {je.Message}");
                    return null;
                }

                var root = doc.RootElement;
                var info = new UpdateInfo();

                if (root.TryGetProperty("version", out var v)) info.Version = v.GetString();
                if (root.TryGetProperty("channel", out var ch)) info.Channel = ch.GetString();
                if (root.TryGetProperty("mandatory", out var m) && m.ValueKind == JsonValueKind.True) info.Mandatory = true;
                if (root.TryGetProperty("minimumDatabaseVersion", out var mdv) && mdv.ValueKind == JsonValueKind.Number)
                {
                    if (mdv.TryGetInt32(out var mi)) info.MinimumDatabaseVersion = mi;
                }
                if (root.TryGetProperty("minimumClientVersion", out var mcv)) info.MinimumClientVersion = mcv.GetString();
                if (root.TryGetProperty("releaseDate", out var rd) && rd.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(rd.GetString(), out var dt)) info.ReleaseDate = dt;
                }
                if (root.TryGetProperty("releaseNotes", out var rn)) info.ReleaseNotes = rn.GetString();
                if (root.TryGetProperty("downloadUrl", out var du)) info.DownloadUrl = du.GetString();
                else if (root.TryGetProperty("url", out var u2)) info.DownloadUrl = u2.GetString();
                if (root.TryGetProperty("installerType", out var it)) info.InstallerType = it.GetString();
                if (root.TryGetProperty("checksum", out var cs)) info.Checksum = cs.GetString();

                // basic validation
                if (string.IsNullOrEmpty(info.Version) || string.IsNullOrEmpty(info.DownloadUrl))
                {
                    Debug.WriteLine("UpdateCheckerService: manifest missing required fields (version/downloadUrl)");
                    return null;
                }

                return info;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateCheckerService fatal: {ex.Message}");
                return null;
            }
        }
    }
}

using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
// Standalone smoke tester without referencing main project
using System.Net;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

record TestUpdateInfo(string version, string downloadUrl, string checksum, string installerType, string releaseNotes);

class Program
{
    static async Task<int> Main()
    {
        int port = 5005;
        var prefix = $"http://localhost:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();
        Console.WriteLine($"SmokeTester listening on {prefix}");

        var serverTask = Task.Run(() => HandleRequests(listener, port));

        // Give server a moment
        await Task.Delay(200);

        // Scenario 1: valid manifest and asset
        var manifestUrl = $"http://localhost:{port}/manifest_ok";
        Console.WriteLine("\nScenario 1: valid manifest and asset (expected: success)");
        var info = await CheckForUpdateAsync(manifestUrl);
        Console.WriteLine($"Checker returned: {(info != null ? info.version : "null")}");

        if (info != null)
        {
            var dl = await DownloadAsync(info.downloadUrl, info.checksum);
            Console.WriteLine($"Download result: success={dl.success} path={dl.filePath} error={dl.error}");
        }

        // Scenario 2: 404 manifest
        manifestUrl = $"http://localhost:{port}/manifest_404";
        Console.WriteLine("\nScenario 2: 404 manifest (expected: no update)");
        var info2 = await CheckForUpdateAsync(manifestUrl);
        Console.WriteLine($"Checker returned: {(info2 != null ? info2.version : "null")}");

        // Scenario 3: invalid JSON
        manifestUrl = $"http://localhost:{port}/manifest_invalid";
        Console.WriteLine("\nScenario 3: invalid JSON manifest (expected: no update)");
        var info3 = await CheckForUpdateAsync(manifestUrl);
        Console.WriteLine($"Checker returned: {(info3 != null ? info3.version : "null")}");

        // Scenario 4: download 500
        manifestUrl = $"http://localhost:{port}/manifest_download500";
        Console.WriteLine("\nScenario 4: download 500 (expected: download fail)");
        var info4 = await CheckForUpdateAsync(manifestUrl);
        Console.WriteLine($"Checker returned: {(info4 != null ? info4.version : "null")}");
        if (info4 != null)
        {
            var dl4 = await DownloadAsync(info4.downloadUrl, info4.checksum);
            Console.WriteLine($"Download result: success={dl4.success} error={dl4.error}");
        }

        // Scenario 5: checksum mismatch
        manifestUrl = $"http://localhost:{port}/manifest_badchecksum";
        Console.WriteLine("\nScenario 5: checksum mismatch (expected: download fail)");
        var info5 = await CheckForUpdateAsync(manifestUrl);
        Console.WriteLine($"Checker returned: {(info5 != null ? info5.version : "null")}");
        if (info5 != null)
        {
            var dl5 = await DownloadAsync(info5.downloadUrl, info5.checksum);
            Console.WriteLine($"Download result: success={dl5.success} error={dl5.error}");
        }

        Console.WriteLine("\nSmoke tests finished. Press Enter to stop server.");
        Console.ReadLine();
        listener.Stop();
        return 0;
    }

    static async Task HandleRequests(HttpListener listener, int port)
    {
        // Pre-build deterministic assets map to avoid nested GetContextAsync and race conditions
        var assets = new System.Collections.Concurrent.ConcurrentDictionary<string, byte[]>();

        // Prepare asset for manifest_ok
        var assetOk = Encoding.UTF8.GetBytes("hello-asset-ok");
        using (var sha = SHA256.Create())
        {
            var hash = sha.ComputeHash(assetOk);
            var hex = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            var manifestJson = JsonSerializer.Serialize(new { version = "2.0.0", downloadUrl = $"http://localhost:{port}/asset_ok.bin", checksum = hex, installerType = "bin", releaseNotes = "OK release" });
            // store manifest response for quick lookup by path
            assets["/asset_ok.bin"] = assetOk;
            assets["/manifest_ok.json"] = Encoding.UTF8.GetBytes(manifestJson);
        }

        // manifest_download500 -> asset returns 500
        var manifestDownload500Json = JsonSerializer.Serialize(new { version = "2.0.1", downloadUrl = $"http://localhost:{port}/asset_500.bin", checksum = "" });
        assets["/manifest_download500.json"] = Encoding.UTF8.GetBytes(manifestDownload500Json);

        // manifest_badchecksum -> asset content different than checksum
        var manifestBadJson = JsonSerializer.Serialize(new { version = "2.0.2", downloadUrl = $"http://localhost:{port}/asset_bad.bin", checksum = "deadbeefdeadbeef" });
        assets["/manifest_badchecksum.json"] = Encoding.UTF8.GetBytes(manifestBadJson);
        assets["/asset_bad.bin"] = Encoding.UTF8.GetBytes("real-content-not-matching-checksum");

        // manifest_invalid content
        assets["/manifest_invalid.json"] = Encoding.UTF8.GetBytes("{ not json");

        while (listener.IsListening)
        {
            try
            {
                var ctx = await listener.GetContextAsync();
                var req = ctx.Request;
                var resp = ctx.Response;
                var path = req.Url.AbsolutePath;

                // map legacy endpoints without .json to our prepared keys
                if (path == "/manifest_ok") path = "/manifest_ok.json";
                if (path == "/manifest_download500") path = "/manifest_download500.json";
                if (path == "/manifest_badchecksum") path = "/manifest_badchecksum.json";
                if (path == "/manifest_invalid") path = "/manifest_invalid.json";

                if (path == "/asset_500.bin")
                {
                    resp.StatusCode = 500;
                    resp.Close();
                    continue;
                }

                if (assets.TryGetValue(path, out var bytes))
                {
                    if (path.EndsWith(".json"))
                    {
                        resp.ContentType = "application/json";
                        await resp.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                    }
                    else
                    {
                        resp.ContentType = "application/octet-stream";
                        await resp.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                    }
                    resp.Close();
                }
                else
                {
                    resp.StatusCode = 404;
                    resp.Close();
                }
            }
            catch (HttpListenerException) { break; }
            catch (Exception ex)
            {
                Console.WriteLine($"Server error: {ex.Message}");
            }
        }
    }

    static async Task<TestUpdateInfo> CheckForUpdateAsync(string manifestUrl)
    {
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(10);
            var resp = await http.GetAsync(manifestUrl).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var content = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var version = root.GetProperty("version").GetString();
            var downloadUrl = root.GetProperty("downloadUrl").GetString();
            var checksum = root.TryGetProperty("checksum", out var cs) ? cs.GetString() : null;
            var installerType = root.TryGetProperty("installerType", out var it) ? it.GetString() : null;
            var releaseNotes = root.TryGetProperty("releaseNotes", out var rn) ? rn.GetString() : null;
            return new TestUpdateInfo(version, downloadUrl, checksum, installerType, releaseNotes);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Checker error: {ex.Message}");
            return null;
        }
    }

    static async Task<(bool success, string filePath, string error)> DownloadAsync(string url, string expectedChecksum)
    {
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return (false, null, $"HTTP {resp.StatusCode}");
            var temp = Path.Combine(Path.GetTempPath(), "smoketest_" + Guid.NewGuid().ToString("N"));
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write))
            {
                await resp.Content.CopyToAsync(fs).ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(expectedChecksum))
            {
                using var sha = SHA256.Create();
                using var fsr = File.OpenRead(temp);
                var hash = sha.ComputeHash(fsr);
                var hex = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
                if (!string.Equals(hex, expectedChecksum, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(temp); } catch { }
                    return (false, null, "checksum_mismatch");
                }
            }

            return (true, temp, null);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }
}

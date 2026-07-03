using System;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Threading.Tasks;

class Program
{
    static async Task<int> Main()
    {
        var updaterExe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Updater", "bin", "Debug", "net8.0", "Updater.exe"));
        // Build path fallback: try published path
        if (!File.Exists(updaterExe))
        {
            // try tools/Updater/Updater.exe in project dir
            updaterExe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "tools", "Updater", "Updater.exe"));
        }

        Console.WriteLine($"Using Updater at: {updaterExe}");

        var tmpRoot = Path.Combine(Path.GetTempPath(), "IntegrationTestApp");
        if (Directory.Exists(tmpRoot)) Directory.Delete(tmpRoot, true);
        Directory.CreateDirectory(tmpRoot);

        // Create dummy app file
        var appFile = Path.Combine(tmpRoot, "appfile.txt");
        File.WriteAllText(appFile, "v1");

        Console.WriteLine("Initial appfile content: " + File.ReadAllText(appFile));

        // 1) v1 -> v2 upgrade
        Console.WriteLine("\n=== Test 1: v1 -> v2 upgrade ===");
        var zipV2 = Path.Combine(Path.GetTempPath(), "update_v2.zip");
        CreateZipWithFile(zipV2, "appfile.txt", "v2");

        var code = RunUpdater(updaterExe, zipV2, tmpRoot);
        Console.WriteLine($"Updater exit code: {code}");
        // Wait a moment
        await Task.Delay(500);
        var content = File.ReadAllText(appFile);
        Console.WriteLine("After update, appfile content: " + content);

        // 2) rollback (restore from backup)
        Console.WriteLine("\n=== Test 2: Rollback using backup ===");
        var backupsDir = Path.Combine(tmpRoot, "backups");
        if (Directory.Exists(backupsDir))
        {
            var latest = Directory.GetDirectories(backupsDir, "update_*", SearchOption.TopDirectoryOnly);
            if (latest.Length > 0)
            {
                Array.Sort(latest);
                var last = latest[^1];
                var backupFile = Path.Combine(last, "appfile.txt");
                if (File.Exists(backupFile))
                {
                    File.Copy(backupFile, appFile, true);
                    Console.WriteLine("Rollback applied. appfile content: " + File.ReadAllText(appFile));
                }
                else
                {
                    Console.WriteLine("No backup file found to rollback.");
                }
            }
            else Console.WriteLine("No backups directory found.");
        }
        else Console.WriteLine("Backups dir not found.");

        // 3) internet kesilmesi: simulate by pointing to non-existent manifest (handled by SmokeTester earlier)
        Console.WriteLine("\n=== Test 3: Internet cut (simulated by 404) ===");
        // We will perform a simple HTTP request to nonexistent local endpoint
        var webFail = await TryHttpGetAsync("http://localhost:5005/nonexistent_manifest");
        Console.WriteLine("HTTP GET result: " + (webFail ? "reachable" : "unreachable/failed"));

        // 4) checksum fail: create package and simulate bad checksum by using different content and expected checksum in manifest
        Console.WriteLine("\n=== Test 4: checksum fail simulation ===");
        // Use SmokeTester endpoints instead for deterministic behavior (assumes smoke tester is running)
        Console.WriteLine("Please run SmokeTester separately to fully verify checksum failure scenario.");

        // 5) updater crash simulation
        Console.WriteLine("\n=== Test 5: Updater crash simulation ===");
        var zipCrash = Path.Combine(Path.GetTempPath(), "update_crash.zip");
        CreateZipWithFile(zipCrash, "SIMULATE_CRASH", "1");
        // Also include a new appfile to ensure behavior
        CreateZipWithFileAppend(zipCrash, "appfile.txt", "v3");

        var code2 = RunUpdater(updaterExe, zipCrash, tmpRoot);
        Console.WriteLine($"Updater exit code (simulated crash): {code2}");
        Console.WriteLine("appfile content after simulated crash: " + File.ReadAllText(appFile));

        // Check backups and restore if needed
        if (Directory.Exists(backupsDir))
        {
            var latest = Directory.GetDirectories(backupsDir, "update_*", SearchOption.TopDirectoryOnly);
            if (latest.Length > 0)
            {
                Array.Sort(latest);
                var last = latest[^1];
                var backupFile = Path.Combine(last, "appfile.txt");
                if (File.Exists(backupFile))
                {
                    // Restore
                    File.Copy(backupFile, appFile, true);
                    Console.WriteLine("After restoring from backup, appfile content: " + File.ReadAllText(appFile));
                }
            }
        }

        Console.WriteLine("\nIntegration tests completed.");
        return 0;
    }

    static int RunUpdater(string updaterExe, string packagePath, string appRoot)
    {
        try
        {
            if (!File.Exists(updaterExe))
            {
                Console.WriteLine("Updater executable not found: " + updaterExe);
                // As fallback, run the tools/Updater project via dotnet run
                var projectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "tools", "Updater", "Updater.csproj"));
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"run --project \"{projectPath}\" -- \"{packagePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                if (p != null)
                {
                    p.WaitForExit(30000);
                    return p.ExitCode;
                }
                return -1;
            }

            var psi = new ProcessStartInfo
            {
                FileName = updaterExe,
                Arguments = $"\"{packagePath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = appRoot
            };
            var proc = Process.Start(psi);
            proc.WaitForExit(30000);
            var outp = proc.StandardOutput.ReadToEnd();
            var err = proc.StandardError.ReadToEnd();
            Console.WriteLine("Updater stdout:\n" + outp);
            if (!string.IsNullOrEmpty(err)) Console.WriteLine("Updater stderr:\n" + err);
            return proc.ExitCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine("RunUpdater failed: " + ex.Message);
            return -1;
        }
    }

    static void CreateZipWithFile(string zipPath, string name, string content)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using (var z = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = z.CreateEntry(name);
            using var s = e.Open();
            using var sw = new StreamWriter(s);
            sw.Write(content);
        }
    }

    static void CreateZipWithFileAppend(string zipPath, string name, string content)
    {
        using (var z = ZipFile.Open(zipPath, ZipArchiveMode.Update))
        {
            var e = z.CreateEntry(name);
            using var s = e.Open();
            using var sw = new StreamWriter(s);
            sw.Write(content);
        }
    }

    static async Task<bool> TryHttpGetAsync(string url)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.Timeout = TimeSpan.FromSeconds(5);
            var r = await http.GetAsync(url).ConfigureAwait(false);
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}

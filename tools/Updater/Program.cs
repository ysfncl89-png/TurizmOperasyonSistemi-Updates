using System;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

class Program
{
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: Updater.exe <path-to-package.zip|exe>");
                return 1;
            }

            var package = args[0];

            if (!File.Exists(package))
            {
                Console.WriteLine($"Package not found: {package}");
                return 1;
            }

            var exeDir = AppContext.BaseDirectory; // path to tools/Updater
            var appRoot = Path.GetFullPath(Path.Combine(exeDir, "..", ".."));

            Console.WriteLine($"Updater running. AppRoot: {appRoot}");

            var ext = Path.GetExtension(package).ToLowerInvariant();

            if (ext == ".exe" || ext == ".msi")
            {
                // If it's a direct installer, run it
                Console.WriteLine("Starting installer...");
                Process.Start(new ProcessStartInfo { FileName = package, UseShellExecute = true });
                return 0;
            }

            if (ext == ".zip")
            {
                var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                var backupDir = Path.Combine(appRoot, "backups", "update_" + timestamp);
                Directory.CreateDirectory(backupDir);

                Console.WriteLine("Creating backup of current files...");
                // copy existing files to backup
                foreach (var file in Directory.GetFiles(appRoot, "*", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(file);
                    if (string.Equals(name, "Updater.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Equals("turizm.db", StringComparison.OrdinalIgnoreCase)) continue; // do not touch DB
                    var dest = Path.Combine(backupDir, name);
                    File.Copy(file, dest, overwrite: true);
                }

                // extract archive to temp
                var tempDir = Path.Combine(Path.GetTempPath(), "TurizmUpdater", timestamp);
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);

                Console.WriteLine($"Extracting package to {tempDir}...");
                ZipFile.ExtractToDirectory(package, tempDir);
                // If the package contains a file named SIMULATE_CRASH, simulate a crash after backup and before copying.
                var simulateCrashPath = Path.Combine(tempDir, "SIMULATE_CRASH");
                if (File.Exists(simulateCrashPath))
                {
                    Console.WriteLine("SIMULATE_CRASH detected in package. Simulating updater crash.");
                    throw new Exception("Simulated updater crash");
                }

                Console.WriteLine("Copying files to application directory...");
                foreach (var src in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(tempDir, src);
                    var dest = Path.Combine(appRoot, rel);
                    var destDir = Path.GetDirectoryName(dest);
                    if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

                    // Do not overwrite DB
                    if (string.Equals(Path.GetFileName(dest), "turizm.db", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("Skipping database file replacement");
                        continue;
                    }

                    File.Copy(src, dest, overwrite: true);
                }

                Console.WriteLine("Cleanup temp files...");
                try { Directory.Delete(tempDir, true); } catch { }

                // Start main application
                var mainExe = Path.Combine(appRoot, "TurizmOperasyonSistemi.exe");
                if (File.Exists(mainExe))
                {
                    Console.WriteLine("Restarting main application...");
                    Process.Start(new ProcessStartInfo { FileName = mainExe, UseShellExecute = true });
                }
                else
                {
                    Console.WriteLine("Main executable not found after update. Please start application manually.");
                }

                return 0;
            }

            Console.WriteLine("Unknown package type. No action taken.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Updater error: {ex}");
            return 2;
        }
    }
}

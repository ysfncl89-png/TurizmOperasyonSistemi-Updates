using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TurizmOperasyonSistemi.Data;

namespace TurizmOperasyonSistemi.Services
{
    /// <summary>
    /// Migration işlemlerinin detaylı kayıt altına alınması ve loglama
    /// </summary>
    public class MigrationLogger
    {
        private readonly AppDbContext _context;
        private readonly string _logDirectory;
        private readonly string _currentLogFile;
        private const int MAX_LOG_FILE_SIZE_MB = 5;
        private const int MAX_LOG_FILES = 10;

        public MigrationLogger(AppDbContext context, string dbPath)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logDirectory = Path.Combine(Path.GetDirectoryName(dbPath), "logs");
            _currentLogFile = Path.Combine(_logDirectory, $"migration_{DateTime.Now:yyyy-MM-dd}.log");

            // Log dizini yoksa oluştur
            if (!Directory.Exists(_logDirectory))
            {
                try
                {
                    Directory.CreateDirectory(_logDirectory);
                    System.Diagnostics.Debug.WriteLine($"Log dizini oluşturuldu: {_logDirectory}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Log dizini oluşturma hatası: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Migration işlemi başladığını kaydeder
        /// </summary>
        public void LogMigrationStarted(string applicationVersion, string databaseVersion)
        {
            var message = $"=== MIGRATION BAŞLADI ===" +
                $"\nZaman: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}" +
                $"\nUygulama Versiyonu: {applicationVersion}" +
                $"\nDatabase Versiyonu: {databaseVersion ?? "None"}" +
                $"\nBilgisayar: {Environment.MachineName}" +
                $"\nProcess ID: {System.Diagnostics.Process.GetCurrentProcess().Id}" +
                $"\n";

            WriteLog(message);
        }

        /// <summary>
        /// Migration işlemi başarıyla tamamlandığını kaydeder
        /// </summary>
        public void LogMigrationCompleted(string applicationVersion, List<string> appliedMigrations, TimeSpan duration)
        {
            var message = $"=== MIGRATION BAŞARILI ===" +
                $"\nZaman: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}" +
                $"\nSüre: {duration.TotalSeconds:F2} saniye" +
                $"\nUygulama Versiyonu: {applicationVersion}" +
                $"\nUygulanan Migration'lar ({appliedMigrations.Count}):\n";

            foreach (var migration in appliedMigrations)
            {
                message += $"  ✓ {migration}\n";
            }

            message += $"\n";

            WriteLog(message);

            // Database'e de kaydet
            LogToDatabase(
                operationType: "MigrationCompleted",
                status: "Success",
                description: $"Migration başarıyla tamamlandı. {appliedMigrations.Count} migration uygulandı.",
                details: string.Join(", ", appliedMigrations)
            );
        }

        /// <summary>
        /// Migration işlemi başarısız olduğunu kaydeder
        /// </summary>
        public void LogMigrationFailed(string applicationVersion, Exception ex, TimeSpan duration)
        {
            var message = $"=== MIGRATION BAŞARISIZ ===" +
                $"\nZaman: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}" +
                $"\nSüre: {duration.TotalSeconds:F2} saniye" +
                $"\nUygulama Versiyonu: {applicationVersion}" +
                $"\nHata: {ex.Message}" +
                $"\nStack Trace:\n{ex.StackTrace}\n" +
                $"\n";

            WriteLog(message);

            // Database'e de kaydet
            LogToDatabase(
                operationType: "MigrationFailed",
                status: "Failed",
                description: $"Migration başarısız: {ex.Message}",
                details: ex.StackTrace
            );
        }

        /// <summary>
        /// Validation işlemlerini kaydeder
        /// </summary>
        public void LogValidation(string validationType, ValidationResult result)
        {
            var message = $"[{DateTime.Now:HH:mm:ss.fff}] Validation: {validationType}\n" +
                $"  Status: {(result.IsValid ? "PASS" : "FAIL")}\n" +
                $"  Message: {result.Message}\n";

            if (result.Exception != null)
            {
                message += $"  Exception: {result.Exception.Message}\n";
            }

            message += "\n";

            WriteLog(message);
        }

        /// <summary>
        /// Backup işlemlerini kaydeder
        /// </summary>
        public void LogBackup(string operation, BackupResult result)
        {
            var message = $"[{DateTime.Now:HH:mm:ss.fff}] Backup: {operation}\n" +
                $"  Status: {(result.IsSuccess ? "SUCCESS" : "FAILED")}\n" +
                $"  Message: {result.Message}\n" +
                $"  Size: {(result.BackupSize > 0 ? FormatBytes(result.BackupSize) : "N/A")}\n" +
                $"  Path: {result.BackupPath ?? "N/A"}\n";

            if (result.Exception != null)
            {
                message += $"  Exception: {result.Exception.Message}\n";
            }

            message += "\n";

            WriteLog(message);

            // Database'e de kaydet
            LogToDatabase(
                operationType: "Backup",
                status: result.IsSuccess ? "Success" : "Failed",
                description: $"{operation}: {result.Message}",
                details: result.BackupPath
            );
        }

        /// <summary>
        /// Lock işlemlerini kaydeder
        /// </summary>
        public void LogLock(string operation, LockResult result)
        {
            var message = $"[{DateTime.Now:HH:mm:ss.fff}] Lock: {operation}\n" +
                $"  Status: {(result.IsSuccess ? "SUCCESS" : "FAILED")}\n" +
                $"  Message: {result.Message}\n" +
                $"  LockId: {result.LockId ?? "N/A"}\n";

            if (result.Exception != null)
            {
                message += $"  Exception: {result.Exception.Message}\n";
            }

            message += "\n";

            WriteLog(message);
        }

        /// <summary>
        /// Genel işlem kaydı
        /// </summary>
        public void LogOperation(string operation, string message, bool isSuccess = true)
        {
            var logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {operation}: {message}\n";
            WriteLog(logMessage);
        }

        /// <summary>
        /// Hata kaydı
        /// </summary>
        public void LogError(string operation, Exception ex)
        {
            var message = $"[{DateTime.Now:HH:mm:ss.fff}] ERROR in {operation}:\n" +
                $"  Message: {ex.Message}\n" +
                $"  Stack Trace: {ex.StackTrace}\n\n";

            WriteLog(message);

            // Database'e de kaydet
            LogToDatabase(
                operationType: operation,
                status: "Error",
                description: ex.Message,
                details: ex.StackTrace
            );
        }

        /// <summary>
        /// Log dosyasına yazar
        /// </summary>
        private void WriteLog(string message)
        {
            try
            {
                // Console debug output
                System.Diagnostics.Debug.WriteLine(message);

                // File output
                lock (this) // Thread-safe dosya yazma
                {
                    // Log dosyasının boyutu kontrol et
                    CheckLogFileSize();

                    // Dosyaya yaz
                    File.AppendAllText(_currentLogFile, message, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Log yazma hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// Log dosyasının boyutu kontrol eder ve rotation yapar
        /// </summary>
        private void CheckLogFileSize()
        {
            try
            {
                if (!File.Exists(_currentLogFile))
                    return;

                var fileInfo = new FileInfo(_currentLogFile);
                long fileSizeInMB = fileInfo.Length / (1024 * 1024);

                if (fileSizeInMB > MAX_LOG_FILE_SIZE_MB)
                {
                    // Log dosyasını rotation yap
                    string rotatedFileName = Path.Combine(
                        _logDirectory,
                        $"migration_{DateTime.Now:yyyy-MM-dd_HHmmss}.log.bak"
                    );

                    try
                    {
                        if (File.Exists(_currentLogFile))
                        {
                            File.Move(_currentLogFile, rotatedFileName, overwrite: false);
                            System.Diagnostics.Debug.WriteLine($"Log dosyası rotation yapıldı: {rotatedFileName}");
                        }
                    }
                    catch
                    {
                        // Rotation başarısız olsa bile devam et
                    }

                    // Eski log dosyalarını temizle
                    CleanupOldLogFiles();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Log dosya boyutu kontrol hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// Eski log dosyalarını temizler
        /// </summary>
        private void CleanupOldLogFiles()
        {
            try
            {
                var logFiles = Directory.GetFiles(_logDirectory, "migration_*.log*")
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(f => f.CreationTime)
                    .ToList();

                if (logFiles.Count > MAX_LOG_FILES)
                {
                    var filesToDelete = logFiles.Skip(MAX_LOG_FILES).ToList();

                    foreach (var file in filesToDelete)
                    {
                        try
                        {
                            file.Delete();
                            System.Diagnostics.Debug.WriteLine($"Eski log dosyası silindi: {file.Name}");
                        }
                        catch
                        {
                            // Silme başarısız olsa bile devam et
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Log dosya temizleme hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// Database'e migration log kaydı ekler
        /// </summary>
        private void LogToDatabase(string operationType, string status, string description, string details)
        {
            try
            {
                if (!_context.GetTableExists("MigrationLogs"))
                    return;

                var logEntry = new Models.MigrationLog
                {
                    MigrationName = operationType,
                    AppliedAt = DateTime.Now,
                    Success = status == "Success",
                    // Ek bilgiler için düşünüyorum
                };

                _context.MigrationLogs.Add(logEntry);
                _context.SaveChanges();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Database log kaydı hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// Byte cinsinden boyutu insan tarafından okunabilir formata dönüştürür
        /// </summary>
        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        /// <summary>
        /// Tüm log dosyasını döndürür (son N satır)
        /// </summary>
        public string GetLogs(int lastLines = 100)
        {
            try
            {
                if (!File.Exists(_currentLogFile))
                    return "Log dosyası bulunamadı";

                var lines = File.ReadAllLines(_currentLogFile, Encoding.UTF8);
                var selectedLines = lines.TakeLast(lastLines).ToList();

                return string.Join("\n", selectedLines);
            }
            catch (Exception ex)
            {
                return $"Log okuma hatası: {ex.Message}";
            }
        }

        /// <summary>
        /// Log dizininin boyutunu döndürür
        /// </summary>
        public long GetLogDirectorySize()
        {
            try
            {
                if (!Directory.Exists(_logDirectory))
                    return 0;

                return Directory.GetFiles(_logDirectory, "migration_*")
                    .Select(p => new FileInfo(p).Length)
                    .Sum();
            }
            catch
            {
                return 0;
            }
        }
    }
}

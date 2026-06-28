using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using TurizmOperasyonSistemi.Data;

namespace TurizmOperasyonSistemi.Services
{
    /// <summary>
    /// Veritabanı ve sistem sağlığını kontrol eden servis
    /// </summary>
    public class HealthCheckService
    {
        private readonly AppDbContext _context;
        private readonly ConfigurationProvider _config;

        public HealthCheckService(AppDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _config = ConfigurationProvider.Instance;
        }

        /// <summary>
        /// Tüm health check'leri çalıştırır ve rapor döndürür
        /// </summary>
        public HealthCheckReport RunCompleteHealthCheck()
        {
            var report = new HealthCheckReport
            {
                CheckedAt = DateTime.Now,
                Checks = new List<HealthCheckItem>()
            };

            try
            {
                // 1. Database Connectivity
                report.Checks.Add(CheckDatabaseConnectivity());

                // 2. Schema Integrity
                report.Checks.Add(CheckSchemaIntegrity());

                // 3. Required Tables
                report.Checks.Add(CheckRequiredTables());

                // 4. Disk Space
                report.Checks.Add(CheckDiskSpace());

                // 5. Backup Directory
                report.Checks.Add(CheckBackupDirectory());

                // Extra: Backup directory usage & last backup info
                report.Checks.Add(CheckBackupUsage());

                // 6. Log Directory
                report.Checks.Add(CheckLogDirectory());

                // 7. Orphaned Data
                report.Checks.Add(CheckOrphanedData());

                // 8. Database File Size
                report.Checks.Add(CheckDatabaseFileSize());

                // Set overall status
                report.IsHealthy = report.Checks.All(c => c.Status == "OK");
                report.FailedChecks = report.Checks.Count(c => c.Status == "FAIL");
                report.WarningChecks = report.Checks.Count(c => c.Status == "WARN");
            }
            catch (Exception ex)
            {
                report.Checks.Add(new HealthCheckItem
                {
                    CheckName = "Overall Health Check",
                    Status = "ERROR",
                    Message = ex.Message,
                    Details = ex.StackTrace
                });
                report.IsHealthy = false;
            }

            return report;
        }

        /// <summary>
        /// Database bağlantısını kontrol eder
        /// </summary>
        private HealthCheckItem CheckDatabaseConnectivity()
        {
            try
            {
                using (var connection = _context.Database.GetDbConnection())
                {
                    if (connection.State != ConnectionState.Open)
                        connection.Open();
                    connection.Close();
                }

                return new HealthCheckItem
                {
                    CheckName = "Database Connectivity",
                    Status = "OK",
                    Message = "Database'e başarıyla bağlanıldı"
                };
            }
            catch (Exception ex)
            {
                return new HealthCheckItem
                {
                    CheckName = "Database Connectivity",
                    Status = "FAIL",
                    Message = $"Database bağlantısı başarısız: {ex.Message}",
                    Details = ex.StackTrace
                };
            }
        }

        /// <summary>
        /// Schema bütünlüğünü kontrol eder
        /// </summary>
        private HealthCheckItem CheckSchemaIntegrity()
        {
            try
            {
                using (var connection = _context.Database.GetDbConnection())
                {
                    if (connection.State != ConnectionState.Open)
                        connection.Open();

                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "PRAGMA integrity_check(1);";
                        var checkResult = cmd.ExecuteScalar()?.ToString() ?? "error";

                        if (checkResult == "ok")
                        {
                            return new HealthCheckItem
                            {
                                CheckName = "Schema Integrity",
                                Status = "OK",
                                Message = "Database şeması bütünlük kontrolü: OK"
                            };
                        }
                        else
                        {
                            return new HealthCheckItem
                            {
                                CheckName = "Schema Integrity",
                                Status = "FAIL",
                                Message = $"Schema bütünlüğü sorunu: {checkResult}",
                                Details = checkResult
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return new HealthCheckItem
                {
                    CheckName = "Schema Integrity",
                    Status = "WARN",
                    Message = $"Schema kontrol uyarısı: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Gerekli tabloların var olup olmadığını kontrol eder
        /// </summary>
        private HealthCheckItem CheckRequiredTables()
        {
            try
            {
                string[] requiredTables = { "Persons", "Registrations", "Payments", "Hotels", "Users" };
                var missingTables = new List<string>();

                using (var connection = _context.Database.GetDbConnection())
                {
                    if (connection.State != ConnectionState.Open)
                        connection.Open();

                    foreach (var table in requiredTables)
                    {
                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{table}';";
                            var result = cmd.ExecuteScalar();
                            if (result == null)
                                missingTables.Add(table);
                        }
                    }
                }

                if (missingTables.Count == 0)
                {
                    return new HealthCheckItem
                    {
                        CheckName = "Required Tables",
                        Status = "OK",
                        Message = $"Tüm {requiredTables.Length} gerekli tablo mevcut"
                    };
                }
                else
                {
                    return new HealthCheckItem
                    {
                        CheckName = "Required Tables",
                        Status = "WARN",
                        Message = $"Eksik tablolar: {string.Join(", ", missingTables)}",
                        Details = string.Join("\n", missingTables)
                    };
                }
            }
            catch (Exception ex)
            {
                return new HealthCheckItem
                {
                    CheckName = "Required Tables",
                    Status = "ERROR",
                    Message = $"Tablo kontrol hatası: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Disk alanını kontrol eder
        /// </summary>
        private HealthCheckItem CheckDiskSpace()
        {
            try
            {
                string dbPath = _config.GetDatabasePath();
                string directory = Path.GetDirectoryName(dbPath);
                var driveInfo = new DriveInfo(Path.GetPathRoot(directory));

                long availableMB = driveInfo.AvailableFreeSpace / (1024 * 1024);
                long minimumRequired = 10;

                if (availableMB > minimumRequired)
                {
                    return new HealthCheckItem
                    {
                        CheckName = "Disk Space",
                        Status = "OK",
                        Message = $"Disk alanı yeterli: {availableMB} MB mevcut"
                    };
                }
                else
                {
                    return new HealthCheckItem
                    {
                        CheckName = "Disk Space",
                        Status = "FAIL",
                        Message = $"Disk alanı yetersiz: {availableMB} MB mevcut, {minimumRequired} MB gerekli"
                    };
                }
            }
            catch (Exception ex)
            {
                return new HealthCheckItem
                {
                    CheckName = "Disk Space",
                    Status = "WARN",
                    Message = $"Disk alanı kontrol uyarısı: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Backup directory'sini kontrol eder
        /// </summary>
        private HealthCheckItem CheckBackupDirectory()
        {
            try
            {
                string backupDir = _config.GetBackupDirectory();

                if (!Directory.Exists(backupDir))
                {
                    try
                    {
                        Directory.CreateDirectory(backupDir);
                    }
                    catch (Exception ex)
                    {
                        return new HealthCheckItem
                        {
                            CheckName = "Backup Directory",
                            Status = "FAIL",
                            Message = $"Backup dizini oluşturulamadı: {ex.Message}"
                        };
                    }
                }

                // Yazma testi
                string testFile = Path.Combine(backupDir, ".health_check_test");
                try
                {
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);

                    return new HealthCheckItem
                    {
                        CheckName = "Backup Directory",
                        Status = "OK",
                        Message = $"Backup dizini OK: {backupDir}"
                    };
                }
                catch (Exception ex)
                {
                    return new HealthCheckItem
                    {
                        CheckName = "Backup Directory",
                        Status = "FAIL",
                        Message = $"Backup dizinine yazma başarısız: {ex.Message}"
                    };
                }
            }
            catch (Exception ex)
            {
                return new HealthCheckItem
                {
                    CheckName = "Backup Directory",
                    Status = "WARN",
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// Backup dizininin toplam kullanım miktarını ve son yedek bilgisini raporlar
        /// </summary>
        private HealthCheckItem CheckBackupUsage()
        {
            try
            {
                string backupDir = _config.GetBackupDirectory();
                if (!Directory.Exists(backupDir))
                    return new HealthCheckItem { CheckName = "Backup Usage", Status = "WARN", Message = "Backup dizini yok" };

                long sizeBytes = 0;
                DateTime? lastBackup = null;

                var patterns = new[] { "*" + BackupManager.BACKUP_EXTENSION, "*" + BackupManager.BACKUP_EXTENSION + ".gz" };
                    var files = new List<FileInfo>();
                    foreach (var pattern in patterns)
                        files.AddRange(Directory.GetFiles(backupDir, pattern).Select(p => new FileInfo(p)));

                foreach (var f in files)
                {
                    sizeBytes += f.Length;
                    if (!lastBackup.HasValue || f.CreationTime > lastBackup.Value) lastBackup = f.CreationTime;
                }

                long sizeMB = sizeBytes / (1024 * 1024);

                return new HealthCheckItem
                {
                    CheckName = "Backup Usage",
                    Status = "OK",
                    Message = $"Backup dir size: {sizeMB} MB, Files: {files.Count}",
                    Details = lastBackup?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never"
                };
            }
            catch (Exception ex)
            {
                return new HealthCheckItem
                {
                    CheckName = "Backup Usage",
                    Status = "WARN",
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// Log directory'sini kontrol eder
        /// </summary>
        private HealthCheckItem CheckLogDirectory()
        {
            try
            {
                string logDir = _config.GetLogDirectory();

                if (!Directory.Exists(logDir))
                {
                    try
                    {
                        Directory.CreateDirectory(logDir);
                    }
                    catch (Exception ex)
                    {
                        return new HealthCheckItem
                        {
                            CheckName = "Log Directory",
                            Status = "WARN",
                            Message = $"Log dizini oluşturulamadı: {ex.Message}"
                        };
                    }
                }

                return new HealthCheckItem
                {
                    CheckName = "Log Directory",
                    Status = "OK",
                    Message = $"Log dizini OK: {logDir}"
                };
            }
            catch (Exception ex)
            {
                return new HealthCheckItem
                {
                    CheckName = "Log Directory",
                    Status = "WARN",
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// Orphaned data (Foreign key constraint violations) kontrol eder
        /// </summary>
        private HealthCheckItem CheckOrphanedData()
        {
            try
            {
                using (var connection = _context.Database.GetDbConnection())
                {
                    if (connection.State != ConnectionState.Open)
                        connection.Open();

                    using (var cmd = connection.CreateCommand())
                    {
                        // Registrations tablosunda orphaned PersonId var mı kontrol et
                        cmd.CommandText = @"
                            SELECT COUNT(*) FROM Registrations 
                            WHERE PersonId NOT IN (SELECT Id FROM Persons) 
                            AND PersonId IS NOT NULL;";

                        long orphanedCount = (long)(cmd.ExecuteScalar() ?? 0);

                        if (orphanedCount == 0)
                        {
                            return new HealthCheckItem
                            {
                                CheckName = "Orphaned Data",
                                Status = "OK",
                                Message = "Orphaned verisi yok"
                            };
                        }
                        else
                        {
                            return new HealthCheckItem
                            {
                                CheckName = "Orphaned Data",
                                Status = "WARN",
                                Message = $"{orphanedCount} orphaned record bulundu",
                                Details = "Referans verisi bulunmayan kayıtlar temizlenebilir"
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return new HealthCheckItem
                {
                    CheckName = "Orphaned Data",
                    Status = "WARN",
                    Message = $"Orphaned data kontrol uyarısı: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Database dosyasının boyutunu kontrol eder
        /// </summary>
        private HealthCheckItem CheckDatabaseFileSize()
        {
            try
            {
                string dbPath = _config.GetDatabasePath();

                if (!File.Exists(dbPath))
                {
                    return new HealthCheckItem
                    {
                        CheckName = "Database File Size",
                        Status = "WARN",
                        Message = $"Database dosyası bulunamadı: {dbPath}"
                    };
                }

                var fileInfo = new FileInfo(dbPath);
                long sizeMB = fileInfo.Length / (1024 * 1024);

                return new HealthCheckItem
                {
                    CheckName = "Database File Size",
                    Status = "OK",
                    Message = $"Database boyutu: {sizeMB} MB"
                };
            }
            catch (Exception ex)
            {
                return new HealthCheckItem
                {
                    CheckName = "Database File Size",
                    Status = "WARN",
                    Message = ex.Message
                };
            }
        }
    }

    /// <summary>
    /// Health check raporu
    /// </summary>
    public class HealthCheckReport
    {
        public DateTime CheckedAt { get; set; }
        public List<HealthCheckItem> Checks { get; set; } = new List<HealthCheckItem>();
        public bool IsHealthy { get; set; }
        public int FailedChecks { get; set; }
        public int WarningChecks { get; set; }

        public override string ToString()
        {
            return $"[{(IsHealthy ? "HEALTHY" : "UNHEALTHY")}] " +
                $"Failures: {FailedChecks}, Warnings: {WarningChecks}, " +
                $"Checked at: {CheckedAt:yyyy-MM-dd HH:mm:ss}";
        }
    }

    /// <summary>
    /// Tek bir health check item'i
    /// </summary>
    public class HealthCheckItem
    {
        public string CheckName { get; set; }
        public string Status { get; set; } // OK, WARN, FAIL, ERROR
        public string Message { get; set; }
        public string Details { get; set; }

        public override string ToString()
        {
            return $"[{Status}] {CheckName}: {Message}";
        }
    }
}

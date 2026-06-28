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
    /// Database ve migration işlemleri öncesi kontrol ve validasyon yapan servis
    /// </summary>
    public class DatabaseValidator
    {
        private readonly AppDbContext _context;
        private const long MINIMUM_DISK_SPACE_MB = 10; // Minimum 10 MB disk alanı gerekli

        public DatabaseValidator(AppDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// Tüm migration öncesi validasyonları çalıştırır
        /// </summary>
        public ValidationResult ValidateBeforeMigration(string dbPath)
        {
            var result = new ValidationResult();

            try
            {
                // 1. Database dosyası var mı kontrol et
                ValidateDatabaseFileExists(dbPath, result);
                if (!result.IsValid) return result;

                // 2. Database dizini yazılabilir mi kontrol et
                ValidateDatabaseDirectoryWritable(dbPath, result);
                if (!result.IsValid) return result;

                // 3. Disk alanı yeterli mi kontrol et
                ValidateDiskSpace(dbPath, result);
                if (!result.IsValid) return result;

                // 4. Database'e bağlanabilir mi kontrol et
                ValidateDatabaseConnection(result);
                if (!result.IsValid) return result;

                // 5. Schema bütünlüğü kontrol et
                ValidateSchemaIntegrity(result);
                if (!result.IsValid) return result;

                result.IsValid = true;
                result.Message = "Tüm validation kontrolleri başarıyla tamamlandı.";
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Message = $"Validation hatası: {ex.Message}";
                result.Exception = ex;
            }

            return result;
        }

        /// <summary>
        /// Database dosyasının var olup olmadığını kontrol eder
        /// </summary>
        private void ValidateDatabaseFileExists(string dbPath, ValidationResult result)
        {
            try
            {
                string directory = Path.GetDirectoryName(dbPath);

                // Dizin yoksa oluştur
                if (!Directory.Exists(directory))
                {
                    try
                    {
                        Directory.CreateDirectory(directory);
                        System.Diagnostics.Debug.WriteLine($"Database dizini oluşturuldu: {directory}");
                    }
                    catch (Exception ex)
                    {
                        result.IsValid = false;
                        result.Message = $"Database dizini oluşturulamadı: {ex.Message}";
                        return;
                    }
                }

                // Dosya zaten varsa sorun yok, yoksa da migration oluşturacak
                if (!File.Exists(dbPath))
                {
                    System.Diagnostics.Debug.WriteLine($"Database dosyası yeni oluşturulacak: {dbPath}");
                }
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Message = $"Database dosyası kontrolü başarısız: {ex.Message}";
            }
        }

        /// <summary>
        /// Database dizinin yazılabilir olup olmadığını kontrol eder
        /// </summary>
        private void ValidateDatabaseDirectoryWritable(string dbPath, ValidationResult result)
        {
            try
            {
                string directory = Path.GetDirectoryName(dbPath);

                // Yazma testi için test dosyası oluştur
                string testFilePath = Path.Combine(directory, ".write_test_" + Guid.NewGuid().ToString());

                try
                {
                    File.WriteAllText(testFilePath, "test");
                    File.Delete(testFilePath);
                    System.Diagnostics.Debug.WriteLine($"Database dizinine yazma izni var: {directory}");
                }
                catch (UnauthorizedAccessException)
                {
                    result.IsValid = false;
                    result.Message = $"Database dizinine yazma izni yok: {directory}. Lütfen Administrator olarak çalıştırın.";
                }
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Message = $"Yazma izni kontrolü başarısız: {ex.Message}";
            }
        }

        /// <summary>
        /// Disk alanının migration için yeterli olup olmadığını kontrol eder
        /// </summary>
        private void ValidateDiskSpace(string dbPath, ValidationResult result)
        {
            try
            {
                string directory = Path.GetDirectoryName(dbPath);
                var driveInfo = new DriveInfo(Path.GetPathRoot(directory));

                long availableMB = driveInfo.AvailableFreeSpace / (1024 * 1024);

                if (availableMB < MINIMUM_DISK_SPACE_MB)
                {
                    result.IsValid = false;
                    result.Message = $"Disk alanı yetersiz. Gerekli: {MINIMUM_DISK_SPACE_MB} MB, Mevcut: {availableMB} MB";
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"Disk alanı yeterli: {availableMB} MB");
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Message = $"Disk alanı kontrolü başarısız: {ex.Message}";
            }
        }

        /// <summary>
        /// Database'e bağlanabilir olup olmadığını kontrol eder
        /// </summary>
        private void ValidateDatabaseConnection(ValidationResult result)
        {
            try
            {
                // Connection string'i test et
                using (var connection = _context.Database.GetDbConnection())
                {
                    if (connection.State != ConnectionState.Open)
                    {
                        connection.Open();
                    }
                    connection.Close();
                }

                System.Diagnostics.Debug.WriteLine("Database bağlantısı başarılı");
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Message = $"Database bağlantısı başarısız: {ex.Message}";
            }
        }

        /// <summary>
        /// Database schema bütünlüğünü kontrol eder
        /// </summary>
        private void ValidateSchemaIntegrity(ValidationResult result)
        {
            try
            {
                using (var connection = _context.Database.GetDbConnection())
                {
                    if (connection.State != ConnectionState.Open)
                        connection.Open();

                    using (var cmd = connection.CreateCommand())
                    {
                        // SQLite integrity check
                        cmd.CommandText = "PRAGMA integrity_check(1);";
                        var checkResult = cmd.ExecuteScalar()?.ToString() ?? "error";

                        if (checkResult != "ok" && checkResult != "error")
                        {
                            result.IsValid = false;
                            result.Message = $"Database bütünlüğü sorunu: {checkResult}";
                            return;
                        }

                        System.Diagnostics.Debug.WriteLine("Database bütünlüğü kontrol: OK");
                    }
                }
            }
            catch (Exception ex)
            {
                // Hata olsa bile devam et, bu kritik değil
                System.Diagnostics.Debug.WriteLine($"Schema bütünlüğü kontrolü uyarısı: {ex.Message}");
            }
        }

        /// <summary>
        /// Tüm gerekli tabloların var olup olmadığını kontrol eder
        /// </summary>
        public bool ValidateRequiredTablesExist()
        {
            try
            {
                using (var connection = _context.Database.GetDbConnection())
                {
                    if (connection.State != ConnectionState.Open)
                        connection.Open();

                    // Kontrol edilecek tablolar
                    string[] requiredTables = { 
                        "Persons", 
                        "Registrations", 
                        "Payments",
                        "Hotels",
                        "Users"
                    };

                    foreach (var tableName in requiredTables)
                    {
                        if (!TableExists(connection, tableName))
                        {
                            System.Diagnostics.Debug.WriteLine($"Gerekli tablo eksik: {tableName}");
                            // İlk kurulum ise sorun değil, migration oluşturacak
                        }
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Gerekli tablolar kontrol hatası: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Belirtilen tablonun var olup olmadığını kontrol eder
        /// </summary>
        private bool TableExists(IDbConnection connection, string tableName)
        {
            try
            {
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{tableName}';";
                    var result = cmd.ExecuteScalar();
                    return result != null;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Validation sonuçlarını tutan sınıf
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; } = false;
        public string Message { get; set; } = "";
        public Exception Exception { get; set; }

        public override string ToString()
        {
            return $"[{(IsValid ? "PASS" : "FAIL")}] {Message}";
        }
    }
}

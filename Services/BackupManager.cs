using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TurizmOperasyonSistemi.Services
{
    /// <summary>
    /// Database backup ve restore işlemlerini yöneten servis
    /// </summary>
    public class BackupManager
    {
        private readonly string _dbPath;
        private readonly string _backupDirectory;
        private const int MAX_BACKUPS = 10; // Maksimum 10 backup dosyası tutulacak
        private const string BACKUP_PREFIX = "turizm_backup_";
        private const string BACKUP_EXTENSION = ".bak";

        public BackupManager(string dbPath)
        {
            _dbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
            _backupDirectory = Path.Combine(Path.GetDirectoryName(_dbPath), "backups");
        }

        /// <summary>
        /// Database'in bir yedeğini oluşturur
        /// </summary>
        public BackupResult CreateBackup()
        {
            var result = new BackupResult();

            try
            {
                // Backup dizini yoksa oluştur
                if (!Directory.Exists(_backupDirectory))
                {
                    Directory.CreateDirectory(_backupDirectory);
                    System.Diagnostics.Debug.WriteLine($"Backup dizini oluşturuldu: {_backupDirectory}");
                }

                // Database dosyası var mı kontrol et
                if (!File.Exists(_dbPath))
                {
                    result.IsSuccess = false;
                    result.Message = $"Database dosyası bulunamadı: {_dbPath}";
                    return result;
                }

                // Timestamp ile backup dosya adı oluştur
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                string backupFileName = $"{BACKUP_PREFIX}{timestamp}{BACKUP_EXTENSION}";
                string backupPath = Path.Combine(_backupDirectory, backupFileName);

                // Database dosyasını kopyala
                try
                {
                    File.Copy(_dbPath, backupPath, overwrite: false);
                    result.IsSuccess = true;
                    result.Message = $"Backup başarıyla oluşturuldu: {backupFileName}";
                    result.BackupPath = backupPath;
                    result.BackupFileName = backupFileName;
                    result.BackupSize = new FileInfo(backupPath).Length;

                    System.Diagnostics.Debug.WriteLine($"Backup oluşturuldu: {backupPath}");
                }
                catch (IOException ex)
                {
                    result.IsSuccess = false;
                    result.Message = $"Backup dosyası kopyalanamadı: {ex.Message}";
                    result.Exception = ex;
                    return result;
                }

                // Eski backup dosyalarını temizle
                CleanupOldBackups();

                return result;
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"Backup oluşturma hatası: {ex.Message}";
                result.Exception = ex;
                return result;
            }
        }

        /// <summary>
        /// Son backup'tan geri yükler
        /// </summary>
        public RestoreResult RestoreFromLastBackup()
        {
            var result = new RestoreResult();

            try
            {
                var lastBackup = GetLastBackup();

                if (lastBackup == null)
                {
                    result.IsSuccess = false;
                    result.Message = "Geri yüklenecek backup dosyası bulunamadı.";
                    return result;
                }

                return RestoreFromBackup(lastBackup.FullName);
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"Restore hatası: {ex.Message}";
                result.Exception = ex;
                return result;
            }
        }

        /// <summary>
        /// Belirtilen backup dosyasından geri yükler
        /// </summary>
        public RestoreResult RestoreFromBackup(string backupPath)
        {
            var result = new RestoreResult();

            try
            {
                // Backup dosyası var mı kontrol et
                if (!File.Exists(backupPath))
                {
                    result.IsSuccess = false;
                    result.Message = $"Backup dosyası bulunamadı: {backupPath}";
                    return result;
                }

                // Mevcut database dosyasını yeniden adlandır (recovery amacıyla)
                string brokenDbPath = _dbPath + ".broken_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

                try
                {
                    if (File.Exists(_dbPath))
                    {
                        File.Move(_dbPath, brokenDbPath, overwrite: false);
                        System.Diagnostics.Debug.WriteLine($"Bozuk database dosyası taşındı: {brokenDbPath}");
                    }
                }
                catch (Exception ex)
                {
                    result.IsSuccess = false;
                    result.Message = $"Mevcut database dosyası taşınamadı: {ex.Message}";
                    result.Exception = ex;
                    return result;
                }

                // Backup dosyasını orijinal konuma kopyala
                try
                {
                    File.Copy(backupPath, _dbPath, overwrite: true);
                    result.IsSuccess = true;
                    result.Message = $"Database başarıyla restore edildi: {Path.GetFileName(backupPath)}";
                    result.RestoredFromBackup = Path.GetFileName(backupPath);

                    System.Diagnostics.Debug.WriteLine($"Database restore edildi: {_dbPath}");

                    // Eğer recovery başarılıysa, bozuk dosyayı sil
                    try
                    {
                        File.Delete(brokenDbPath);
                    }
                    catch
                    {
                        System.Diagnostics.Debug.WriteLine($"Bozuk database dosyası silinemedi (izin sorunu): {brokenDbPath}");
                    }
                }
                catch (Exception ex)
                {
                    result.IsSuccess = false;
                    result.Message = $"Backup dosyası kopyalanamadı: {ex.Message}";
                    result.Exception = ex;

                    // Original database'i eski backup'tan restore etmeye çalış
                    try
                    {
                        if (File.Exists(brokenDbPath))
                        {
                            File.Move(brokenDbPath, _dbPath, overwrite: true);
                        }
                    }
                    catch { }

                    return result;
                }

                return result;
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"Restore işlemi sırasında hata: {ex.Message}";
                result.Exception = ex;
                return result;
            }
        }

        /// <summary>
        /// Tüm backup dosyalarını listelenir
        /// </summary>
        public List<FileInfo> GetAllBackups()
        {
            try
            {
                if (!Directory.Exists(_backupDirectory))
                    return new List<FileInfo>();

                var backupFiles = Directory.GetFiles(_backupDirectory, BACKUP_PREFIX + "*" + BACKUP_EXTENSION)
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(f => f.CreationTime)
                    .ToList();

                return backupFiles;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Backup dosyalarını listeleme hatası: {ex.Message}");
                return new List<FileInfo>();
            }
        }

        /// <summary>
        /// Son backup dosyasını döndürür
        /// </summary>
        public FileInfo GetLastBackup()
        {
            return GetAllBackups().FirstOrDefault();
        }

        /// <summary>
        /// Belirtilen backup dosyasının özet bilgisini döndürür
        /// </summary>
        public BackupInfo GetBackupInfo(string backupFileName)
        {
            try
            {
                string backupPath = Path.Combine(_backupDirectory, backupFileName);

                if (!File.Exists(backupPath))
                    return null;

                var fileInfo = new FileInfo(backupPath);
                return new BackupInfo
                {
                    FileName = backupFileName,
                    Size = fileInfo.Length,
                    CreatedAt = fileInfo.CreationTime,
                    LastModified = fileInfo.LastWriteTime,
                    FullPath = backupPath
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Eski backup dosyalarını temizler (MAX_BACKUPS'ı aşanları siler)
        /// </summary>
        private void CleanupOldBackups()
        {
            try
            {
                var allBackups = GetAllBackups();

                if (allBackups.Count > MAX_BACKUPS)
                {
                    // Eski olanları sil
                    var backupsToDelete = allBackups.Skip(MAX_BACKUPS).ToList();

                    foreach (var backup in backupsToDelete)
                    {
                        try
                        {
                            backup.Delete();
                            System.Diagnostics.Debug.WriteLine($"Eski backup silindi: {backup.Name}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Backup silme hatası: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Backup temizleme hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// Belirtilen backup dosyasını siler
        /// </summary>
        public bool DeleteBackup(string backupFileName)
        {
            try
            {
                string backupPath = Path.Combine(_backupDirectory, backupFileName);

                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                    System.Diagnostics.Debug.WriteLine($"Backup silindi: {backupFileName}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Backup silinirken hata: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Backup dizininin disk kullanımını döndürür (byte cinsinden)
        /// </summary>
        public long GetBackupDirectorySize()
        {
            try
            {
                if (!Directory.Exists(_backupDirectory))
                    return 0;

                return Directory.GetFiles(_backupDirectory, "*" + BACKUP_EXTENSION)
                    .Select(p => new FileInfo(p).Length)
                    .Sum();
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// Backup oluşturma sonuçlarını tutan sınıf
    /// </summary>
    public class BackupResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public string BackupPath { get; set; }
        public string BackupFileName { get; set; }
        public long BackupSize { get; set; }
        public Exception Exception { get; set; }

        public override string ToString()
        {
            return $"[{(IsSuccess ? "SUCCESS" : "FAILED")}] {Message} - Size: {BackupSize} bytes";
        }
    }

    /// <summary>
    /// Restore işlemi sonuçlarını tutan sınıf
    /// </summary>
    public class RestoreResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public string RestoredFromBackup { get; set; }
        public Exception Exception { get; set; }

        public override string ToString()
        {
            return $"[{(IsSuccess ? "SUCCESS" : "FAILED")}] {Message}";
        }
    }

    /// <summary>
    /// Backup dosya bilgisini tutan sınıf
    /// </summary>
    public class BackupInfo
    {
        public string FileName { get; set; }
        public long Size { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastModified { get; set; }
        public string FullPath { get; set; }

        public override string ToString()
        {
            return $"{FileName} ({Size} bytes) - Created: {CreatedAt:yyyy-MM-dd HH:mm:ss}";
        }
    }
}

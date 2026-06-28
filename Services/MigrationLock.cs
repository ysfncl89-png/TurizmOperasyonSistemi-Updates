using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TurizmOperasyonSistemi.Data;

namespace TurizmOperasyonSistemi.Services
{
    /// <summary>
    /// Eş zamanlı migration işlemlerini önlemek için database-level locking mekanizması
    /// </summary>
    public class MigrationLock
    {
        private readonly AppDbContext _context;
        private const int LOCK_TIMEOUT_SECONDS = 300; // 5 dakika timeout
        private const int LOCK_ACQUISITION_RETRY_MS = 500; // 500ms aralıklarla retry
        private const int MAX_LOCK_ACQUISITION_ATTEMPTS = 12; // Maksimum 6 dakika retry

        private bool _lockAcquired = false;
        private string _lockId = null;

        public MigrationLock(AppDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// Migration lock'u edinmeye çalışır (başarılı olana kadar bekler)
        /// </summary>
        public LockResult AcquireLock()
        {
            var result = new LockResult();

            try
            {
                System.Diagnostics.Debug.WriteLine($"Migration lock edinme başlıyor... (Timeout: {LOCK_TIMEOUT_SECONDS}s)");

                // Lock ID'si oluştur (makine adı + process ID + unique ID kombinasyonu)
                _lockId = GenerateLockId();

                int attempts = 0;

                while (attempts < MAX_LOCK_ACQUISITION_ATTEMPTS)
                {
                    attempts++;

                    try
                    {
                        // DatabaseVersions tablosunun create edilmesini sağla
                        EnsureLockTableExists();

                        // Database'de lock kaydını kontrol et
                        if (TryAcquireLockInDatabase())
                        {
                            _lockAcquired = true;
                            result.IsSuccess = true;
                            result.Message = $"Migration lock başarıyla edinildi (ID: {_lockId})";
                            result.LockId = _lockId;

                            System.Diagnostics.Debug.WriteLine($"✓ Migration lock edinildi: {_lockId}");
                            return result;
                        }

                        // Lock alınamadı, diğer process açık tutuyor
                        if (attempts == 1)
                        {
                            System.Diagnostics.Debug.WriteLine($"Migration lock meşgul, bekleniyor... (Deneme {attempts}/{MAX_LOCK_ACQUISITION_ATTEMPTS})");
                        }

                        // Biraz bekle ve yeniden dene
                        Thread.Sleep(LOCK_ACQUISITION_RETRY_MS);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Lock edinme deneme {attempts} başarısız: {ex.Message}");
                    }
                }

                // Timeout doldu
                result.IsSuccess = false;
                result.Message = $"Migration lock {MAX_LOCK_ACQUISITION_ATTEMPTS * LOCK_ACQUISITION_RETRY_MS / 1000} saniye içinde alınamadı. " +
                    "Başka bir process migration yapıyor olabilir. Lütfen daha sonra tekrar deneyin.";

                System.Diagnostics.Debug.WriteLine($"✗ Migration lock alınamadı: Timeout");
                return result;
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"Lock edinme hatası: {ex.Message}";
                result.Exception = ex;
                return result;
            }
        }

        /// <summary>
        /// Migration lock'u bırakır
        /// </summary>
        public void ReleaseLock()
        {
            try
            {
                if (!_lockAcquired)
                {
                    System.Diagnostics.Debug.WriteLine("Lock edinilmemişti, bırakılmaz");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"Migration lock bırakılıyor: {_lockId}");

                using (var connection = _context.Database.GetDbConnection())
                {
                    if (connection.State != ConnectionState.Open)
                        connection.Open();

                    using (var cmd = connection.CreateCommand())
                    {
                        // Lock kaydını sil
                        cmd.CommandText = @"DELETE FROM __MigrationLocks WHERE LockId = @lockId;";
                        var param = cmd.CreateParameter();
                        param.ParameterName = "@lockId";
                        param.Value = _lockId;
                        cmd.Parameters.Add(param);

                        cmd.ExecuteNonQuery();
                    }
                }

                _lockAcquired = false;
                System.Diagnostics.Debug.WriteLine($"✓ Migration lock bırakıldı: {_lockId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Lock bırakma hatası (kritik değil): {ex.Message}");
            }
        }

        /// <summary>
        /// Database'de lock kaydını oluşturmaya çalışır
        /// </summary>
        private bool TryAcquireLockInDatabase()
        {
            try
            {
                using (var connection = _context.Database.GetDbConnection())
                {
                    if (connection.State != ConnectionState.Open)
                        connection.Open();

                    // Eski lock'ları kontrol et (timeout olanları sil)
                    CleanupExpiredLocks(connection);

                    // Başka bir aktif lock var mı kontrol et
                    using (var checkCmd = connection.CreateCommand())
                    {
                        checkCmd.CommandText = @"SELECT COUNT(*) FROM __MigrationLocks WHERE ExpiresAt > datetime('now');";
                        var existingLocks = (long)checkCmd.ExecuteScalar();

                        if (existingLocks > 0)
                        {
                            // Başka lock var
                            return false;
                        }
                    }

                    // Yeni lock kaydı oluştur
                    using (var insertCmd = connection.CreateCommand())
                    {
                        insertCmd.CommandText = @"INSERT INTO __MigrationLocks (LockId, AcquiredAt, ExpiresAt, MachineName, ProcessId)
                            VALUES (@lockId, datetime('now'), datetime('now', '+' || @timeout || ' seconds'), @machineName, @processId);";

                        var param1 = insertCmd.CreateParameter();
                        param1.ParameterName = "@lockId";
                        param1.Value = _lockId;
                        insertCmd.Parameters.Add(param1);

                        var param2 = insertCmd.CreateParameter();
                        param2.ParameterName = "@timeout";
                        param2.Value = LOCK_TIMEOUT_SECONDS;
                        insertCmd.Parameters.Add(param2);

                        var param3 = insertCmd.CreateParameter();
                        param3.ParameterName = "@machineName";
                        param3.Value = Environment.MachineName;
                        insertCmd.Parameters.Add(param3);

                        var param4 = insertCmd.CreateParameter();
                        param4.ParameterName = "@processId";
                        param4.Value = System.Diagnostics.Process.GetCurrentProcess().Id;
                        insertCmd.Parameters.Add(param4);

                        insertCmd.ExecuteNonQuery();
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Database lock kontrol hatası: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// __MigrationLocks tablosunun var olup olmadığını kontrol eder ve oluşturur
        /// </summary>
        private void EnsureLockTableExists()
        {
            try
            {
                using (var connection = _context.Database.GetDbConnection())
                {
                    if (connection.State != ConnectionState.Open)
                        connection.Open();

                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS __MigrationLocks (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            LockId TEXT NOT NULL UNIQUE,
                            AcquiredAt TEXT NOT NULL,
                            ExpiresAt TEXT NOT NULL,
                            MachineName TEXT,
                            ProcessId INTEGER
                        );";
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Lock tablosu oluşturma hatası: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Timeout dolmuş lock'ları temizler
        /// </summary>
        private void CleanupExpiredLocks(IDbConnection connection)
        {
            try
            {
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"DELETE FROM __MigrationLocks WHERE ExpiresAt <= datetime('now');";
                    var deletedCount = cmd.ExecuteNonQuery();

                    if (deletedCount > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"Timeout dolmuş {deletedCount} lock silindi");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Eski lock temizleme hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// Benzersiz bir lock ID'si oluşturur
        /// </summary>
        private string GenerateLockId()
        {
            string machineName = Environment.MachineName;
            int processId = System.Diagnostics.Process.GetCurrentProcess().Id;
            string uniqueId = Guid.NewGuid().ToString().Substring(0, 8);

            return $"{machineName}_{processId}_{uniqueId}";
        }

        /// <summary>
        /// Tüm aktif lock'ları döndürür (debug amaçlı)
        /// </summary>
        public string GetActiveLocksInfo()
        {
            try
            {
                using (var connection = _context.Database.GetDbConnection())
                {
                    if (connection.State != ConnectionState.Open)
                        connection.Open();

                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = @"SELECT LockId, MachineName, ProcessId, AcquiredAt, ExpiresAt 
                            FROM __MigrationLocks 
                            WHERE ExpiresAt > datetime('now')
                            ORDER BY AcquiredAt DESC;";

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (!reader.HasRows)
                                return "Aktif lock yok";

                            var info = "Aktif Locks:\n";
                            while (reader.Read())
                            {
                                info += $"  - {reader["LockId"]} ({reader["MachineName"]}/{reader["ProcessId"]}) " +
                                    $"até {reader["ExpiresAt"]}\n";
                            }
                            return info;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return $"Lock bilgisi alınamadı: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// Lock edinme sonuçlarını tutan sınıf
    /// </summary>
    public class LockResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public string LockId { get; set; }
        public Exception Exception { get; set; }

        public override string ToString()
        {
            return $"[{(IsSuccess ? "SUCCESS" : "FAILED")}] {Message}";
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace TurizmOperasyonSistemi.Services
{
    /// <summary>
    /// OneDrive klasörünü tespit eden ve uygulama için gerekli TurizmDB altyapısını sağlayan servis.
    /// - Otomatik olarak OneDrive yolunu keşfeder (env var, registry, klasör araması).
    /// - Gerekli dizinleri oluşturur: TurizmDB, Backup, Logs, Config
    /// - Production seviyesinde hata yönetimi ve IDisposable desteği sağlar.
    /// </summary>
    public sealed class OneDriveService : IDisposable
    {
        private bool _disposed;

        /// <summary>
        /// OneDriveService oluşturulur ve keşif/olusturma işlemleri yürütülür.
        /// </summary>
        /// <exception cref="OneDriveServiceException">OneDrive bulunamaz veya dizin oluşturulamazsa fırlatılır.</exception>
        public OneDriveService()
        {
            try
            {
                BaseOneDrivePath = LocateOneDriveFolder();

                if (BaseOneDrivePath == null)
                    throw new OneDriveServiceException("OneDrive klasörü bulunamadı.");

                // Root path for our application inside OneDrive
                RootPath = Path.Combine(BaseOneDrivePath, "TurizmDB");

                DatabasePath = Path.Combine(RootPath, "Turizm.db");
                BackupPath = Path.Combine(RootPath, "Backup");
                LogPath = Path.Combine(RootPath, "Logs");
                ConfigPath = Path.Combine(RootPath, "Config");

                EnsureDirectoriesExist();
            }
            catch (OneDriveServiceException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new OneDriveServiceException("OneDriveService başlatılırken hata oluştu.", ex);
            }
        }

        /// <summary>
        /// Fiziksel OneDrive kök dizini (ör. C:\Users\Ahmet\OneDrive veya farklı sürücüdeki yol)
        /// </summary>
        public string BaseOneDrivePath { get; }

        /// <summary>
        /// Uygulamaya ayrılmış root dizini: {BaseOneDrivePath}/TurizmDB
        /// </summary>
        public string RootPath { get; }

        /// <summary>
        /// Veritabanı dosyası yolu (Turizm.db)
        /// </summary>
        public string DatabasePath { get; }

        /// <summary>
        /// Yedeklerin saklanacağı dizin yolu
        /// </summary>
        public string BackupPath { get; }

        /// <summary>
        /// Log dosyalarının saklanacağı dizin yolu
        /// </summary>
        public string LogPath { get; }

        /// <summary>
        /// Uygulama konfigürasyonunun saklanacağı dizin yolu
        /// </summary>
        public string ConfigPath { get; }

        /// <summary>
        /// OneDrive keşif adımlarını sırasıyla çalıştırır ve ilk güvenilir sonucu döndürür.
        /// </summary>
        private string LocateOneDriveFolder()
        {
            // 1) Environment variables (en yaygın)
            var candidates = new List<string?>
            {
                Environment.GetEnvironmentVariable("OneDrive"),
                Environment.GetEnvironmentVariable("OneDriveCommercial"),
                Environment.GetEnvironmentVariable("OneDriveConsumer")
            };

            foreach (var c in candidates.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                if (Directory.Exists(c))
                    return NormalizePath(c);
            }

            // 2) Registry (HKCU) - OneDrive stores user folder information here in some installations
            try
            {
                // Personal account key
                string? reg = ReadRegistryOneDriveUserFolder();
                if (!string.IsNullOrWhiteSpace(reg) && Directory.Exists(reg))
                    return NormalizePath(reg);
            }
            catch
            {
                // Registry erişim hataları fatal olmamalı; bir üst seviyede loglanabilir.
            }

            // 3) Kullanıcı profili altında isim benzerlikleri: "OneDrive", "OneDrive - "
            try
            {
                var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrWhiteSpace(profile) && Directory.Exists(profile))
                {
                    var dirs = Directory.GetDirectories(profile, "OneDrive*")
                                        .Where(d => Directory.Exists(d))
                                        .Select(NormalizePath)
                                        .ToList();

                    if (dirs.Count == 1)
                        return dirs[0];

                    // Eğer birden fazla ise TurizmDB içeriği veya mevcut veritabanını kontrol et ve önceliklendir
                    foreach (var d in dirs)
                    {
                        if (Directory.Exists(Path.Combine(d, "TurizmDB")))
                            return Path.Combine(d);

                        if (File.Exists(Path.Combine(d, "Turizm.db")))
                        {
                            return d;
                        }
                    }

                    if (dirs.Count > 0)
                        return dirs.First();
                }
            }
            catch
            {
                // ignore
            }

            // 4) Disk taraması kaldırıldı - sadece env, registry ve profile kontrolü yapılacak.

            // OneDrive bulunamadıysa kullanıcıya açık ve yönlendirici bir hata fırlat
            throw new OneDriveServiceException("OneDrive bulunamadı veya senkronizasyon tamamlanmamış.");

        }

        private static string NormalizePath(string path)
        {
            return Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        private static string? ReadRegistryOneDriveUserFolder()
        {
            try
            {
                // Common keys where OneDrive may keep user folder info
                using (var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\OneDrive"))
                {
                    if (key != null)
                    {
                        var val = key.GetValue("UserFolder") as string;
                        if (!string.IsNullOrWhiteSpace(val))
                            return val;
                    }
                }

                // Newer OneDrive versions keep account info under Accounts\Personal or Accounts\Business
                using (var key2 = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\OneDrive\\Accounts\\Client") )
                {
                    if (key2 != null)
                    {
                        var val = key2.GetValue("UserFolder") as string;
                        if (!string.IsNullOrWhiteSpace(val))
                            return val;
                    }
                }

                // Try Accounts\Personal and Accounts\Business nodes (this structure can vary)
                using (var accounts = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\OneDrive\\Accounts"))
                {
                    if (accounts != null)
                    {
                        foreach (var name in accounts.GetSubKeyNames())
                        {
                            try
                        {
                                using (var k = accounts.OpenSubKey(name))
                                {
                                    var v = k?.GetValue("UserFolder") as string;
                                    if (!string.IsNullOrWhiteSpace(v))
                                        return v;
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch
            {
                // Registry erişim sorunları yoksayılsın; üstte tekrar denendi
            }

            return null;
        }

        private void EnsureDirectoriesExist()
        {
            try
            {
                // Root and subdirectories
                Directory.CreateDirectory(RootPath);
                Directory.CreateDirectory(BackupPath);
                Directory.CreateDirectory(LogPath);
                Directory.CreateDirectory(ConfigPath);
            }
            catch (Exception ex)
            {
                throw new OneDriveServiceException("OneDrive içindeki gerekli dizinler oluşturulamadı.", ex);
            }
        }

        /// <summary>
        /// Basit yardımcı: TurizmDB içinde mevcut veritabanı veya klasör varsa taşımak/merge etmek için kullanılabilir.
        /// Bu sınıf yalnızca keşif ve hazırlık yapar; taşıma/migration opsiyonel tutulsun.
        /// </summary>
        public bool IsInitialized => !string.IsNullOrWhiteSpace(BaseOneDrivePath) && Directory.Exists(RootPath);

        #region IDisposable Support
        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // managed resources yok
                }

                // unmanaged resources yok

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }

    /// <summary>
    /// OneDriveService ile ilgili özel hata sınıfı. Üst katmanlarda ayrıntılı hata yönetimi için kullanılır.
    /// </summary>
    public sealed class OneDriveServiceException : Exception
    {
        public OneDriveServiceException() { }
        public OneDriveServiceException(string message) : base(message) { }
        public OneDriveServiceException(string message, Exception inner) : base(message, inner) { }
    }
}

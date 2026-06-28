using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Services;

namespace TurizmOperasyonSistemi.ViewModels
{
    /// <summary>
    /// Migration Dashboard'un ViewModel'i - MVVM pattern
    /// </summary>
    public class MigrationDashboardViewModel : INotifyPropertyChanged
    {
        private readonly AppDbContext _context;
        private readonly HealthCheckService _healthCheckService;
        private readonly MigrationRollbackService _rollbackService;
        private readonly ConfigurationProvider _config;
        private readonly MigrationLogger _logger;

        // Observable Collections
        private ObservableCollection<HealthCheckItemViewModel> _healthChecks;
        private ObservableCollection<BackupItemViewModel> _availableBackups;
        private ObservableCollection<MigrationLogItemViewModel> _migrationLogs;

        // Properties
        private bool _isHealthy;
        private string _databaseStatus;
        private string _databasePath;
        private string _databaseSize;
        private int _failedChecks;
        private int _warningChecks;
        private bool _isLoading;
        private string _statusMessage;
        private DateTime _lastCheckedAt;
        private bool _canRollback;
        private string _rollbackReason;

        // Commands
        public ICommand RefreshCommand { get; }
        public ICommand RollbackCommand { get; }
        public ICommand ViewLogsCommand { get; }
        public ICommand CloseCommand { get; }

        public MigrationDashboardViewModel(AppDbContext context, string dbPath = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _config = ConfigurationProvider.Instance;

            if (string.IsNullOrEmpty(dbPath))
                dbPath = _config.GetDatabasePath();

            _healthCheckService = new HealthCheckService(context);
            _rollbackService = new MigrationRollbackService(context, dbPath);
            _logger = new MigrationLogger(context, dbPath);

            // Initialize collections
            _healthChecks = new ObservableCollection<HealthCheckItemViewModel>();
            _availableBackups = new ObservableCollection<BackupItemViewModel>();
            _migrationLogs = new ObservableCollection<MigrationLogItemViewModel>();

            // Initialize commands
            RefreshCommand = new RelayCommand(async () => await RefreshHealthCheckAsync());
            RollbackCommand = new RelayCommand<BackupItemViewModel>(async backup => 
                await PerformRollbackAsync(backup));
            ViewLogsCommand = new RelayCommand(ViewLogs);
            CloseCommand = new RelayCommand(() => { /* Parent window handles close */ });

            // Set properties
            _databasePath = _config.GetDatabasePath();
            _canRollback = _rollbackService.CanRollback();
            _rollbackReason = _rollbackService.GetRollbackAvailabilityReason();

            StatusMessage = "Başlangıç kontrolleri yapılıyor...";
        }

        // ==================== Public Properties ====================

        public ObservableCollection<HealthCheckItemViewModel> HealthChecks
        {
            get => _healthChecks;
            set { _healthChecks = value; OnPropertyChanged(); }
        }

        public ObservableCollection<BackupItemViewModel> AvailableBackups
        {
            get => _availableBackups;
            set { _availableBackups = value; OnPropertyChanged(); }
        }

        public ObservableCollection<MigrationLogItemViewModel> MigrationLogs
        {
            get => _migrationLogs;
            set { _migrationLogs = value; OnPropertyChanged(); }
        }

        public bool IsHealthy
        {
            get => _isHealthy;
            set { _isHealthy = value; OnPropertyChanged(); }
        }

        public string DatabaseStatus
        {
            get => _databaseStatus;
            set { _databaseStatus = value; OnPropertyChanged(); }
        }

        public string DatabasePath
        {
            get => _databasePath;
            set { _databasePath = value; OnPropertyChanged(); }
        }

        public string DatabaseSize
        {
            get => _databaseSize;
            set { _databaseSize = value; OnPropertyChanged(); }
        }

        public int FailedChecks
        {
            get => _failedChecks;
            set { _failedChecks = value; OnPropertyChanged(); }
        }

        public int WarningChecks
        {
            get => _warningChecks;
            set { _warningChecks = value; OnPropertyChanged(); }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public DateTime LastCheckedAt
        {
            get => _lastCheckedAt;
            set { _lastCheckedAt = value; OnPropertyChanged(); }
        }

        public bool CanRollback
        {
            get => _canRollback;
            set { _canRollback = value; OnPropertyChanged(); }
        }

        public string RollbackReason
        {
            get => _rollbackReason;
            set { _rollbackReason = value; OnPropertyChanged(); }
        }

        // ==================== Methods ====================

        /// <summary>
        /// Tüm health check'leri çalıştırır ve dashboard'u günceller
        /// </summary>
        public async Task RefreshHealthCheckAsync()
        {
            IsLoading = true;
            StatusMessage = "Health check yapılıyor...";

            try
            {
                var report = await Task.Run(() =>
                {
                    return _healthCheckService.RunCompleteHealthCheck();
                });

                // UI'ı güncelleştir
                IsHealthy = report.IsHealthy;
                FailedChecks = report.FailedChecks;
                WarningChecks = report.WarningChecks;
                LastCheckedAt = report.CheckedAt;

                // Health check item'lerini güncelle
                HealthChecks.Clear();
                foreach (var check in report.Checks)
                {
                    HealthChecks.Add(new HealthCheckItemViewModel
                    {
                        CheckName = check.CheckName,
                        Status = check.Status,
                        Message = check.Message,
                        Details = check.Details,
                        StatusColor = GetStatusColor(check.Status)
                    });
                }

                // Database status'unu güncelle
                DatabaseStatus = IsHealthy ? "✓ Sağlıklı" : "✗ Sorunlu";

                // Backup'ları yükle
                await LoadAvailableBackupsAsync();

                // Migration log'larını yükle
                await LoadMigrationLogsAsync();

                StatusMessage = $"Last check: {LastCheckedAt:yyyy-MM-dd HH:mm:ss}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Hata: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Dashboard refresh error: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Mevcut backup'ları yükler
        /// </summary>
        private async Task LoadAvailableBackupsAsync()
        {
            try
            {
                var backups = await Task.Run(() =>
                {
                    return _rollbackService.GetAvailableBackups();
                });

                AvailableBackups.Clear();
                foreach (var backup in backups.OrderByDescending(b => b.CreatedAt))
                {
                    AvailableBackups.Add(new BackupItemViewModel
                    {
                        FileName = backup.FileName,
                        Size = FormatFileSize(backup.Size),
                        CreatedAt = backup.CreatedAt,
                        LastModified = backup.LastModified,
                        FullPath = backup.FullPath,
                        CreatedAtFormatted = backup.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load backups: {ex.Message}");
            }
        }

        /// <summary>
        /// Migration log'larını yükler
        /// </summary>
        private async Task LoadMigrationLogsAsync()
        {
            try
            {
                var logs = await Task.Run(() =>
                {
                    // MigrationLogger'dan son 10 log'u al
                    return new List<MigrationLogItem>(); // Şimdilik boş, MigrationLogger'a method eklenecek
                });

                MigrationLogs.Clear();
                foreach (var log in logs.OrderByDescending(l => l.CreatedAt).Take(10))
                {
                    MigrationLogs.Add(new MigrationLogItemViewModel
                    {
                        Operation = log.Operation,
                        Message = log.Message,
                        IsSuccess = log.IsSuccess,
                        CreatedAt = log.CreatedAt,
                        CreatedAtFormatted = log.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        StatusColor = log.IsSuccess ? "#4CAF50" : "#F44336"
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load logs: {ex.Message}");
            }
        }

        /// <summary>
        /// Rollback işlemini başlatır
        /// </summary>
        private async Task PerformRollbackAsync(BackupItemViewModel backup)
        {
            if (backup == null)
            {
                StatusMessage = "Lütfen rollback yapılacak backup'ı seçin";
                return;
            }

            IsLoading = true;
            StatusMessage = $"Rollback yapılıyor: {backup.FileName}...";

            try
            {
                var result = await Task.Run(() =>
                {
                    return _rollbackService.RollbackToBackup(backup.FileName);
                });

                if (result.IsSuccess)
                {
                    StatusMessage = $"✓ Rollback başarılı: {backup.CreatedAtFormatted}";
                    _logger.LogOperation("Rollback", $"Backup'dan geri döndürüldü: {backup.FileName}", true);

                    // Yeniden health check çalıştır
                    await RefreshHealthCheckAsync();
                }
                else
                {
                    StatusMessage = $"✗ Rollback başarısız: {result.Message}";
                    _logger.LogOperation("Rollback", result.Message, false);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Rollback hatası: {ex.Message}";
                _logger.LogError("RollbackAsync", ex);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Log penceresini açar
        /// </summary>
        private void ViewLogs()
        {
            System.Diagnostics.Debug.WriteLine("Opening migration logs...");
            // Log viewer window burada açılacak
        }

        // ==================== Helper Methods ====================

        private string GetStatusColor(string status)
        {
            return status switch
            {
                "OK" => "#4CAF50",      // Yeşil
                "WARN" => "#FF9800",    // Turuncu
                "FAIL" => "#F44336",    // Kırmızı
                "ERROR" => "#9C27B0",   // Mor
                _ => "#757575"          // Gri
            };
        }

        private string FormatFileSize(long bytes)
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

        // ==================== INotifyPropertyChanged ====================

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // ==================== ViewModel Item Classes ====================

    public class HealthCheckItemViewModel
    {
        public string CheckName { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public string Details { get; set; }
        public string StatusColor { get; set; }
    }

    public class BackupItemViewModel
    {
        public string FileName { get; set; }
        public string Size { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastModified { get; set; }
        public string FullPath { get; set; }
        public string CreatedAtFormatted { get; set; }
    }

    public class MigrationLogItemViewModel
    {
        public string Operation { get; set; }
        public string Message { get; set; }
        public bool IsSuccess { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedAtFormatted { get; set; }
        public string StatusColor { get; set; }
    }

    // ==================== Helper Classes ====================

    public class MigrationLogItem
    {
        public string Operation { get; set; }
        public string Message { get; set; }
        public bool IsSuccess { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// WPF compatible RelayCommand implementation
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object parameter) => _execute();
    }

    /// <summary>
    /// Generic RelayCommand implementation
    /// </summary>
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;

        public RelayCommand(Action<T> execute, Func<T, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke((T)parameter) ?? true;
        public void Execute(object parameter) => _execute((T)parameter);
    }
}

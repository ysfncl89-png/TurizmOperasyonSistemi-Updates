using System;
using System.Windows;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.ViewModels;

namespace TurizmOperasyonSistemi.Windows
{
    /// <summary>
    /// Migration Dashboard penceresinin code-behind (WPF)
    /// </summary>
    public partial class MigrationDashboardWindow : Window
    {
        private readonly MigrationDashboardViewModel _viewModel;

        public MigrationDashboardWindow(AppDbContext context, string dbPath = null)
        {
            InitializeComponent();

            // ViewModel oluştur ve set et
            _viewModel = new MigrationDashboardViewModel(context, dbPath);
            this.DataContext = _viewModel;

            // Window'u kapatmak için command'ı bağla
            var closeCommand = new RelayCommand(() => this.Close());
            _viewModel.CloseCommand = closeCommand;
        }

        /// <summary>
        /// Pencere yüklendiğinde health check'i başlat
        /// </summary>
        protected override async void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);

            try
            {
                // İlk health check'i başlat
                await _viewModel.RefreshHealthCheckAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing dashboard: {ex.Message}");
                MessageBox.Show(
                    $"Dashboard başlatma hatası: {ex.Message}",
                    "Hata",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        /// <summary>
        /// Pencere kapatılırken temizlik yap
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            System.Diagnostics.Debug.WriteLine("Migration Dashboard closed");
        }
    }
}

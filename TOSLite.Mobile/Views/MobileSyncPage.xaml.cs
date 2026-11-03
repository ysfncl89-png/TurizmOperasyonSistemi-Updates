using System.Collections.Concurrent;
using TOSLite.Mobile.Services;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace TOSLite.Mobile.Views;

public partial class MobileSyncPage : ContentPage
{
    private readonly MobileSyncService _syncService;
    private readonly ConcurrentDictionary<string, byte> _seen = new();
    private int _processing;

    public MobileSyncPage(MobileSyncService syncService)
    {
        InitializeComponent();
        _syncService = syncService;
        MobileTheme.Apply(this);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        SyncButton.IsVisible = _syncService.IsPaired;
        UnpairButton.IsVisible = _syncService.IsPaired;
        if (_syncService.IsPaired)
            StatusLabel.Text = $"Eşleştirildi: {_syncService.PairedAddress} . Eşitleme işlemini siz başlatabilirsiniz.";
    }

    private async void OnScanClicked(object? sender, EventArgs e)
    {
        try
        {
            var status = await Permissions.RequestAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
            {
                await DisplayAlertAsync("Kamera izni", "QR kodu okutmak için kamera izni gereklidir.", "Tamam");
                return;
            }
            _seen.Clear();
            CameraView.IsDetecting = true;
            ScanButton.IsEnabled = false;
            StatusLabel.Text = "Masaüstü eşleştirme QR kodunu kameraya gösterin.";
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Kamera açılamadı", ex.Message, "Tamam");
        }
    }

    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        var text = e.Results.FirstOrDefault(x => x.Format == BarcodeFormat.QrCode)?.Value;
        if (string.IsNullOrWhiteSpace(text) || !_seen.TryAdd(text, 0) || Interlocked.Exchange(ref _processing, 1) != 0) return;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            CameraView.IsDetecting = false;
            try
            {
                StatusLabel.Text = "Masaüstüyle güvenli eşleştirme yapılıyor...";
                await _syncService.PairAsync(text);
                SyncButton.IsVisible = true;
                UnpairButton.IsVisible = true;
                ScanButton.IsEnabled = true;
                StatusLabel.Text = $"Eşleştirme tamamlandı: {_syncService.PairedAddress}. Senkronizasyon için Şimdi Eşitle'ye dokunun.";
            }
            catch (Exception ex)
            {
                ScanButton.IsEnabled = true;
                StatusLabel.Text = "Eşleştirme başarısız.";
                await DisplayAlertAsync("Eşleştirme hatası", ex.Message, "Tamam");
            }
            finally { Interlocked.Exchange(ref _processing, 0); }
        });
    }

    private async void OnSyncClicked(object? sender, EventArgs e)
    {
        SyncButton.IsEnabled = false;
        try
        {
            ResultPanel.IsVisible = false;
            StatusLabel.Text = "Mobil kayıtlar masaüstüne aktarılıyor...";
            var result = await _syncService.SyncAsync();
            var hasConflicts = result.Conflicts.Count > 0;
            ResultIcon.Text = hasConflicts ? "!" : "✓";
            ResultIcon.TextColor = hasConflicts ? Color.FromArgb("#D97706") : Color.FromArgb("#20A464");
            ResultTitle.Text = hasConflicts ? "Eşitleme tamamlandı, inceleme gerekli" : "Tüm kayıtlar eşitlendi";
            ResultTitle.TextColor = hasConflicts ? Color.FromArgb("#A65A00") : Color.FromArgb("#176B43");
            ResultSummary.Text = $"Kişi: {result.ImportedPersons}, tur kaydı: {result.ImportedRegistrations}, ayar: {result.ImportedSettings}, oda yerleşimi: {result.ImportedRooming}";
            ImportedItemsView.ItemsSource = result.ImportedItems;
            ConflictSummary.Text = hasConflicts
                ? $"{result.Conflicts.Count} kayıt çakışma nedeniyle aktarılmadı. Ayrıntılar masaüstü raporunda görüntülenebilir."
                : string.Empty;
            ConflictSummary.IsVisible = hasConflicts;
            ResultPanel.IsVisible = true;
            StatusLabel.Text = hasConflicts ? "Eşitleme tamamlandı; bazı kayıtlar incelenmeli." : "Eşitleme tamamlandı.";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Eşitleme tamamlanamadı.";
            await DisplayAlertAsync("Eşitleme hatası", ex.Message, "Tamam");
        }
        finally { SyncButton.IsEnabled = true; }
    }

    private async void OnUnpairClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Eşleşmeyi kaldır", "Bu telefonun kayıtlı masaüstü bağlantısı silinsin mi?", "Kaldır", "Vazgeç");
        if (!confirm) return;
        _syncService.Unpair();
        SyncButton.IsVisible = false;
        UnpairButton.IsVisible = false;
        StatusLabel.Text = "Eşleşme kaldırıldı. Yeniden bağlanmak için masaüstü QR kodunu okutun.";
    }

    private async void OnHomeClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}

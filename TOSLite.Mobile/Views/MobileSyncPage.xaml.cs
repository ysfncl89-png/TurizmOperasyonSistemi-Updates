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
    private bool _changingPairing;

    public MobileSyncPage(MobileSyncService syncService)
    {
        InitializeComponent();
        _syncService = syncService;
        MobileTheme.Apply(this);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UpdatePairingUi();
    }

    private void UpdatePairingUi()
    {
        SyncButton.IsVisible = _syncService.IsPaired && !_syncService.IsAwaitingDesktopSync;
        DesktopSyncButton.IsVisible = _syncService.IsPaired && _syncService.IsAwaitingDesktopSync;
        UnpairButton.IsVisible = _syncService.IsPaired;
        ChangePairingButton.IsVisible = _syncService.IsPaired && !_changingPairing;
        ManualPairPanel.IsVisible = !_syncService.IsPaired || _changingPairing;
        ScanButton.IsVisible = !_changingPairing;
        CameraView.IsVisible = !_changingPairing;
        SyncButton.IsVisible = _syncService.IsPaired && !_syncService.IsAwaitingDesktopSync && !_changingPairing;
        DesktopSyncButton.IsVisible = _syncService.IsPaired && _syncService.IsAwaitingDesktopSync && !_changingPairing;
        UnpairButton.IsVisible = _syncService.IsPaired && !_changingPairing;
        CancelManualPairButton.IsVisible = _changingPairing;
        ManualPairHeading.Text = _changingPairing ? "Yeni masaüstüyle eşleştir" : "Kamera kullanamıyor musunuz?";
        if (_syncService.IsPaired)
            StatusLabel.Text = _syncService.IsAwaitingDesktopSync
                ? "Mobil veriler masaüstüne aktarıldı. Eşitlemeyi tamamlamak için kırmızı düğmeye dokunun."
                : $"Eşleştirildi: {_syncService.PairedAddress}. İlk aşamayı başlatabilirsiniz.";
    }

    private void OnChangePairingClicked(object? sender, EventArgs e)
    {
        _changingPairing = true;
        PairingTextEntry.Text = string.Empty;
        ChangePairingButton.IsVisible = false;
        ManualPairPanel.IsVisible = true;
        ManualPairHeading.Text = "Yeni masaüstüyle eşleştir";
        ScanButton.IsVisible = false;
        CameraView.IsVisible = false;
        SyncButton.IsVisible = false;
        DesktopSyncButton.IsVisible = false;
        UnpairButton.IsVisible = false;
        CancelManualPairButton.IsVisible = true;
        StatusLabel.Text = "Masaüstü eşleştirme metnini yapıştırın. İşlem başarılı olana kadar mevcut eşleşme korunur.";
    }

    private void OnCancelManualPairClicked(object? sender, EventArgs e)
    {
        _changingPairing = false;
        ManualPairPanel.IsVisible = false;
        PairingTextEntry.Text = string.Empty;
        UpdatePairingUi();
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
                SyncButton.IsVisible = !_syncService.IsAwaitingDesktopSync;
                UnpairButton.IsVisible = true;
                ManualPairPanel.IsVisible = false;
                _changingPairing = false;
                ChangePairingButton.IsVisible = true;
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

    private async void OnPastePairingTextClicked(object? sender, EventArgs e)
    {
        try
        {
            if (Clipboard.Default.HasText)
                PairingTextEntry.Text = await Clipboard.Default.GetTextAsync();
            else
                await DisplayAlertAsync("Pano boş", "Önce masaüstündeki eşleştirme metnini kopyalayın.", "Tamam");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Yapıştırılamadı", ex.Message, "Tamam");
        }
    }

    private async void OnManualPairClicked(object? sender, EventArgs e)
    {
        var text = PairingTextEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            await DisplayAlertAsync("Eşleştirme metni gerekli", "Masaüstü QR ekranından kopyaladığınız eşleştirme metnini yapıştırın.", "Tamam");
            return;
        }

        ManualPairButton.IsEnabled = false;
        try
        {
            StatusLabel.Text = "Masaüstüyle güvenli eşleştirme yapılıyor...";
            await _syncService.PairAsync(text);
            SyncButton.IsVisible = !_syncService.IsAwaitingDesktopSync;
            UnpairButton.IsVisible = true;
            ManualPairPanel.IsVisible = false;
            _changingPairing = false;
            ChangePairingButton.IsVisible = true;
            StatusLabel.Text = $"Eşleştirme tamamlandı: {_syncService.PairedAddress}. Senkronizasyon için Şimdi Eşitle'ye dokunun.";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Eşleştirme başarısız.";
            await DisplayAlertAsync("Eşleştirme hatası", ex.Message, "Tamam");
        }
        finally
        {
            ManualPairButton.IsEnabled = true;
        }
    }

    private async void OnSyncClicked(object? sender, EventArgs e)
    {
        SyncButton.IsEnabled = false;
        try
        {
            ResultPanel.IsVisible = false;
            FinalSyncMessage.IsVisible = false;
            StatusLabel.Text = "Mobil kayıtlar masaüstüne aktarılıyor...";
            var result = await _syncService.SyncAsync();
            var hasConflicts = result.Conflicts.Count > 0;
            ResultIcon.Text = hasConflicts ? "!" : "✓";
            ResultIcon.TextColor = hasConflicts ? Color.FromArgb("#D97706") : Color.FromArgb("#20A464");
            ResultTitle.Text = hasConflicts ? "Masaüstüne aktarım tamamlandı, inceleme gerekli" : "Mobil veriler masaüstüne aktarıldı";
            ResultTitle.TextColor = hasConflicts ? Color.FromArgb("#A65A00") : Color.FromArgb("#176B43");
            ResultSummary.Text = $"Kişi: {result.ImportedPersons}, tur kaydı: {result.ImportedRegistrations}, ayar: {result.ImportedSettings}, oda yerleşimi: {result.ImportedRooming}";
            ImportedItemsView.ItemsSource = result.ImportedItems;
            ConflictSummary.Text = hasConflicts
                ? $"{result.Conflicts.Count} kayıt çakışma nedeniyle aktarılmadı. Ayrıntılar masaüstü raporunda görüntülenebilir."
                : string.Empty;
            ConflictSummary.IsVisible = hasConflicts;
            ResultPanel.IsVisible = true;
            SyncButton.IsVisible = false;
            DesktopSyncButton.IsVisible = true;
            StatusLabel.Text = hasConflicts
                ? "Bazı kayıtlar çözümlenemedi. Masaüstü raporunu inceledikten sonra kırmızı düğmeyle devam edin."
                : "Mobilden masaüstüne aktarım tamamlandı. Şimdi kırmızı düğmeye dokunarak eşitlemeyi bitirin.";
        }
        catch (Exception ex)
        {
            var errorDetails = GetExceptionDetails(ex);
            StatusLabel.Text = "Mobilden masaüstüne eşitleme tamamlanamadı: " + errorDetails;
            await DisplayAlertAsync("Eşitleme hatası", errorDetails, "Tamam");
        }
        finally { SyncButton.IsEnabled = true; }
    }

    private async void OnDesktopSyncClicked(object? sender, EventArgs e)
    {
        DesktopSyncButton.IsEnabled = false;
        SyncButton.IsEnabled = false;
        try
        {
            ResultPanel.IsVisible = false;
            FinalSyncMessage.IsVisible = false;
            StatusLabel.Text = "Masaüstü verileri yedek alınarak mobil uygulamaya aktarılıyor...";
            var result = await _syncService.SyncFromDesktopAsync();
            ResultIcon.Text = "✓";
            ResultIcon.TextColor = Color.FromArgb("#20A464");
            ResultTitle.Text = "Her iki cihaz da eşitlendi";
            ResultTitle.TextColor = Color.FromArgb("#176B43");
            ResultSummary.Text = $"Kişi: {result.Persons.Count(x => !x.IsDeleted)}, tur kaydı: {result.Registrations.Count}, ayar: {result.Settings.Count}, oda yerleşimi: {result.Rooming.Count(x => !x.IsDeleted)}";
            ImportedItemsView.ItemsSource = result.ImportedItems;
            ConflictSummary.IsVisible = false;
            FinalSyncMessage.Text = "Her iki cihaz da eşitlendi. Mobil Uygulamadaki verileriniz Masaüstü Programınızdaki verilerle aynı.";
            FinalSyncMessage.IsVisible = true;
            ResultPanel.IsVisible = true;
            DesktopSyncButton.IsVisible = false;
            StatusLabel.Text = "Eşitleme başarıyla tamamlandı.";
        }
        catch (Exception ex)
        {
            var errorDetails = GetExceptionDetails(ex);
            StatusLabel.Text = "Masaüstünden mobil uygulamaya eşitleme başarısız oldu.";
            ResultIcon.Text = "!";
            ResultIcon.TextColor = Color.FromArgb("#C62828");
            ResultTitle.Text = "Eşitleme başarısız oldu: " + errorDetails;
            ResultTitle.TextColor = Color.FromArgb("#C62828");
            ResultSummary.Text = errorDetails;
            FinalSyncMessage.IsVisible = false;
            ResultPanel.IsVisible = true;
            await DisplayAlertAsync("Eşitleme hatası", errorDetails, "Tamam");
        }
        finally
        {
            DesktopSyncButton.IsEnabled = true;
            SyncButton.IsEnabled = true;
        }
    }

    private async void OnUnpairClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Eşleşmeyi kaldır", "Bu telefonun kayıtlı masaüstü bağlantısı silinsin mi?", "Kaldır", "Vazgeç");
        if (!confirm) return;
        _syncService.Unpair();
        SyncButton.IsVisible = false;
        DesktopSyncButton.IsVisible = false;
        UnpairButton.IsVisible = false;
        StatusLabel.Text = "Eşleşme kaldırıldı. Yeniden bağlanmak için masaüstü QR kodunu okutun.";
    }

    private static string GetExceptionDetails(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(current.Message) && !messages.Contains(current.Message))
                messages.Add(current.Message);
        }
        return string.Join("\n\n", messages);
    }

    private async void OnHomeClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}

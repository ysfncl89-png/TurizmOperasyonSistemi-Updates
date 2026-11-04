using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using QRCoder;
using TurizmOperasyonSistemi.Services;

namespace TurizmOperasyonSistemi.Views;

public partial class MobileSyncWindow : Window
{
    private readonly MobileSyncServer _server;

    public MobileSyncWindow()
    {
        InitializeComponent();
        _server = new MobileSyncServer();
        AddressText.Text = _server.BaseUrl;
        FingerprintText.Text = _server.Fingerprint;
        QrImage.Source = CreateQrImage(_server.PairingPayload);
        _server.StatusChanged += OnStatusChanged;
        _server.SyncCompleted += OnSyncCompleted;
        Closed += (_, _) => _server.Dispose();
        EnsureFirewallAccess();
    }

    private void EnsureFirewallAccess()
    {
        if (MobileSyncFirewall.IsRuleInstalled()) return;
        var result = MessageBox.Show(
            "Telefonun bu bilgisayara eşitleme bağlantısı kurabilmesi için Windows Güvenlik Duvarı'nda yalnızca TOS Lite'a ait TCP 49321 bağlantısına izin verilmelidir.\n\nİzin verildiğinde Windows yönetici onayı isteyecek. Kural sadece yerel ağdan gelen bağlantılara açıktır.",
            "Mobil eşitleme bağlantı izni",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (result != MessageBoxResult.Yes)
        {
            StatusText.Text = "Telefon bağlantısı için Windows Güvenlik Duvarı izni gerekiyor.";
            return;
        }

        try
        {
            MobileSyncFirewall.AddRule();
            StatusText.Text = "Yerel ağ bağlantı izni hazır. QR kodu okutabilirsiniz.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Bağlantı izni eklenemedi veya onaylanmadı: {ex.Message}";
        }
    }

    private void OnStatusChanged(string status)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = status;
            if (status == "Yeni eşleştirme kodu hazır.")
                QrImage.Source = CreateQrImage(_server.PairingPayload);
        });
    }

    private void OnSyncCompleted(MobileSyncResultDto result)
    {
        Dispatcher.Invoke(() =>
        {
            SummaryText.Text = $"Kişi: {result.ImportedPersons}, tur kaydı: {result.ImportedRegistrations}, ayar: {result.ImportedSettings}, oda yerleşimi: {result.ImportedRooming} aktarıldı. Çakışma: {result.Conflicts.Count}.";
            ImportedPersonsList.ItemsSource = GetImportedItems(result, "Kişi");
            ImportedRegistrationsList.ItemsSource = GetImportedItems(result, "Tur kaydı");
            ImportedRoomingList.ItemsSource = GetImportedItems(result, "Oda yerleşimi");
            ImportedSettingsList.ItemsSource = GetImportedItems(result, "Ayar");
            ConflictList.ItemsSource = result.Conflicts;
            SyncCompletionPanel.Visibility = Visibility.Visible;
            SyncSuccessIcon.Text = result.Conflicts.Count == 0 ? "✓" : "i";
            SyncSuccessIcon.Foreground = result.Conflicts.Count == 0
                ? System.Windows.Media.Brushes.ForestGreen
                : System.Windows.Media.Brushes.DarkOrange;
            SyncInstructionText.Text = result.Conflicts.Count == 0
                ? "Mobilden Masaüstüne eşitleme yapıldı. Şimdi mobil cihazınızdan “Masaüstü Programından Mobil Programa Eşitle” düğmesine tıklayarak eşitlemeyi tamamlayın."
                : $"Eşitleme bağlantısı kuruldu ve aktarım tamamlandı. {result.Conflicts.Count} kayıt aktarılmadı; ayrıntılarını aşağıdan inceleyin. Ardından mobil cihazınızdan “Masaüstü Programından Mobil Programa Eşitle” düğmesine tıklayın.";
        });
    }

    private static List<string> GetImportedItems(MobileSyncResultDto result, string recordType)
    {
        var items = result.ImportedItems
            .Where(item => string.Equals(item.RecordType, recordType, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Description)
            .ToList();
        return items.Count == 0 ? ["Bu eşitlemede kayıt aktarılmadı."] : items;
    }

    private static BitmapImage CreateQrImage(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        using var qr = new PngByteQRCode(data);
        using var stream = new MemoryStream(qr.GetGraphic(8));
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

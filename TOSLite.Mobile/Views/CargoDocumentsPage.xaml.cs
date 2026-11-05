using System.Globalization;
using TOSLite.Mobile.Data;
using TOSLite.Mobile.Models;
using TOSLite.Mobile.Services;

namespace TOSLite.Mobile.Views;

public sealed class CargoCandidate
{
    public int RegistrationId { get; init; }
    public int? DesktopRegistrationId { get; init; }
    public string PersonName { get; init; } = string.Empty;
    public string Tc { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public string TourType { get; init; } = string.Empty;
    public string Period { get; init; } = string.Empty;
    public string GroupType { get; init; } = string.Empty;
    public string DaysCount { get; init; } = string.Empty;
    public string TourSummary => $"{TourType} · {Period} · {GroupType} · {DaysCount}";
}

public partial class CargoDocumentsPage : ContentPage
{
    private readonly RegistrationRepository _registrations;
    private readonly PersonRepository _persons;
    private readonly CargoQueueRepository _queue;
    private readonly ListExportService _export;
    private readonly DownloadFileService _downloads;
    private readonly List<CargoCandidate> _allCandidates = [];
    private CargoCandidate? _selected;
    private bool _loadingFilters;

    public CargoDocumentsPage(RegistrationRepository registrations, PersonRepository persons, CargoQueueRepository queue, ListExportService export, DownloadFileService downloads)
    {
        InitializeComponent();
        _registrations = registrations; _persons = persons; _queue = queue; _export = export; _downloads = downloads;
        MobileTheme.Apply(this);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _queue.InitializeAsync();
            await LoadFiltersAsync();
            await LoadCandidatesAsync();
            await RefreshQueueAsync();
        }
        catch (Exception ex) { await DisplayAlertAsync("Hata", $"Kargo evrakları yüklenemedi: {ex.Message}", "Tamam"); }
    }

    private async Task LoadFiltersAsync()
    {
        _loadingFilters = true;
        var registrations = (await _registrations.GetAllAsync()).Where(x => x.IsActive && IsSending(x.CargoRequest)).ToList();
        static string[] Values(IEnumerable<string> values) => new[] { "Tümü" }.Concat(values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(x => x)).ToArray();
        TourFilterPicker.ItemsSource = Values(registrations.Select(x => x.TourType));
        PeriodFilterPicker.ItemsSource = Values(registrations.Select(x => x.Period));
        GroupFilterPicker.ItemsSource = Values(registrations.Select(x => x.GroupType));
        TourFilterPicker.SelectedIndex = PeriodFilterPicker.SelectedIndex = GroupFilterPicker.SelectedIndex = 0;
        _loadingFilters = false;
    }

    private static bool IsSending(string? value) => string.IsNullOrWhiteSpace(value)
        || value.Trim().Equals("Seçilmedi", StringComparison.OrdinalIgnoreCase)
        || value.Trim().Equals("Seçiniz", StringComparison.OrdinalIgnoreCase)
        || value.Trim().Equals("Kargo gönderilecek", StringComparison.OrdinalIgnoreCase);

    private async Task LoadCandidatesAsync()
    {
        _allCandidates.Clear();
        var people = (await _persons.GetAllAsync()).ToDictionary(x => x.Id);
        var queuedRegistrationIds = (await _queue.GetQueueAsync()).Select(x => x.RegistrationId).ToHashSet();
        foreach (var registration in (await _registrations.GetAllAsync()).Where(x => x.IsActive && IsSending(x.CargoRequest) && !queuedRegistrationIds.Contains(x.Id)))
        {
            if (!people.TryGetValue(registration.PersonId, out var person)) continue;
            _allCandidates.Add(new CargoCandidate
            {
                RegistrationId = registration.Id, DesktopRegistrationId = registration.Id,
                PersonName = $"{person.FirstName} {person.LastName}".Trim(), Tc = person.TcKimlikNo,
                PhoneNumber = person.PhoneNumber, Address = person.Address,
                TourType = registration.TourType, Period = registration.Period,
                GroupType = registration.GroupType, DaysCount = registration.DaysCount
            });
        }
        ApplyCandidateFilters();
    }

    private void ApplyCandidateFilters()
    {
        var query = CandidateSearchEntry.Text?.Trim() ?? string.Empty;
        var tour = SelectedFilter(TourFilterPicker); var period = SelectedFilter(PeriodFilterPicker); var group = SelectedFilter(GroupFilterPicker);
        CandidatesCollection.ItemsSource = _allCandidates.Where(x =>
            (tour == "Tümü" || x.TourType == tour) && (period == "Tümü" || x.Period == period) && (group == "Tümü" || x.GroupType == group) &&
            (string.IsNullOrWhiteSpace(query) || x.PersonName.Contains(query, StringComparison.CurrentCultureIgnoreCase) || x.TourSummary.Contains(query, StringComparison.CurrentCultureIgnoreCase) || x.PhoneNumber.Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToList();
    }

    private static string SelectedFilter(Picker picker) => picker.SelectedItem?.ToString() ?? "Tümü";
    private void OnCandidateSearchChanged(object? sender, TextChangedEventArgs e) => ApplyCandidateFilters();
    private void OnFilterChanged(object? sender, EventArgs e) { if (!_loadingFilters) ApplyCandidateFilters(); }
    private void OnCandidateSelected(object? sender, SelectionChangedEventArgs e) => _selected = e.CurrentSelection.FirstOrDefault() as CargoCandidate;

    private async void OnPreviewCandidateClicked(object? sender, EventArgs e)
    {
        if (_selected == null) { await DisplayAlertAsync("Kayıt seçin", "Kargo havuzuna göndermeden önce incelemek istediğiniz kişiyi seçin.", "Tamam"); return; }
        await DisplayAlertAsync("Gönderi bilgilerini inceleyin", $"Kişi: {_selected.PersonName}\nKimlik: {_selected.Tc}\nTelefon: {_selected.PhoneNumber}\nAdres: {_selected.Address}\nTur: {_selected.TourSummary}", "Kapat");
    }

    private async void OnAddToQueueClicked(object? sender, EventArgs e)
    {
        if (_selected == null) { await DisplayAlertAsync("Kayıt seçin", "Havuza göndermeden önce kargo kaydını seçin ve bilgileri inceleyin.", "Tamam"); return; }
        if (!int.TryParse(QuantityEntry.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var quantity) || quantity <= 0)
        { await DisplayAlertAsync("Adet geçersiz", "Etiket adedi 1 veya daha büyük olmalıdır.", "Tamam"); return; }
        var item = _selected;
        if (!await DisplayAlertAsync("Kargo havuzuna gönder", $"{item.PersonName}\nTelefon: {item.PhoneNumber}\nAdres: {item.Address}\nEtiket adedi: {quantity}", "Havuza gönder", "Vazgeç")) return;
        await _queue.AddAsync(new CargoQueueRecord
        {
            RegistrationId = item.RegistrationId, DesktopRegistrationId = item.DesktopRegistrationId,
            FullName = item.PersonName, Tc = item.Tc, Phone = item.PhoneNumber, Address = item.Address,
            TourType = item.TourType, Period = item.Period, GroupType = item.GroupType, DaysCount = item.DaysCount, Quantity = quantity
        });
        CandidatesCollection.SelectedItem = null; _selected = null;
        await LoadCandidatesAsync(); await RefreshQueueAsync();
    }

    private async Task RefreshQueueAsync()
    {
        var query = QueueSearchEntry.Text?.Trim() ?? string.Empty;
        var items = await _queue.GetQueueAsync();
        QueueCollection.ItemsSource = items.Where(x => string.IsNullOrWhiteSpace(query) || x.DisplayTitle.Contains(query, StringComparison.CurrentCultureIgnoreCase) || x.Phone.Contains(query, StringComparison.CurrentCultureIgnoreCase) || x.Address.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    private async void OnQueueSearchChanged(object? sender, TextChangedEventArgs e) => await RefreshQueueAsync();

    private async void OnRemoveQueueItemClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not CargoQueueRecord item) return;
        if (!await DisplayAlertAsync("Havuzdan kaldır", $"{item.FullName} kaydı kargo havuzundan kaldırılsın mı?", "Kaldır", "Vazgeç")) return;
        await _queue.RemoveAsync(item.Id); await RefreshQueueAsync(); await LoadCandidatesAsync();
    }

    private async void OnExportQueueClicked(object? sender, EventArgs e)
    {
        try
        {
            var items = (await _queue.GetQueueAsync()).ToList();
            if (items.Count == 0) { await DisplayAlertAsync("Kargo havuzu boş", "PDF için önce kargo kayıtlarını havuza ekleyin.", "Tamam"); return; }
            var rows = items.Select(item => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
            {
                ["Ad Soyad"] = item.FullName, ["Telefon"] = item.Phone, ["Adres"] = item.Address,
                ["Tur"] = item.TourType, ["Dönem"] = item.Period, ["Grup"] = item.GroupType,
                ["Gün"] = item.DaysCount, ["Etiket adedi"] = item.Quantity.ToString(CultureInfo.CurrentCulture)
            }).ToList();
            var bytes = _export.CreatePdf("Kargo Havuzu", ["Ad Soyad", "Telefon", "Adres", "Tur", "Dönem", "Grup", "Gün", "Etiket adedi"], rows);
            var fileName = $"Kargo_Havuzu_{DateTime.Now:yyyyMMdd_HHmm}.pdf";
            await _downloads.SaveAsync(fileName, "application/pdf", bytes);
            await DisplayAlertAsync("İndirme tamamlandı", $"Kargo PDF dosyası İndirilenler klasörüne kaydedildi:\n{fileName}", "Tamam");
        }
        catch (Exception ex) { await DisplayAlertAsync("PDF indirilemedi", ex.Message, "Tamam"); }
    }
}

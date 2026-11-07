using System.Globalization;
using TOSLite.Mobile.Data;
using TOSLite.Mobile.Models;
using TOSLite.Mobile.Services;

namespace TOSLite.Mobile.Views;

public sealed class CargoCandidate
{
    public int RegistrationId { get; init; }
    public int PersonId { get; init; }
    public int? DesktopRegistrationId { get; init; }
    public string PersonName { get; init; } = string.Empty;
    public string Tc { get; init; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string TourType { get; init; } = string.Empty;
    public string Period { get; init; } = string.Empty;
    public string GroupType { get; init; } = string.Empty;
    public string DaysCount { get; init; } = string.Empty;
    public string TourSummary => $"{TourType} · {Period} · {GroupType} · {DaysCount}";
}

public partial class CargoDocumentsPage : ContentPage
{
    private sealed record QueueFilterChoice(string Label, string Value, bool IsAll = false, bool IsUnspecified = false);

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
                RegistrationId = registration.Id, PersonId = person.Id, DesktopRegistrationId = registration.Id,
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
        var candidate = _selected;
        var edited = await ShowCandidatePreviewAsync(candidate);
        if (edited is null) return;
        await _persons.UpdateCargoContactAsync(candidate.PersonId, edited.Value.Phone, edited.Value.Address);
        candidate.PhoneNumber = edited.Value.Phone;
        candidate.Address = edited.Value.Address;
        ApplyCandidateFilters();
    }

    private async Task<(string Phone, string Address)?> ShowCandidatePreviewAsync(CargoCandidate candidate)
    {
        var phoneEntry = new Entry { Text = candidate.PhoneNumber, Placeholder = "Telefon", Keyboard = Keyboard.Telephone };
        var addressEditor = new Editor { Text = candidate.Address, Placeholder = "Adres", AutoSize = EditorAutoSizeOption.TextChanges, HeightRequest = 100 };
        var result = new TaskCompletionSource<(string Phone, string Address)?>();
        var cancelButton = new Button { Text = "İptal", BackgroundColor = Color.FromArgb("#E2E8F0"), TextColor = Color.FromArgb("#172033") };
        var saveButton = new Button { Text = "Kaydet", BackgroundColor = Color.FromArgb("#4338CA"), TextColor = Colors.White };
        var modal = new ContentPage
        {
            Title = "Gönderi bilgileri",
            BackgroundColor = Color.FromArgb("#F4F6FA"),
            Content = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Padding = 20,
                    Spacing = 12,
                    Children =
                    {
                        new Label { Text = "Gönderi bilgilerini inceleyin", FontSize = 21, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#172033") },
                        new Label { Text = $"Kişi: {candidate.PersonName}\nKimlik: {candidate.Tc}", FontSize = 15, TextColor = Color.FromArgb("#334155") },
                        new Label { Text = "Telefon (düzenlenebilir)", FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#334155") },
                        phoneEntry,
                        new Label { Text = "Adres (düzenlenebilir)", FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#334155") },
                        addressEditor,
                        new Label { Text = $"Tur: {candidate.TourSummary}", FontSize = 14, TextColor = Color.FromArgb("#475569") },
                        new HorizontalStackLayout
                        {
                            Spacing = 10,
                            HorizontalOptions = LayoutOptions.End,
                            Children =
                            {
                                cancelButton,
                                saveButton
                            }
                        }
                    }
                }
            }
        };
        cancelButton.Clicked += async (_, _) =>
        {
            result.TrySetResult(null);
            await Navigation.PopModalAsync();
        };
        saveButton.Clicked += async (_, _) =>
        {
            result.TrySetResult((phoneEntry.Text?.Trim() ?? string.Empty, addressEditor.Text?.Trim() ?? string.Empty));
            await Navigation.PopModalAsync();
        };
        await Navigation.PushModalAsync(new NavigationPage(modal));
        return await result.Task;
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

            var periodFilter = await SelectQueueFilterAsync("Dönem", items.Select(item => item.Period));
            if (periodFilter is null) return;
            var daysFilter = await SelectQueueFilterAsync("Grup süresi", items.Select(item => item.DaysCount));
            if (daysFilter is null) return;
            var tourFilter = await SelectQueueFilterAsync("Tur türü", items.Select(item => item.TourType));
            if (tourFilter is null) return;
            var groupFilter = await SelectQueueFilterAsync("Grup türü", items.Select(item => item.GroupType));
            if (groupFilter is null) return;

            var filteredItems = items.Where(item =>
                MatchesQueueFilter(item.Period, periodFilter)
                && MatchesQueueFilter(item.DaysCount, daysFilter)
                && MatchesQueueFilter(item.TourType, tourFilter)
                && MatchesQueueFilter(item.GroupType, groupFilter)).ToList();
            if (filteredItems.Count == 0)
            {
                await DisplayAlertAsync("Kayıt bulunamadı", "Seçilen filtrelerle eşleşen kargo kaydı bulunamadı.", "Tamam");
                return;
            }

            var bytes = _export.CreateCargoLabelsPdf(filteredItems);
            var fileName = $"Kargo_Havuzu_{DateTime.Now:yyyyMMdd_HHmm}.pdf";
            await _downloads.SaveAsync(fileName, "application/pdf", bytes);
            await DisplayAlertAsync("İndirme tamamlandı", $"Kargo PDF dosyası İndirilenler klasörüne kaydedildi:\n{fileName}", "Tamam");
        }
        catch (Exception ex) { await DisplayAlertAsync("PDF indirilemedi", ex.Message, "Tamam"); }
    }

    private async Task<QueueFilterChoice?> SelectQueueFilterAsync(string filterName, IEnumerable<string> values)
    {
        var sourceValues = values.ToList();
        var choices = new List<QueueFilterChoice> { new("Tümü", string.Empty, IsAll: true) };
        foreach (var value in sourceValues
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase))
        {
            var label = value is "Tümü" or "Belirtilmemiş" or "Vazgeç" ? $"{value} (değer)" : value;
            choices.Add(new QueueFilterChoice(label, value));
        }

        if (sourceValues.Any(string.IsNullOrWhiteSpace))
            choices.Add(new QueueFilterChoice("Belirtilmemiş", string.Empty, IsUnspecified: true));

        var selectedLabel = await DisplayActionSheetAsync(
            $"PDF için {filterName} seçin",
            "Vazgeç",
            null,
            choices.Select(choice => choice.Label).ToArray());
        return choices.FirstOrDefault(choice => choice.Label == selectedLabel);
    }

    private static bool MatchesQueueFilter(string value, QueueFilterChoice filter) =>
        filter.IsAll
        || (filter.IsUnspecified
            ? string.IsNullOrWhiteSpace(value)
            : string.Equals(value, filter.Value, StringComparison.CurrentCultureIgnoreCase));
}

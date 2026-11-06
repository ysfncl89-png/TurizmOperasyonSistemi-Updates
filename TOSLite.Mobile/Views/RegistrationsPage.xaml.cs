using TOSLite.Mobile.Data;
using TOSLite.Mobile.Models;

namespace TOSLite.Mobile.Views;

public partial class RegistrationsPage : ContentPage
{
    private readonly RegistrationRepository _repository;
    private readonly SettingsRepository _settingsRepository;
    private List<RegistrationRecord> _registrations = [];
    private RegistrationRecord? _selectedRegistration;
    private bool _initializingPeriod;

    public RegistrationsPage(RegistrationRepository repository, SettingsRepository settingsRepository)
    {
        InitializeComponent();
        _repository = repository;
        _settingsRepository = settingsRepository;
        MobileTheme.Apply(this);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _repository.InitializeAsync();
            await InitializePeriodPickerAsync();
            await RefreshRegistrationsAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Hata", $"Tur kayıtları yüklenemedi: {ex.Message}", "Tamam");
        }
    }

    private async Task InitializePeriodPickerAsync()
    {
        _initializingPeriod = true;
        try
        {
            var periods = (await _settingsRepository.GetOptionsAsync("Dönem", includeInactive: false))
                .Select(option => option.Value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var options = new List<string> { "Tüm dönemler" };
            options.AddRange(periods);
            PeriodPicker.ItemsSource = options;

            var currentPeriod = DateTime.Now.ToString("MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
            var selectedIndex = options.FindIndex(option => string.Equals(option, currentPeriod, StringComparison.CurrentCultureIgnoreCase));
            PeriodPicker.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
        }
        finally
        {
            _initializingPeriod = false;
        }
    }

    private async Task RefreshRegistrationsAsync()
    {
        _registrations = (await _repository.GetAllAsync()).ToList();
        ApplySearch();
    }

    private void ApplySearch()
    {
        var query = SearchEntry.Text?.Trim() ?? string.Empty;
        var selectedPeriod = PeriodPicker.SelectedItem as string;
        IEnumerable<RegistrationRecord> filtered = _registrations;
        if (!string.IsNullOrWhiteSpace(selectedPeriod) && selectedPeriod != "Tüm dönemler")
        {
            filtered = filtered.Where(x => string.Equals(x.Period, selectedPeriod, StringComparison.CurrentCultureIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(x =>
                x.PersonName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || x.TourType.Contains(query, StringComparison.OrdinalIgnoreCase)
                || x.Period.Contains(query, StringComparison.OrdinalIgnoreCase)
                || x.GroupType.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var results = filtered.ToList();
        RegistrationsCollection.ItemsSource = results;
        StatusLabel.Text = string.IsNullOrWhiteSpace(query)
            ? $"Kayıtlı tur: {results.Count}"
            : $"Arama sonucu: {results.Count}";
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e) => ApplySearch();

    private void OnPeriodChanged(object? sender, EventArgs e)
    {
        if (!_initializingPeriod) ApplySearch();
    }

    private void OnRegistrationSelected(object? sender, SelectionChangedEventArgs e)
    {
        _selectedRegistration = e.CurrentSelection.FirstOrDefault() as RegistrationRecord;
        SelectedActions.IsVisible = _selectedRegistration != null;
    }

    private async void OnNewClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(RegistrationFormPage));
    }

    private async void OnEditClicked(object? sender, EventArgs e)
    {
        if (_selectedRegistration == null) return;
        await Shell.Current.GoToAsync($"{nameof(RegistrationFormPage)}?registrationId={_selectedRegistration.Id}");
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (_selectedRegistration == null) return;
        var confirmed = await DisplayAlertAsync("Tur kaydını sil", "Seçilen tur kaydını cihazdaki veritabanından silmek istiyor musunuz?", "Sil", "Vazgeç");
        if (!confirmed) return;

        try
        {
            await _repository.DeleteAsync(_selectedRegistration.Id);
            _selectedRegistration = null;
            RegistrationsCollection.SelectedItem = null;
            SelectedActions.IsVisible = false;
            await RefreshRegistrationsAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Hata", ex.Message, "Tamam");
        }
    }

    private async void OnHomeClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}

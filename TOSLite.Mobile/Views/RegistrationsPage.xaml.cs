using TOSLite.Mobile.Data;
using TOSLite.Mobile.Models;

namespace TOSLite.Mobile.Views;

public partial class RegistrationsPage : ContentPage
{
    private readonly RegistrationRepository _repository;
    private List<RegistrationRecord> _registrations = [];
    private RegistrationRecord? _selectedRegistration;

    public RegistrationsPage(RegistrationRepository repository)
    {
        InitializeComponent();
        _repository = repository;
        MobileTheme.Apply(this);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _repository.InitializeAsync();
            await RefreshRegistrationsAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Hata", $"Tur kayıtları yüklenemedi: {ex.Message}", "Tamam");
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
        RegistrationsCollection.ItemsSource = string.IsNullOrWhiteSpace(query)
            ? _registrations
            : _registrations.Where(x =>
                x.PersonName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || x.TourType.Contains(query, StringComparison.OrdinalIgnoreCase)
                || x.Period.Contains(query, StringComparison.OrdinalIgnoreCase)
                || x.GroupType.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        StatusLabel.Text = string.IsNullOrWhiteSpace(query)
            ? $"Kayıtlı tur: {_registrations.Count}"
            : $"Arama sonucu: {RegistrationsCollection.ItemsSource.Cast<RegistrationRecord>().Count()}";
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e) => ApplySearch();

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

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        _ = RefreshRegistrationsAsync();
    }
}

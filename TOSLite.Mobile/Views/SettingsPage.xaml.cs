using TOSLite.Mobile.Data;
using TOSLite.Mobile.Models;

namespace TOSLite.Mobile.Views;

public partial class SettingsPage : ContentPage
{
    private static readonly (string Title, string Type)[] Categories =
    [
        ("Cinsiyet", "Cinsiyet"),
        ("Yaş Aralığı", "Yaş Aralığı"),
        ("Kan Grubu", "Kan Grubu"),
        ("Pasaport Türü", "Pasaport Türü"),
        ("Uyruk", "Uyruk"),
        ("Pasaportu Geldi mi ?", "Pasaportu Geldi mi ?"),
        ("Bilet Durumu", "Bilet Durumu"),
        ("Tur Türü", "TourType"),
        ("Dönem", "Dönem"),
        ("Grup Türü", "GroupType"),
        ("Kaç Günlük Grup", "Kaç Günlük Grup"),
        ("Vize Talebi", "Vize Talebi"),
        ("Vize Durumu", "Vize Durumu"),
        ("Bilet Talebi", "Bilet Talebi"),
        ("Kargo Talebi", "Kargo Talebi"),
        ("Oda Türü", "RoomType"),
        ("Otel Ekle", "Otel Ekle"),
        ("Katılım Yapılacak Şehir", "Katılım Yapılacak Şehir"),
        ("Aktarma Şehri", "Aktarma Şehri"),
        ("Aktarma Var mı?", "Aktarma Var mı?"),
        ("Para Birimi", "Para Birimi"),
        ("Ödeme Yöntemi", "Ödeme Yöntemi")
    ];

    private readonly SettingsRepository _repository;
    private SettingOption? _editingOption;
    private string? _selectedType;
    private string? _selectedTitle;
    private List<SettingOption> _currentOptions = [];

    public SettingsPage(SettingsRepository repository)
    {
        InitializeComponent();
        _repository = repository;
        MobileTheme.Apply(this);
        BuildCards();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _repository.InitializeAsync();
        if (_selectedType != null) await LoadOptionsAsync();
    }

    private void BuildCards()
    {
        var query = SearchEntry?.Text?.Trim() ?? string.Empty;
        SettingsCards.Children.Clear();
        foreach (var category in Categories.Where(x => string.IsNullOrWhiteSpace(query) || x.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
        {
            var isSelected = _selectedType == CanonicalType(category.Type);
            var button = new Button
            {
                Text = isSelected ? $"✓  {category.Title}" : $"{category.Title}  ›",
                HeightRequest = 52,
                HorizontalOptions = LayoutOptions.Fill,
                Margin = new Thickness(0, 2),
                CornerRadius = 12,
                BackgroundColor = isSelected ? Color.FromArgb("#EEF2FF") : Colors.White,
                BorderColor = isSelected ? Color.FromArgb("#C7D2FE") : Color.FromArgb("#E2E8F0"),
                BorderWidth = 1,
                TextColor = isSelected ? Color.FromArgb("#3730A3") : Color.FromArgb("#0F172A"),
                FontAttributes = FontAttributes.Bold,
                CommandParameter = category.Type
            };
            button.Clicked += OnCategoryClicked;
            SettingsCards.Children.Add(button);
        }
        MobileTheme.Apply(this);
    }

    private async void OnCategoryClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string type }) return;
        _selectedType = CanonicalType(type);
        _selectedTitle = Categories.First(category => CanonicalType(category.Type) == _selectedType).Title;
        OptionsHeading.Text = _selectedTitle;
        ClearEditor();
        OptionsDialog.IsVisible = true;
        BuildCards();
        await LoadOptionsAsync();
    }

    private async Task LoadOptionsAsync()
    {
        if (_selectedType == null) return;
        _currentOptions = (await _repository.GetOptionsAsync(_selectedType)).ToList();
        OptionsCollection.ItemsSource = _currentOptions;
        var maxOrder = _currentOptions.Select(x => x.SortOrder).DefaultIfEmpty(0).Max();
        if (_editingOption == null) SortOrderEntry.Text = (maxOrder + 1).ToString();
        MobileTheme.Apply(this);
    }

    private async void OnSaveOptionClicked(object? sender, EventArgs e)
    {
        if (_selectedType == null) return;
        if (!int.TryParse(SortOrderEntry.Text, out var sortOrder) || sortOrder < 1)
        {
            await DisplayAlertAsync("Uyarı", "Sıra 1 veya daha büyük bir tam sayı olmalıdır.", "Tamam");
            return;
        }

        var option = _editingOption ?? new SettingOption { Type = _selectedType, IsActive = true };
        option.Value = ValueEntry.Text?.Trim() ?? string.Empty;
        option.SortOrder = sortOrder;
        option.IsActive = true;
        try
        {
            await _repository.SaveAsync(option);
            ClearEditor();
            await LoadOptionsAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Uyarı", ex.Message, "Tamam");
        }
    }

    private void OnEditOptionClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: SettingOption option }) return;
        _editingOption = option;
        ValueEntry.Text = option.Value;
        SortOrderEntry.Text = option.SortOrder.ToString();
        SaveOptionButton.Text = "Güncelle";
    }

    private async void OnToggleOptionClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: SettingOption option }) return;
        await _repository.SetActiveAsync(option.Id, !option.IsActive);
        await LoadOptionsAsync();
    }

    private async void OnDeleteOptionClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: SettingOption option }) return;
        var confirmed = await DisplayAlertAsync("Seçeneği sil", $"'{option.Value}' kalıcı olarak silinsin mi? Mevcut tur kayıtlarındaki metin değişmez.", "Sil", "Vazgeç");
        if (!confirmed) return;
        await _repository.DeleteAsync(option.Id);
        if (_editingOption?.Id == option.Id) ClearEditor();
        await LoadOptionsAsync();
    }

    private void OnClearClicked(object? sender, EventArgs e) => ClearEditor();

    private void OnCloseOptionsClicked(object? sender, EventArgs e)
    {
        OptionsDialog.IsVisible = false;
        ClearEditor();
    }

    private void ClearEditor()
    {
        _editingOption = null;
        ValueEntry.Text = string.Empty;
        SortOrderEntry.Text = (_currentOptions.Select(x => x.SortOrder).DefaultIfEmpty(0).Max() + 1).ToString();
        SaveOptionButton.Text = "Seçeneği ekle";
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e) => BuildCards();
    private async void OnHomeClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");

    private static string CanonicalType(string type) => type switch
    {
        "Tur Türü" => "TourType",
        "Grup Türü" => "GroupType",
        "Oda Türü" => "RoomType",
        _ => type
    };
}

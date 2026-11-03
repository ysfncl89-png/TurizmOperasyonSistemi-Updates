using System.Globalization;
using TOSLite.Mobile.Data;
using TOSLite.Mobile.Models;

namespace TOSLite.Mobile.Views;

[QueryProperty(nameof(RegistrationId), "registrationId")]
public partial class RegistrationFormPage : ContentPage
{
    private readonly RegistrationRepository _repository;
    private readonly PersonRepository _personRepository;
    private RegistrationRecord _record = new();
    private List<PersonRecord> _personItems = [];
    private List<string> _periodOptions = [];
    private List<string> _tourTypeOptions = [];
    private List<string> _groupTypeOptions = [];
    private List<string> _daysCountOptions = [];
    private List<string> _passportOptions = [];
    private List<string> _visaStatusOptions = [];
    private List<string> _ticketStatusOptions = [];
    private List<PersonRecord> _visiblePeople = [];
    private int? _selectedPersonId;
    private int _step = 1;
    private bool _initializing;
    private bool _formInitialized;
    private int _registrationId;

    public string RegistrationId
    {
        get => _registrationId.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (int.TryParse(value, CultureInfo.InvariantCulture, out var id))
            {
                _registrationId = id;
                if (IsLoaded) _ = LoadRegistrationAsync(id);
            }
        }
    }

    public RegistrationFormPage(RegistrationRepository repository, PersonRepository personRepository)
    {
        InitializeComponent();
        _repository = repository;
        _personRepository = personRepository;
        MobileTheme.Apply(this);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_formInitialized) return;
        try
        {
            _initializing = true;
            await _repository.InitializeAsync();
            await RefreshPeopleAsync();
            _tourTypeOptions = (await _repository.GetLookupOptionsAsync("Tur Türü")).ToList();
            TourTypePicker.ItemsSource = _tourTypeOptions;
            _periodOptions = (await _repository.GetLookupOptionsAsync("Dönem")).ToList();
            PeriodPicker.ItemsSource = _periodOptions;
            _groupTypeOptions = (await _repository.GetLookupOptionsAsync("Grup Türü")).ToList();
            GroupTypePicker.ItemsSource = _groupTypeOptions;
            _daysCountOptions = (await _repository.GetLookupOptionsAsync("Kaç Günlük Grup")).ToList();
            DaysCountPicker.ItemsSource = _daysCountOptions;
            await LoadPickerAsync(VisaRequestPicker, "Vize Talebi");
            await LoadPickerAsync(TicketRequestPicker, "Bilet Talebi");
            _visaStatusOptions = (await _repository.GetLookupOptionsAsync("Vize Durumu")).ToList();
            VisaStatusPicker.ItemsSource = _visaStatusOptions;
            _ticketStatusOptions = (await _repository.GetLookupOptionsAsync("Bilet Durumu")).ToList();
            TicketStatusPicker.ItemsSource = _ticketStatusOptions;
            await LoadPickerAsync(CargoPicker, "Kargo Talebi");
            await LoadPickerAsync(RoomTypePicker, "Oda Türü");
            await LoadPickerAsync(DepartureCityPicker, "Katılım Yapılacak Şehir");
            await LoadPickerAsync(TransferCityPicker, "Aktarma Şehri");
            await LoadPickerAsync(CurrencyPicker, "Para Birimi");
            TransferPicker.ItemsSource = new[] { "Var", "Yok" };
            _passportOptions = (await _repository.GetLookupOptionsAsync("Pasaportu Geldi mi ?")).ToList();
            PassportArrivedPicker.ItemsSource = _passportOptions;

            if (_registrationId > 0)
            {
                await LoadRegistrationAsync(_registrationId);
            }
            else
            {
                _record = new RegistrationRecord();
                SetPicker(TransferPicker, _record.HasTransfer);
                SetPicker(VisaRequestPicker, _record.VisaRequest);
                SetPicker(TicketRequestPicker, _record.TicketRequest);
                RefreshConditionalOptions();
            }

            _initializing = false;
            _formInitialized = true;
            SetStep(1);
        }
        catch (Exception ex)
        {
            _initializing = false;
            await DisplayAlertAsync("Hata", $"Tur kayıt formu yüklenemedi: {ex.Message}", "Tamam");
        }
    }

    private async Task RefreshPeopleAsync(int? selectPersonId = null)
    {
        _personItems = (await _personRepository.GetAllAsync()).Where(x => x.IsActive).ToList();
        if (selectPersonId.HasValue)
        {
            _selectedPersonId = _personItems.Any(x => x.Id == selectPersonId.Value) ? selectPersonId.Value : null;
        }
        ApplyPersonSearch();
        UpdateSelectedPersonLabel();
    }

    private void OnPersonSearchChanged(object? sender, TextChangedEventArgs e) => ApplyPersonSearch();

    private void ApplyPersonSearch()
    {
        if (PersonsCollection == null) return;
        var query = NormalizePersonSearch(PersonSearchEntry.Text);
        _visiblePeople = string.IsNullOrWhiteSpace(query)
            ? []
            : _personItems.Where(person =>
                NormalizePersonSearch($"{person.FirstName} {person.LastName}").Contains(query, StringComparison.Ordinal)
                || NormalizePersonSearch(person.TcKimlikNo).Contains(query, StringComparison.Ordinal)).ToList();
        PersonsCollection.ItemsSource = _visiblePeople;
        PersonSearchStatusLabel.Text = string.IsNullOrWhiteSpace(query)
            ? $"Kayıtlı aktif kişi: {_personItems.Count}. Bulmak için ad, soyad veya kimlik no yazın."
            : _visiblePeople.Count == 0
                ? "Kayıt bulunamadı. Kişi listede yoksa yeni kişi ekleyebilirsiniz."
                : $"{_visiblePeople.Count} kişi bulundu. Tur kaydı yapılacak kişiyi seçin.";

        if (_selectedPersonId.HasValue)
        {
            PersonsCollection.SelectedItem = _visiblePeople.FirstOrDefault(x => x.Id == _selectedPersonId.Value);
        }
    }

    private static string NormalizePersonSearch(string? value) =>
        new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private void OnPersonSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is PersonRecord person)
        {
            _selectedPersonId = person.Id;
            UpdateSelectedPersonLabel();
        }
    }

    private void UpdateSelectedPersonLabel()
    {
        var person = _personItems.FirstOrDefault(x => x.Id == _selectedPersonId);
        SelectedPersonLabel.IsVisible = person != null;
        SelectedPersonLabel.Text = person == null
            ? string.Empty
            : $"Seçilen kişi: {person.FirstName} {person.LastName} — {person.TcKimlikNo}";
    }

    private async void OnAddPersonClicked(object? sender, EventArgs e)
    {
        var personsPage = new PersonsPage(_personRepository);
        personsPage.OpenNewPersonForRegistration(PersonSearchEntry.Text);
        var saved = new TaskCompletionSource<PersonRecord?>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<PersonRecord>? handler = null;
        handler = (_, person) =>
        {
            personsPage.PersonSaved -= handler;
            saved.TrySetResult(person);
        };
        personsPage.PersonSaved += handler;
        personsPage.Disappearing += (_, _) => saved.TrySetResult(null);

        try
        {
            await Navigation.PushModalAsync(new NavigationPage(personsPage));
            var newPerson = await saved.Task;
            if (newPerson != null)
            {
                await Navigation.PopModalAsync();
                await RefreshPeopleAsync(newPerson.Id);
            }
        }
        catch (Exception ex)
        {
            personsPage.PersonSaved -= handler;
            await DisplayAlertAsync("Hata", $"Kişi ekleme ekranı açılamadı: {ex.Message}", "Tamam");
        }
    }

    private async Task LoadPickerAsync(Picker picker, string type)
    {
        picker.ItemsSource = (await _repository.GetLookupOptionsAsync(type)).ToList();
    }

    private async Task LoadRegistrationAsync(int id)
    {
        var loaded = await _repository.GetByIdAsync(id);
        if (loaded == null)
        {
            await DisplayAlertAsync("Hata", "Tur kaydı bulunamadı.", "Tamam");
            return;
        }

        _record = loaded;
        if (!_periodOptions.Contains(loaded.Period, StringComparer.OrdinalIgnoreCase))
        {
            _periodOptions.Add(loaded.Period);
            PeriodPicker.ItemsSource = _periodOptions.ToList();
        }
        _selectedPersonId = _personItems.Any(x => x.Id == loaded.PersonId) ? loaded.PersonId : null;
        ApplyPersonSearch();
        UpdateSelectedPersonLabel();
        SetPicker(TourTypePicker, loaded.TourType);
        SetPicker(PeriodPicker, loaded.Period);
        SetPicker(GroupTypePicker, loaded.GroupType);
        SetPicker(DaysCountPicker, loaded.DaysCount);
        SetPicker(VisaRequestPicker, loaded.VisaRequest);
        SetPicker(TicketRequestPicker, loaded.TicketRequest);
        SetPicker(CargoPicker, loaded.CargoRequest);
        SetPicker(RoomTypePicker, loaded.RoomType);
        SetPicker(DepartureCityPicker, loaded.DepartureCity);
        SetPicker(TransferPicker, loaded.HasTransfer);
        SetPicker(TransferCityPicker, loaded.TransferCity);
        SetPicker(CurrencyPicker, loaded.Currency);
        DistrictEntry.Text = loaded.District == "Belirtilmedi" ? string.Empty : loaded.District;
        TotalPriceEntry.Text = loaded.TotalPrice.ToString(CultureInfo.CurrentCulture);
        PaidAmountEntry.Text = loaded.PaidAmount.ToString(CultureInfo.CurrentCulture);
        RemainingDebtEntry.Text = loaded.RemainingDebt.ToString(CultureInfo.CurrentCulture);
        ReferenceEntry.Text = loaded.Reference == "Belirtilmedi" ? string.Empty : loaded.Reference;
        ActiveSwitch.IsToggled = loaded.IsActive;
        SetPicker(PassportArrivedPicker, loaded.PassportArrivedValue);
        if (string.IsNullOrWhiteSpace(loaded.PassportArrivedValue))
            SetOptionalPicker(PassportArrivedPicker, loaded.PassportArrived);
        RefreshConditionalOptions();
        SetPicker(VisaStatusPicker, loaded.VisaStatusChoice);
        SetPicker(TicketStatusPicker, loaded.TicketStatusChoice);
    }

    private static void SetPicker(Picker picker, string? value)
    {
        var items = picker.ItemsSource is System.Collections.IEnumerable source
            ? source.Cast<object>().Select(x => x.ToString() ?? string.Empty).ToList()
            : [];
        var index = string.IsNullOrWhiteSpace(value) ? -1 : items.FindIndex(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
        if (index < 0 && !string.IsNullOrWhiteSpace(value))
        {
            items.Add(value);
            picker.ItemsSource = items;
            index = items.Count - 1;
        }

        picker.SelectedIndex = index;
    }

    private void SetOptionalPicker(Picker picker, int? value)
    {
        if (!value.HasValue) return;
        var items = picker.ItemsSource?.Cast<string>().ToList() ?? [];
        var selected = value.Value switch { 1 => items.FindIndex(x => x.Contains("evet", StringComparison.OrdinalIgnoreCase)), 0 => items.FindIndex(x => x.Contains("hay", StringComparison.OrdinalIgnoreCase)), _ => items.FindIndex(x => x.Contains("verm", StringComparison.OrdinalIgnoreCase)) };
        picker.SelectedIndex = selected;
    }

    private void RefreshConditionalOptions()
    {
        var visaRequest = VisaRequestPicker.SelectedItem?.ToString() ?? string.Empty;
        var visa = _visaStatusOptions.ToList();
        var passport = _passportOptions.ToList();
        if (Normalize(visaRequest).Contains("kendisi"))
        {
            passport = passport.Where(x => Normalize(x).Contains("verm")).ToList();
            visa = visa.Where(x => Normalize(x).Contains("kendisi")).ToList();
        }
        else if (Normalize(visaRequest).Contains("alac") && Normalize(visaRequest).Contains("vize"))
        {
            passport = passport.Where(x => Normalize(x).Contains("evet") || Normalize(x).Contains("hay")).ToList();
            var passportValue = PassportArrivedPicker.SelectedItem?.ToString() ?? string.Empty;
            if (Normalize(passportValue).Contains("hay")) visa = visa.Where(x => Normalize(x).Contains("pasaport beklen")).ToList();
            else if (Normalize(passportValue).Contains("evet")) visa = visa.Where(x => Normalize(x).Contains("alin") || Normalize(x).Contains("cik")).ToList();
        }

        var selectedPassport = PassportArrivedPicker.SelectedItem?.ToString();
        var selectedVisa = VisaStatusPicker.SelectedItem?.ToString();
        PassportArrivedPicker.ItemsSource = passport;
        VisaStatusPicker.ItemsSource = visa;
        SetPicker(PassportArrivedPicker, selectedPassport);
        SetPicker(VisaStatusPicker, selectedVisa);

        if (Normalize(visaRequest).Contains("kendisi"))
        {
            SetPicker(PassportArrivedPicker, passport.FirstOrDefault(x => Normalize(x).Contains("verm")));
            SetPicker(VisaStatusPicker, visa.FirstOrDefault(x => Normalize(x).Contains("kendisi")));
        }
        else if (Normalize(visaRequest).Contains("alac"))
        {
            var passportValue = PassportArrivedPicker.SelectedItem?.ToString() ?? string.Empty;
            if (Normalize(passportValue).Contains("hay") && VisaStatusPicker.SelectedIndex < 0)
                SetPicker(VisaStatusPicker, visa.FirstOrDefault(x => Normalize(x).Contains("pasaport beklen")));
        }
    }

    private void OnVisaRulesChanged(object? sender, EventArgs e)
    {
        if (!_initializing) RefreshConditionalOptions();
    }

    private void OnTicketRequestChanged(object? sender, EventArgs e)
    {
        if (_initializing) return;
        var request = Normalize(TicketRequestPicker.SelectedItem?.ToString() ?? string.Empty);
        var all = _ticketStatusOptions;
        var options = request.Contains("kendisi")
            ? all.Where(x => Normalize(x).Contains("kendisi") || Normalize(x).Contains("kendi")).ToList()
            : request.Contains("alac")
                ? all.Where(x => Normalize(x).Contains("geld") || Normalize(x).Contains("gelm") || Normalize(x).Contains("alin") || Normalize(x).Contains("cikti")).ToList()
                : all;
        var current = TicketStatusPicker.SelectedItem?.ToString();
        TicketStatusPicker.ItemsSource = options;
        SetPicker(TicketStatusPicker, current);
        if (request.Contains("kendisi")) SetPicker(TicketStatusPicker, options.FirstOrDefault());
    }

    private void OnTransferChanged(object? sender, EventArgs e)
    {
        TransferFields.IsVisible = TransferPicker.SelectedItem?.ToString() == "Var";
        if (!TransferFields.IsVisible)
        {
            TransferCityPicker.SelectedIndex = -1;
        }
    }

    private void OnPaymentChanged(object? sender, TextChangedEventArgs e)
    {
        decimal.TryParse(TotalPriceEntry.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var total);
        decimal.TryParse(PaidAmountEntry.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var paid);
        RemainingDebtEntry.Text = (total - paid).ToString("N2", CultureInfo.CurrentCulture);
    }

    private async void OnNextClicked(object? sender, EventArgs e)
    {
        ValidationLabel.IsVisible = false;
        if (_step == 1 && !_personItems.Any(x => x.Id == _selectedPersonId && x.IsActive))
        {
            await ShowValidationAsync("Lütfen aktif bir kişi seçin.");
            return;
        }
        if (_step == 2 && (TourTypePicker.SelectedIndex < 0 || PeriodPicker.SelectedIndex < 0 || GroupTypePicker.SelectedIndex < 0 || DaysCountPicker.SelectedIndex < 0))
        {
            await ShowValidationAsync("Tur türü, dönem, grup türü ve grup süresi zorunludur.");
            return;
        }
        if (_step == 9)
        {
            if (await BuildPreviewAsync()) SetStep(10);
            return;
        }
        if (_step == 10)
        {
            await SaveAsync();
            return;
        }
        SetStep(_step + 1);
    }

    private async Task<bool> BuildPreviewAsync()
    {
        var parsed = TryReadAmounts(out var total, out var paid);
        if (!parsed)
        {
            await ShowValidationAsync("Fiyat ve ödenen tutar geçerli sayısal değer olmalıdır.");
            SetStep(8);
            return false;
        }
        if (paid < 0 || total < 0)
        {
            await ShowValidationAsync("Fiyat ve ödeme negatif olamaz.");
            SetStep(8);
            return false;
        }
        if (paid > total)
        {
            await ShowValidationAsync("Ödenen tutar toplam fiyattan büyük olamaz.");
            SetStep(8);
            return false;
        }

        _record.TotalPrice = total;
        _record.PaidAmount = paid;
        _record.RemainingDebt = total - paid;
        var person = _personItems.First(x => x.Id == _selectedPersonId);
        PreviewLabel.Text =
            $"KİŞİ\n{person.FirstName} {person.LastName}\n\n" +
            $"TUR VE GRUP\n{Selected(TourTypePicker)} — {Selected(PeriodPicker)}\n{Selected(GroupTypePicker)} — {Selected(DaysCountPicker)}\n\n" +
            $"VİZE / BİLET / KARGO\nVize: {Selected(VisaRequestPicker)} / {Selected(VisaStatusPicker)}\nPasaport: {Selected(PassportArrivedPicker)}\nBilet: {Selected(TicketRequestPicker)} / {Selected(TicketStatusPicker)}\nKargo: {Selected(CargoPicker)}\n\n" +
            $"ODA VE ULAŞIM\nOda: {Selected(RoomTypePicker)}\nKatılım şehri: {Selected(DepartureCityPicker)}\nAktarma: {Selected(TransferPicker)} {Selected(TransferCityPicker)}\nİlçe: {DistrictEntry.Text}\n\n" +
            $"ÖDEME\nToplam: {total:N2} {Selected(CurrencyPicker)}\nÖdenen: {paid:N2}\nKalan: {total - paid:N2}\n\n" +
            $"Referans: {ReferenceEntry.Text}";
        return true;
    }

    private async Task SaveAsync()
    {
        if (!_personItems.Any(x => x.Id == _selectedPersonId && x.IsActive) || TourTypePicker.SelectedIndex < 0 || PeriodPicker.SelectedIndex < 0 || GroupTypePicker.SelectedIndex < 0 || DaysCountPicker.SelectedIndex < 0)
        {
            await ShowValidationAsync("Kişi ve zorunlu tur alanları seçilmelidir.");
            SetStep(!_personItems.Any(x => x.Id == _selectedPersonId && x.IsActive) ? 1 : 2);
            return;
        }
        if (!TryReadAmounts(out var total, out var paid) || total < 0 || paid < 0 || paid > total)
        {
            await ShowValidationAsync("Fiyat ve ödeme değerlerini kontrol edin.");
            SetStep(8);
            return;
        }

        _record.PersonId = _selectedPersonId!.Value;
        _record.TourType = Selected(TourTypePicker);
        _record.Period = Selected(PeriodPicker);
        _record.GroupType = Selected(GroupTypePicker);
        _record.DaysCount = Selected(DaysCountPicker);
        if (await _repository.HasDuplicateAsync(_record))
        {
            await ShowValidationAsync("Bu kişi aynı tur, dönem, grup türü ve süre için zaten kayıtlı.");
            SetStep(2);
            return;
        }

        _record.VisaRequest = Selected(VisaRequestPicker, "Seçilmedi");
        _record.TicketRequest = Selected(TicketRequestPicker, "Seçilmedi");
        _record.VisaStatusChoice = Selected(VisaStatusPicker, "Seçilmedi");
        _record.TicketStatusChoice = Selected(TicketStatusPicker, "Seçilmedi");
        _record.PassportArrivedValue = Selected(PassportArrivedPicker, string.Empty);
        _record.PassportArrived = ParsePassportValue(_record.PassportArrivedValue);
        _record.CargoRequest = Selected(CargoPicker, "Seçilmedi");
        _record.RoomType = Selected(RoomTypePicker, "Seçilmedi");
        _record.DepartureCity = Selected(DepartureCityPicker, "Seçilmedi");
        _record.HasTransfer = Selected(TransferPicker, "Yok");
        _record.TransferCity = _record.HasTransfer == "Var" ? Selected(TransferCityPicker, "Belirtilmedi") : string.Empty;
        _record.District = _record.HasTransfer == "Var" && !string.IsNullOrWhiteSpace(DistrictEntry.Text) ? DistrictEntry.Text.Trim() : "Belirtilmedi";
        _record.TotalPrice = total;
        _record.Currency = Selected(CurrencyPicker, "Seçilmedi");
        _record.PaidAmount = paid;
        _record.RemainingDebt = total - paid;
        _record.Reference = string.IsNullOrWhiteSpace(ReferenceEntry.Text) ? "Belirtilmedi" : ReferenceEntry.Text.Trim();
        _record.IsActive = ActiveSwitch.IsToggled;
        _record.Status = _record.IsActive ? "Aktif" : "Pasif";

        try
        {
            await _repository.SaveAsync(_record);
            await DisplayAlertAsync("Bilgi", "Tur kaydı kaydedildi.", "Tamam");
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Hata", $"Tur kaydı kaydedilemedi: {ex.Message}", "Tamam");
        }
    }

    private bool TryReadAmounts(out decimal total, out decimal paid)
    {
        var totalOk = decimal.TryParse(TotalPriceEntry.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out total)
                      || decimal.TryParse(TotalPriceEntry.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out total);
        var paidOk = decimal.TryParse(PaidAmountEntry.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out paid)
                     || decimal.TryParse(PaidAmountEntry.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out paid);
        return totalOk && paidOk;
    }

    private static int? ParsePassportValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Contains("evet", StringComparison.OrdinalIgnoreCase)) return 1;
        if (value.Contains("hay", StringComparison.OrdinalIgnoreCase)) return 0;
        return -1;
    }

    private static string Selected(Picker picker, string fallback = "Seçilmedi") => picker.SelectedItem?.ToString() ?? fallback;
    private static string Normalize(string value) => value.Trim().ToLowerInvariant().Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c');

    private async Task ShowValidationAsync(string message)
    {
        ValidationLabel.Text = message;
        ValidationLabel.IsVisible = true;
        await DisplayAlertAsync("Uyarı", message, "Tamam");
    }

    private void SetStep(int step)
    {
        _step = Math.Clamp(step, 1, 10);
        var layouts = new[] { Step1Layout, Step2Layout, Step3Layout, Step4Layout, Step5Layout, Step6Layout, Step7Layout, Step8Layout, Step9Layout, Step10Layout };
        for (var index = 0; index < layouts.Length; index++) layouts[index].IsVisible = index == _step - 1;
        StepHeading.Text = $"Adım {_step} / 10";
        StepProgress.Progress = _step / 10d;
        BackButton.IsVisible = _step > 1;
        NextButton.Text = _step == 10 ? "Kaydet ve Bitir" : _step == 9 ? "Önizle" : "İleri";
    }

    private void OnPreviousClicked(object? sender, EventArgs e) => SetStep(_step - 1);
    private async void OnBackToListClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}

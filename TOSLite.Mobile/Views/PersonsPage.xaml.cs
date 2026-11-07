using System.Globalization;
using System.Text;
using TOSLite.Mobile.Data;
using TOSLite.Mobile.Models;
using TOSLite.Mobile.Services;

namespace TOSLite.Mobile.Views;

public partial class PersonsPage : ContentPage
{
    private readonly PersonRepository _repository;
    private readonly IPassportTextRecognitionService _passportTextRecognitionService;
    private List<PersonRecord> _people = [];
    private PersonRecord? _selectedPerson;
    private PersonRecord? _formPerson;
    private bool _loadingPerson;
    private bool _normalizingTc;
    private bool _isPassportProcessing;
    private int _currentStep = 1;
    private bool _openNewPersonOnAppear;
    private string _initialPersonSearch = string.Empty;
    private string? _passportImagePath;
    private PassportFormSnapshot? _passportOcrBaseline;

    private sealed record PassportFormSnapshot(
        string? FirstName,
        string? LastName,
        object? Nationality,
        string? TurkishId,
        object? Gender,
        bool BirthDateEnabled,
        DateTime? BirthDate,
        object? AgeRange,
        string? PassportNumber,
        bool PassportStartEnabled,
        DateTime? PassportStartDate,
        bool PassportEndEnabled,
        DateTime? PassportEndDate);

    public event EventHandler<PersonRecord>? PersonSaved;

    public void OpenNewPersonForRegistration(string? initialSearch)
    {
        _openNewPersonOnAppear = true;
        _initialPersonSearch = initialSearch?.Trim() ?? string.Empty;
    }

    public PersonsPage(PersonRepository repository, IPassportTextRecognitionService passportTextRecognitionService)
    {
        InitializeComponent();
        _repository = repository;
        _passportTextRecognitionService = passportTextRecognitionService;
        CapturePassportImageButton.IsVisible = MediaPicker.Default.IsCaptureSupported;
        MobileTheme.Apply(this);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        DeletePassportImage();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _repository.InitializeAsync();
            await LoadLookupOptionsAsync();
            await RefreshPeopleAsync();
            if (_openNewPersonOnAppear)
            {
                _openNewPersonOnAppear = false;
                OpenNewPersonWizard(_initialPersonSearch);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Hata", $"Kişi verileri yüklenemedi: {ex.Message}", "Tamam");
        }
    }

    private async Task LoadLookupOptionsAsync()
    {
        await LoadPickerAsync(GenderPicker, "Cinsiyet", includePlaceholder: true);
        await LoadPickerAsync(AgeRangePicker, "Yaş Aralığı", includePlaceholder: true);
        await LoadPickerAsync(BloodPicker, "Kan Grubu", includePlaceholder: true);
        await LoadPickerAsync(PassportTypePicker, "Pasaport Türü", includePlaceholder: true);
        await LoadPickerAsync(NationalityPicker, "Uyruk", includePlaceholder: false);
    }

    private async Task LoadPickerAsync(Picker picker, string type, bool includePlaceholder)
    {
        var values = (await _repository.GetLookupOptionsAsync(type)).ToList();
        if (includePlaceholder)
        {
            values.Insert(0, "Seçiniz");
        }

        picker.ItemsSource = values;
        picker.SelectedIndex = includePlaceholder ? 0 : -1;
    }

    private async Task RefreshPeopleAsync()
    {
        _people = (await _repository.GetAllAsync()).Where(person => person.IsActive && !person.IsDeleted).ToList();
        UpdateSearchResults();
    }

    private void UpdateSearchResults()
    {
        var query = Normalize(SearchEntry.Text);
        var matches = string.IsNullOrWhiteSpace(query)
            ? _people
            : _people.Where(person => MatchesSearch(person, query)).ToList();

        PeopleCollection.ItemsSource = matches;
        SelectedPersonActions.IsVisible = _selectedPerson != null && matches.Any(person => person.Id == _selectedPerson.Id);
        if (SelectedPersonActions.IsVisible && _selectedPerson != null)
        {
            SelectedPersonLabel.Text = $"{_selectedPerson.FirstName} {_selectedPerson.LastName}".Trim();
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            SearchStatusLabel.Text = $"Kayıtlı kişiler: {_people.Count}";
            NewPersonButton.IsVisible = false;
            return;
        }

        SearchStatusLabel.Text = matches.Count == 0
            ? "Kayıt bulunamadı. Yeni kişi kaydı oluşturabilirsiniz."
            : $"{matches.Count} kayıt bulundu. Düzenlemek veya silmek için kişiyi seçin.";

        var digitQuery = Digits(SearchEntry.Text);
        var tcMatch = digitQuery.Length >= 3 && matches.Any(person => Digits(person.TcKimlikNo).Contains(digitQuery, StringComparison.Ordinal));
        NewPersonButton.IsVisible = !tcMatch;
    }

    private static bool MatchesSearch(PersonRecord person, string query)
    {
        var firstName = Normalize(person.FirstName);
        var lastName = Normalize(person.LastName);
        var fullName = $"{firstName} {lastName}".Trim();
        var reversedName = $"{lastName} {firstName}".Trim();
        var tc = Normalize(person.TcKimlikNo);

        return fullName.Contains(query, StringComparison.Ordinal)
               || reversedName.Contains(query, StringComparison.Ordinal)
               || firstName.Contains(query, StringComparison.Ordinal)
               || lastName.Contains(query, StringComparison.Ordinal)
               || tc.Contains(query, StringComparison.Ordinal);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(" ", value.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Digits(string? value) =>
        new((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _selectedPerson = null;
        SelectedPersonActions.IsVisible = false;
        if (PeopleCollection != null)
        {
            PeopleCollection.SelectedItem = null;
        }

        UpdateSearchResults();
    }

    private void OnPersonSelected(object? sender, SelectionChangedEventArgs e)
    {
        var selectedPerson = e.CurrentSelection.FirstOrDefault() as PersonRecord;
        if (selectedPerson == null)
        {
            return;
        }

        _selectedPerson = selectedPerson;
        SelectedPersonActions.IsVisible = _selectedPerson != null;
        if (_selectedPerson != null)
        {
            SelectedPersonLabel.Text = $"{_selectedPerson.FirstName} {_selectedPerson.LastName}".Trim();
        }

        if (PeopleCollection != null)
        {
            PeopleCollection.SelectedItem = null;
        }
    }

    private void OnNewPersonClicked(object? sender, EventArgs e)
    {
        OpenNewPersonWizard();
    }

    private async void OnChoosePassportImageClicked(object? sender, EventArgs e)
    {
        try
        {
            var image = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Pasaport görseli seçin",
                FileTypes = FilePickerFileType.Images
            });
            if (image != null)
            {
                await SetPassportImageAsync(image);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Hata", $"Pasaport görseli açılamadı: {ex.Message}", "Tamam");
        }
    }

    private async void OnCapturePassportImageClicked(object? sender, EventArgs e)
    {
        try
        {
            var image = await MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions
            {
                Title = "Pasaport fotoğrafı"
            });
            if (image != null)
            {
                await SetPassportImageAsync(image);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Hata", $"Pasaport fotoğrafı çekilemedi: {ex.Message}", "Tamam");
        }
    }

    private async Task SetPassportImageAsync(FileResult image)
    {
        RestorePassportFormSnapshot();
        DeletePassportImage();
        var extension = Path.GetExtension(image.FileName);
        var imagePath = Path.Combine(FileSystem.CacheDirectory, $"passport-{Guid.NewGuid():N}{extension}");
        try
        {
            await using var source = await image.OpenReadAsync();
            await using var destination = File.Create(imagePath);
            await source.CopyToAsync(destination);
            await destination.FlushAsync();

            if (destination.Length == 0)
            {
                throw new InvalidDataException("Seçilen görsel boş.");
            }

            _passportImagePath = imagePath;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                PassportImageStatusLabel.Text = $"Görsel hazır: {image.FileName}";
                PassportImageStatusLabel.IsVisible = true;
                ProcessPassportImageButton.IsEnabled = true;
                ProcessPassportImageButton.Text = "Bilgileri İşle";
                ProcessPassportImageButton.BackgroundColor = Color.FromArgb("#6D4AFF");
                ProcessPassportImageButton.TextColor = Colors.White;
            });
        }
        catch
        {
            if (File.Exists(imagePath))
            {
                File.Delete(imagePath);
            }

            _passportImagePath = null;
            await MainThread.InvokeOnMainThreadAsync(() => ProcessPassportImageButton.IsEnabled = false);
            throw;
        }
    }

    private async void OnProcessPassportImageClicked(object? sender, EventArgs e)
    {
        if (_isPassportProcessing)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_passportImagePath))
        {
            await DisplayAlertAsync("Bilgi", "Önce pasaport görseli yükleyin veya fotoğraf çekin.", "Tamam");
            return;
        }

        if (!_passportTextRecognitionService.IsSupported)
        {
            await DisplayAlertAsync("Bilgi", "Bu cihazda yerel OCR desteği bulunmuyor. Bilgileri elle girebilirsiniz.", "Tamam");
            return;
        }

        _isPassportProcessing = true;
        ProcessPassportImageButton.Text = "İşleniyor...";
        ProcessPassportImageButton.BackgroundColor = Color.FromArgb("#4C35B5");
        try
        {
            var recognizedText = await _passportTextRecognitionService.RecognizeAsync(_passportImagePath);
            var extracted = PassportMrzParser.Parse(recognizedText);
            if (extracted == null)
            {
                await DisplayAlertAsync("Bilgi", "Pasaport MRZ satırları okunamadı. Görseli netleştirip tekrar deneyin veya bilgileri elle girin.", "Tamam");
                return;
            }

            if (_formPerson?.Id is not > 0
                && !string.IsNullOrWhiteSpace(extracted.TcKimlikNo)
                && string.Equals(extracted.NationalityCode, "TUR", StringComparison.OrdinalIgnoreCase))
            {
                var existingPerson = await _repository.FindUniqueByTurkishIdAsync(extracted.TcKimlikNo);
                if (existingPerson != null)
                {
                    var updateExisting = await DisplayAlertAsync(
                        "Kayıtlı kişi bulundu",
                        $"{existingPerson.FirstName} {existingPerson.LastName} adına kayıtlı kişi bulundu. Yeni pasaport bilgilerini bu kişinin kaydında güncellemek ister misiniz?",
                        "Güncelle",
                        "İptal");
                    if (!updateExisting)
                    {
                        return;
                    }

                    _formPerson = ClonePerson(existingPerson);
                    LoadPersonIntoForm(_formPerson);
                }
            }

            _passportOcrBaseline ??= CapturePassportFormSnapshot();
            ApplyPassportData(extracted);
            SetStep(5);
            await DisplayAlertAsync("Kontrol", "Okunan bilgiler önizlemeye aktarıldı. Kaydetmeden önce doğruluğunu kontrol edin.", "Tamam");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Hata", $"Pasaport işlenemedi: {ex.Message}", "Tamam");
        }
        finally
        {
            _isPassportProcessing = false;
            ProcessPassportImageButton.IsEnabled = !string.IsNullOrWhiteSpace(_passportImagePath);
            ProcessPassportImageButton.Text = "Bilgileri İşle";
            ProcessPassportImageButton.BackgroundColor = Color.FromArgb("#6D4AFF");
        }
    }

    private void ApplyPassportData(PassportExtractedData extracted)
    {
        if (!string.IsNullOrWhiteSpace(extracted.FirstName))
        {
            FirstNameEntry.Text = extracted.FirstName;
        }

        if (!string.IsNullOrWhiteSpace(extracted.LastName))
        {
            LastNameEntry.Text = extracted.LastName;
        }

        if (!string.IsNullOrWhiteSpace(extracted.DocumentNumber))
        {
            PassportNumberEntry.Text = extracted.DocumentNumber;
        }

        var nationality = FindNationalityOption(extracted.NationalityCode);
        if (nationality != null)
        {
            NationalityPicker.SelectedItem = nationality;
        }

        var gender = FindGenderOption(extracted.GenderCode);
        if (gender != null)
        {
            GenderPicker.SelectedItem = gender;
        }

        if (extracted.BirthDate.HasValue)
        {
            BirthDateEnabled.IsChecked = true;
            BirthDatePicker.Date = extracted.BirthDate.Value;
            SetAgeRangeFromBirthDate(extracted.BirthDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(extracted.TcKimlikNo) && GetNationalityMode() == NationalityMode.Turkey)
        {
            TcEntry.Text = extracted.TcKimlikNo;
        }

        if (extracted.PassportStartDate.HasValue)
        {
            PassportStartEnabled.IsChecked = true;
            PassportStartPicker.Date = extracted.PassportStartDate.Value;
        }

        if (extracted.PassportEndDate.HasValue)
        {
            PassportEndEnabled.IsChecked = true;
            PassportEndPicker.Date = extracted.PassportEndDate.Value;
        }

        PassportImageStatusLabel.Text = "Görsel cihazda işlendi. Bilgileri önizlemede kontrol edin.";
    }

    private string? FindNationalityOption(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var aliases = code.ToUpperInvariant() switch
        {
            "TUR" => new[] { "turkiye", "turk", "tc" },
            "USA" => new[] { "abd", "amerika", "unitedstates", "usa" },
            "GBR" => new[] { "ingiltere", "birlesikkrallik", "unitedkingdom", "gbr" },
            "DEU" => new[] { "almanya", "germany", "deu" },
            "FRA" => new[] { "fransa", "france", "fra" },
            "ITA" => new[] { "italya", "italy", "ita" },
            "ESP" => new[] { "ispanya", "spain", "esp" },
            "NLD" => new[] { "hollanda", "netherlands", "nld" },
            "RUS" => new[] { "rusya", "russia", "rus" },
            "UKR" => new[] { "ukrayna", "ukraine", "ukr" },
            "AZE" => new[] { "azerbaycan", "azerbaijan", "aze" },
            "IRN" => new[] { "iran", "irn" },
            "IRQ" => new[] { "irak", "iraq", "irq" },
            "SYR" => new[] { "suriye", "syria", "syr" },
            "GEO" => new[] { "gurcistan", "georgia", "geo" },
            "BGR" => new[] { "bulgaristan", "bulgaria", "bgr" },
            "ROU" => new[] { "romanya", "romania", "rou" },
            "POL" => new[] { "polonya", "poland", "pol" },
            "CAN" => new[] { "kanada", "canada", "can" },
            "AUS" => new[] { "avustralya", "australia", "aus" },
            "CHN" => new[] { "cin", "china", "chn" },
            "JPN" => new[] { "japonya", "japan", "jpn" },
            _ => new[] { code.ToLowerInvariant() }
        };

        var options = NationalityPicker.ItemsSource as IEnumerable<string> ?? [];
        var matchingOption = options.FirstOrDefault(option =>
        {
            var normalized = RemoveDiacritics(option).ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
            return aliases.Any(alias => normalized.Contains(alias, StringComparison.Ordinal));
        });
        if (matchingOption != null)
        {
            return matchingOption;
        }

        return code.Equals("TUR", StringComparison.OrdinalIgnoreCase)
            ? null
            : options.FirstOrDefault(option => RemoveDiacritics(option).Contains("yabanci", StringComparison.OrdinalIgnoreCase));
    }

    private string? FindGenderOption(string? code)
    {
        var expected = code?.Trim().ToUpperInvariant() switch
        {
            "M" => new[] { "erkek", "male", "m" },
            "F" => new[] { "kadin", "female", "f" },
            _ => null
        };
        if (expected == null)
        {
            return null;
        }

        var options = GenderPicker.ItemsSource as IEnumerable<string> ?? [];
        return options.FirstOrDefault(option =>
        {
            var normalized = RemoveDiacritics(option.Trim()).ToLowerInvariant();
            return expected.Any(value => normalized == value || normalized.Contains(value, StringComparison.Ordinal));
        });
    }

    private void DeletePassportImage()
    {
        if (string.IsNullOrWhiteSpace(_passportImagePath))
        {
            return;
        }

        try
        {
            File.Delete(_passportImagePath);
        }
        catch
        {
        }

        _passportImagePath = null;
    }

    private PassportFormSnapshot CapturePassportFormSnapshot() => new(
        FirstNameEntry.Text,
        LastNameEntry.Text,
        NationalityPicker.SelectedItem,
        TcEntry.Text,
        GenderPicker.SelectedItem,
        BirthDateEnabled.IsChecked,
        BirthDateEnabled.IsChecked ? BirthDatePicker.Date : null,
        AgeRangePicker.SelectedItem,
        PassportNumberEntry.Text,
        PassportStartEnabled.IsChecked,
        PassportStartEnabled.IsChecked ? PassportStartPicker.Date : null,
        PassportEndEnabled.IsChecked,
        PassportEndEnabled.IsChecked ? PassportEndPicker.Date : null);

    private void RestorePassportFormSnapshot()
    {
        if (_passportOcrBaseline is not { } baseline)
        {
            return;
        }

        _loadingPerson = true;
        try
        {
            FirstNameEntry.Text = baseline.FirstName;
            LastNameEntry.Text = baseline.LastName;
            NationalityPicker.SelectedItem = baseline.Nationality;
            TcEntry.Text = baseline.TurkishId;
            GenderPicker.SelectedItem = baseline.Gender;
            BirthDateEnabled.IsChecked = baseline.BirthDateEnabled;
            BirthDatePicker.Date = baseline.BirthDate ?? DateTime.Today;
            BirthDatePicker.IsVisible = baseline.BirthDateEnabled;
            AgeRangePicker.SelectedItem = baseline.AgeRange;
            PassportNumberEntry.Text = baseline.PassportNumber;
            PassportStartEnabled.IsChecked = baseline.PassportStartEnabled;
            PassportStartPicker.Date = baseline.PassportStartDate ?? DateTime.Today;
            PassportStartPicker.IsVisible = baseline.PassportStartEnabled;
            PassportEndEnabled.IsChecked = baseline.PassportEndEnabled;
            PassportEndPicker.Date = baseline.PassportEndDate ?? DateTime.Today;
            PassportEndPicker.IsVisible = baseline.PassportEndEnabled;
        }
        finally
        {
            _loadingPerson = false;
        }

        _passportOcrBaseline = null;
        UpdateNationalityFields();
        UpdateAgeLabel();
        UpdatePassportValidity();
    }

    private void OpenNewPersonWizard(string? initialName = null)
    {
        DeletePassportImage();
        _passportOcrBaseline = null;
        _formPerson = new PersonRecord { IsActive = true, BirthDate = DateTime.Now };
        ResetFormFields();
        if (!string.IsNullOrWhiteSpace(initialName))
        {
            FirstNameEntry.Text = initialName;
        }
        OpenWizard();
    }

    private void OnEditClicked(object? sender, EventArgs e)
    {
        if (_selectedPerson == null)
        {
            return;
        }

        _formPerson = ClonePerson(_selectedPerson);
        LoadPersonIntoForm(_formPerson);
        OpenWizard();
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (_selectedPerson == null)
        {
            return;
        }

        var fullName = $"{_selectedPerson.FirstName} {_selectedPerson.LastName}".Trim();
        var confirmed = await DisplayAlertAsync(
            "Kişiyi sil",
            $"{fullName} adlı kişiyi ve ilişkili tur/oda kayıtlarını kalıcı olarak silmek istiyor musunuz? Eşitleme sırasında masaüstünden de silinir.",
            "Sil",
            "Vazgeç");
        if (!confirmed)
        {
            return;
        }

        try
        {
            await _repository.DeleteAsync(_selectedPerson.Id);
            _selectedPerson = null;
            SearchEntry.Text = string.Empty;
            await RefreshPeopleAsync();
            await DisplayAlertAsync("Bilgi", "Kişi silinmek üzere işaretlendi. Eşitleme sırasında masaüstündeki ilişkili kayıtlarla birlikte silinecek.", "Tamam");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Hata", ex.Message, "Tamam");
        }
    }

    private async void OnHomeClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//home");
    }

    private void OpenWizard()
    {
        SearchPanel.IsVisible = false;
        WizardPanel.IsVisible = true;
        ((Button)FindByName("NewPersonResetButton")!).IsVisible = _formPerson?.Id is not > 0;
        ValidationLabel.IsVisible = false;
        SetStep(1);
    }

    private async void OnStartAnotherPersonClicked(object? sender, EventArgs e)
    {
        var confirmed = await DisplayAlertAsync(
            "Başka kişi ekle",
            "Bu kişi için girdiğiniz kaydedilmemiş bilgiler silinecek. Başka kişi için boş form açılsın mı?",
            "Evet, temizle",
            "Vazgeç");
        if (!confirmed)
        {
            return;
        }

        OpenNewPersonWizard();
    }

    private void CloseWizard(bool clearSearch)
    {
        DeletePassportImage();
        WizardPanel.IsVisible = false;
        SearchPanel.IsVisible = true;
        _formPerson = null;
        _currentStep = 1;
        if (clearSearch)
        {
            _selectedPerson = null;
            SearchEntry.Text = string.Empty;
        }

        UpdateSearchResults();
    }

    private void ResetFormFields()
    {
        _loadingPerson = true;
        try
        {
            FirstNameEntry.Text = string.Empty;
            LastNameEntry.Text = string.Empty;
            NationalityPicker.SelectedIndex = -1;
            TcEntry.Text = string.Empty;
            GenderPicker.SelectedIndex = 0;
            BirthDateEnabled.IsChecked = false;
            BirthDatePicker.Date = DateTime.Today;
            BirthDatePicker.IsVisible = false;
            AgeLabel.Text = string.Empty;
            AgeRangePicker.SelectedIndex = 0;
            PhoneEntry.Text = string.Empty;
            AddressEditor.Text = string.Empty;
            BloodPicker.SelectedIndex = 0;
            HealthEditor.Text = string.Empty;
            VaccineEditor.Text = string.Empty;
            PassportTypePicker.SelectedIndex = 0;
            PassportNumberEntry.Text = string.Empty;
            PassportStartEnabled.IsChecked = false;
            PassportStartPicker.Date = DateTime.Today;
            PassportStartPicker.IsVisible = false;
            PassportEndEnabled.IsChecked = false;
            PassportEndPicker.Date = DateTime.Today;
            PassportEndPicker.IsVisible = false;
            PassportValidityBorder.IsVisible = false;
            PassportImageStatusLabel.IsVisible = false;
            PassportImageStatusLabel.Text = string.Empty;
            ProcessPassportImageButton.IsEnabled = false;
        }
        finally
        {
            _loadingPerson = false;
        }

        UpdateNationalityFields();
    }

    private void LoadPersonIntoForm(PersonRecord person)
    {
        _loadingPerson = true;
        try
        {
            FirstNameEntry.Text = person.FirstName;
            LastNameEntry.Text = person.LastName;
            SetPickerValue(NationalityPicker, person.Nationality, includePlaceholder: false);
            TcEntry.Text = person.TcKimlikNo;
            SetPickerValue(GenderPicker, person.Gender, includePlaceholder: true);
            BirthDateEnabled.IsChecked = true;
            BirthDatePicker.Date = person.BirthDate.Date;
            BirthDatePicker.IsVisible = true;
            UpdateAgeLabel();
            SetPickerValue(AgeRangePicker, person.AgeRange, includePlaceholder: true);
            PhoneEntry.Text = person.PhoneNumber;
            AddressEditor.Text = person.Address;
            SetPickerValue(BloodPicker, person.BloodType, includePlaceholder: true);
            HealthEditor.Text = person.HealthStatus == "Belirtmedi" ? string.Empty : person.HealthStatus;
            VaccineEditor.Text = person.VaccineStatus == "Belirtmedi" ? string.Empty : person.VaccineStatus;
            SetPickerValue(PassportTypePicker, person.PassportType, includePlaceholder: true);
            PassportNumberEntry.Text = person.PassportNumber;
            PassportStartEnabled.IsChecked = person.PassportStartDate.HasValue;
            PassportStartPicker.Date = person.PassportStartDate?.Date ?? DateTime.Today;
            PassportStartPicker.IsVisible = person.PassportStartDate.HasValue;
            PassportEndEnabled.IsChecked = person.PassportEndDate.HasValue;
            PassportEndPicker.Date = person.PassportEndDate?.Date ?? DateTime.Today;
            PassportEndPicker.IsVisible = person.PassportEndDate.HasValue;
        }
        finally
        {
            _loadingPerson = false;
        }

        UpdateNationalityFields();
        UpdatePassportValidity();
    }

    private static PersonRecord ClonePerson(PersonRecord person) => new()
    {
        Id = person.Id,
        ClientId = person.ClientId,
        DesktopPersonId = person.DesktopPersonId,
        FirstName = person.FirstName,
        LastName = person.LastName,
        TcKimlikNo = person.TcKimlikNo,
        Gender = person.Gender,
        BirthDate = person.BirthDate,
        PhoneNumber = person.PhoneNumber,
        Address = person.Address,
        Aciklama = person.Aciklama,
        PassportType = person.PassportType,
        PassportNumber = person.PassportNumber,
        PassportStartDate = person.PassportStartDate,
        PassportEndDate = person.PassportEndDate,
        BloodType = person.BloodType,
        HealthStatus = person.HealthStatus,
        VaccineStatus = person.VaccineStatus,
        IsActive = person.IsActive,
        AgeRange = person.AgeRange,
        Nationality = person.Nationality
    };

    private static void SetPickerValue(Picker picker, string? value, bool includePlaceholder)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            picker.SelectedIndex = includePlaceholder ? 0 : -1;
            return;
        }

        var values = (picker.ItemsSource as IEnumerable<string>)?.ToList() ?? [];
        var index = values.FindIndex(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            values.Add(value);
            picker.ItemsSource = values;
            index = values.Count - 1;
        }

        picker.SelectedIndex = index;
    }

    private void OnNationalityChanged(object? sender, EventArgs e)
    {
        if (_loadingPerson)
        {
            return;
        }

        var mode = GetNationalityMode();
        if (mode == NationalityMode.Foreign)
        {
            if (_formPerson?.Id is not > 0)
            {
                TcEntry.Text = string.Empty;
            }
        }

        UpdateNationalityFields();
        ValidationLabel.IsVisible = false;
    }

    private void UpdateNationalityFields()
    {
        var mode = GetNationalityMode();
        TcEntry.IsEnabled = mode == NationalityMode.Turkey;
        TcEntry.Placeholder = mode == NationalityMode.Turkey
            ? "11 haneli TC Kimlik No"
            : "Türkiye uyruklular için";
    }

    private NationalityMode GetNationalityMode()
    {
        var nationality = NationalityPicker.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(nationality))
        {
            return NationalityMode.Unknown;
        }

        var normalized = RemoveDiacritics(nationality.Trim().ToLowerInvariant()).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (normalized == "tc" || normalized.Contains("turkiye", StringComparison.Ordinal) || normalized.Contains("turkey", StringComparison.Ordinal) || normalized.StartsWith("tur", StringComparison.Ordinal) || normalized.Contains("turk", StringComparison.Ordinal))
        {
            return NationalityMode.Turkey;
        }

        return NationalityMode.Foreign;
    }

    private static string RemoveDiacritics(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private void OnTcTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_normalizingTc || TcEntry == null)
        {
            return;
        }

        var digitsOnly = Digits(e.NewTextValue);
        if (!string.Equals(digitsOnly, e.NewTextValue, StringComparison.Ordinal))
        {
            _normalizingTc = true;
            TcEntry.Text = digitsOnly;
            _normalizingTc = false;
        }
    }

    private void OnBirthDateEnabledChanged(object? sender, CheckedChangedEventArgs e)
    {
        BirthDatePicker.IsVisible = e.Value;
        UpdateAgeLabel();
    }

    private void OnBirthDateSelected(object? sender, DateChangedEventArgs e)
    {
        UpdateAgeLabel();
        if (e.NewDate.HasValue)
        {
            SetAgeRangeFromBirthDate(e.NewDate.Value);
        }
    }

    private void SetAgeRangeFromBirthDate(DateTime birthDate)
    {
        if (birthDate.Date > DateTime.Today)
        {
            return;
        }

        var today = DateTime.Today;
        var age = today.Year - birthDate.Year;
        if (birthDate.Date > today.AddYears(-age))
        {
            age--;
        }

        var ageRange = age < 2 ? "Bebek" : age < 18 ? "Çocuk" : "Yetişkin";
        SetPickerValue(AgeRangePicker, ageRange, includePlaceholder: true);
    }

    private void UpdateAgeLabel()
    {
        if (!BirthDateEnabled.IsChecked)
        {
            AgeLabel.Text = string.Empty;
            return;
        }

        if (!BirthDatePicker.Date.HasValue)
        {
            AgeLabel.Text = string.Empty;
            return;
        }

        var birthDate = BirthDatePicker.Date.Value.Date;
        var age = DateTime.Today.Year - birthDate.Year;
        if (DateTime.Today.DayOfYear < birthDate.DayOfYear)
        {
            age--;
        }

        AgeLabel.Text = $"Yaş: {age}";
    }

    private void OnPassportDateEnabledChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (ReferenceEquals(sender, PassportStartEnabled))
        {
            PassportStartPicker.IsVisible = e.Value;
        }
        else if (ReferenceEquals(sender, PassportEndEnabled))
        {
            PassportEndPicker.IsVisible = e.Value;
        }

        UpdatePassportValidity();
    }

    private void OnPassportDateSelected(object? sender, DateChangedEventArgs e)
    {
        UpdatePassportValidity();
    }

    private void UpdatePassportValidity()
    {
        if (!PassportStartEnabled.IsChecked || !PassportEndEnabled.IsChecked)
        {
            PassportValidityBorder.IsVisible = false;
            PassportValidityLabel.Text = string.Empty;
            return;
        }

        PassportValidityBorder.IsVisible = true;
        var startDate = PassportStartPicker.Date?.Date;
        var endDate = PassportEndPicker.Date?.Date;
        if (!startDate.HasValue || !endDate.HasValue)
        {
            PassportValidityBorder.IsVisible = false;
            PassportValidityLabel.Text = string.Empty;
            return;
        }

        if (startDate.Value > endDate.Value)
        {
            PassportValidityBorder.Stroke = new SolidColorBrush(Color.FromArgb("#F87171"));
            PassportValidityLabel.TextColor = Color.FromArgb("#FECACA");
            PassportValidityLabel.Text = "Pasaport başlangıç tarihi, bitiş tarihinden sonra olamaz.";
            return;
        }

        if (endDate.Value < DateTime.Today)
        {
            PassportValidityBorder.Stroke = new SolidColorBrush(Color.FromArgb("#F87171"));
            PassportValidityLabel.TextColor = Color.FromArgb("#FECACA");
            PassportValidityLabel.Text = "Pasaportun geçerlilik süresi dolmuştur.";
            return;
        }

        var cursor = DateTime.Today;
        var years = 0;
        var months = 0;
        while (cursor.AddYears(1) <= endDate.Value)
        {
            cursor = cursor.AddYears(1);
            years++;
        }

        while (cursor.AddMonths(1) <= endDate.Value)
        {
            cursor = cursor.AddMonths(1);
            months++;
        }

        var days = (endDate.Value - cursor).Days;
        PassportValidityBorder.Stroke = new SolidColorBrush(Color.FromArgb("#22D3EE"));
        PassportValidityLabel.TextColor = Color.FromArgb("#67E8F9");
        PassportValidityLabel.Text = $"Pasaport geçerlilik süresi: {years} yıl, {months} ay, {days} gün kaldı.";
    }

    private void OnNextClicked(object? sender, EventArgs e)
    {
        if (_currentStep < 5)
        {
            SetStep(_currentStep + 1);
        }
    }

    private void OnBackClicked(object? sender, EventArgs e)
    {
        if (_currentStep > 1)
        {
            SetStep(_currentStep - 1);
            return;
        }

        CloseWizard(clearSearch: false);
    }

    private void SetStep(int step)
    {
        _currentStep = Math.Clamp(step, 1, 5);
        Step1Layout.IsVisible = _currentStep == 1;
        Step2Layout.IsVisible = _currentStep == 2;
        Step3Layout.IsVisible = _currentStep == 3;
        Step4Layout.IsVisible = _currentStep == 4;
        Step5Layout.IsVisible = _currentStep == 5;
        StepHeading.Text = $"Adım {_currentStep}/5";
        StepProgress.Progress = _currentStep / 5d;
        BackButton.IsVisible = true;
        NextButton.IsVisible = _currentStep < 5;
        SaveButton.IsVisible = _currentStep == 5;
        ValidationLabel.IsVisible = false;
        if (_currentStep == 5)
        {
            UpdatePreview();
        }
    }

    private void UpdatePreview()
    {
        var nationality = NationalityPicker.SelectedItem as string ?? string.Empty;
        var gender = GenderPicker.SelectedItem as string ?? "Seçiniz";
        var age = BirthDateEnabled.IsChecked ? GetAge(BirthDatePicker.Date) : string.Empty;
        var ageRange = AgeRangePicker.SelectedItem as string ?? "Seçiniz";
        var blood = BloodPicker.SelectedItem as string ?? "Seçiniz";
        var passportType = PassportTypePicker.SelectedItem as string ?? "Seçiniz";
        var passportValidity = GetPassportValiditySummary();

        var preview =
            $"AD SOYAD : {FirstNameEntry.Text} {LastNameEntry.Text}\n" +
            $"TC KİMLİK NO : {TcEntry.Text}\n" +
            $"UYRUK : {nationality}\n" +
            $"CİNSİYET : {gender}\n" +
            $"DOĞUM TARİHİ : {(BirthDateEnabled.IsChecked ? BirthDatePicker.Date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? string.Empty : string.Empty)}\n" +
            $"YAŞ : {age}\n" +
            $"YAŞ ARALIĞI : {ageRange}\n" +
            $"TELEFON : {PhoneEntry.Text}\n" +
            $"ADRES : {AddressEditor.Text}\n" +
            $"KAN GRUBU : {blood}\n" +
            $"HASTALIK DURUMU : {HealthEditor.Text}\n" +
            $"AŞI DURUMU : {VaccineEditor.Text}\n" +
            $"PASAPORT TÜRÜ : {passportType}\n" +
            $"PASAPORT NO : {PassportNumberEntry.Text}\n" +
            $"PASAPORT BAŞLANGIÇ : {(PassportStartEnabled.IsChecked ? PassportStartPicker.Date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? string.Empty : string.Empty)}\n" +
            $"PASAPORT BİTİŞ : {(PassportEndEnabled.IsChecked ? PassportEndPicker.Date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? string.Empty : string.Empty)}" +
            (string.IsNullOrWhiteSpace(passportValidity) ? string.Empty : $"\n{passportValidity}");
        var formattedPreview = new FormattedString();
        var previewLines = preview.Split('\n');
        for (var index = 0; index < previewLines.Length; index++)
        {
            var line = previewLines[index];
            var separator = line.IndexOf(" : ", StringComparison.Ordinal);
            if (separator >= 0)
            {
                formattedPreview.Spans.Add(new Span { Text = line[..(separator + 3)], FontAttributes = FontAttributes.Bold, TextColor = (Color)Application.Current!.Resources["PrimaryDark"] });
                formattedPreview.Spans.Add(new Span { Text = line[(separator + 3)..], TextColor = (Color)Application.Current.Resources["TextPrimary"] });
            }
            else
            {
                formattedPreview.Spans.Add(new Span { Text = line, FontAttributes = FontAttributes.Bold, TextColor = (Color)Application.Current!.Resources["Primary"] });
            }
            if (index < previewLines.Length - 1)
                formattedPreview.Spans.Add(new Span { Text = "\n" });
        }
        PreviewLabel.FormattedText = formattedPreview;
    }

    private string GetPassportValiditySummary()
    {
        if (!PassportEndEnabled.IsChecked || !PassportEndPicker.Date.HasValue)
        {
            return string.Empty;
        }

        var endDate = PassportEndPicker.Date.Value.Date;
        if (endDate < DateTime.Today)
        {
            return "PASAPORT GEÇERLİLİK : Süresi dolmuştur.";
        }

        var cursor = DateTime.Today;
        var years = 0;
        var months = 0;
        while (cursor.AddYears(1) <= endDate)
        {
            cursor = cursor.AddYears(1);
            years++;
        }

        while (cursor.AddMonths(1) <= endDate)
        {
            cursor = cursor.AddMonths(1);
            months++;
        }

        var days = (endDate - cursor).Days;
        return $"PASAPORT GEÇERLİLİK : {years} yıl, {months} ay, {days} gün kaldı.";
    }

    private static string GetAge(DateTime? birthDate)
    {
        if (!birthDate.HasValue)
        {
            return string.Empty;
        }

        var date = birthDate.Value;
        var age = DateTime.Today.Year - date.Year;
        if (DateTime.Today.DayOfYear < date.DayOfYear)
        {
            age--;
        }

        return age.ToString(CultureInfo.CurrentCulture);
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_formPerson == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(FirstNameEntry.Text)
            || string.IsNullOrWhiteSpace(LastNameEntry.Text)
            || NationalityPicker.SelectedIndex < 0
            || GenderPicker.SelectedIndex <= 0)
        {
            await ShowValidationErrorAsync("Ad, soyad, uyruk ve cinsiyet zorunludur.", step: 1);
            return;
        }

        var nationalityMode = GetNationalityMode();
        var tcKimlikNo = Digits(TcEntry.Text);
        if (nationalityMode == NationalityMode.Turkey && tcKimlikNo.Length != 11)
        {
            await ShowValidationErrorAsync("TC Kimlik No 11 haneli olmalıdır.", step: 1);
            return;
        }

        if (nationalityMode == NationalityMode.Foreign && string.IsNullOrWhiteSpace(PassportNumberEntry.Text))
        {
            await ShowValidationErrorAsync("Yabancı uyruklu kişiler için pasaport numarası zorunludur.", step: 4);
            return;
        }

        if (nationalityMode == NationalityMode.Turkey
            && await _repository.HasDuplicateTurkishIdAsync(tcKimlikNo, _formPerson.Id > 0 ? _formPerson.Id : null))
        {
            await ShowValidationErrorAsync("Bu TC zaten kayıtlı.", step: 1);
            return;
        }

        _formPerson.FirstName = FirstNameEntry.Text.Trim();
        _formPerson.LastName = LastNameEntry.Text.Trim();
        _formPerson.Nationality = NationalityPicker.SelectedItem as string;
        _formPerson.TcKimlikNo = nationalityMode == NationalityMode.Turkey ? tcKimlikNo : string.Empty;
        _formPerson.Gender = GenderPicker.SelectedItem as string ?? "Seçiniz";
        _formPerson.BirthDate = BirthDateEnabled.IsChecked ? BirthDatePicker.Date ?? DateTime.Now : DateTime.Now;
        _formPerson.AgeRange = AgeRangePicker.SelectedItem as string ?? "Seçiniz";
        _formPerson.PhoneNumber = PhoneEntry.Text ?? string.Empty;
        _formPerson.Address = AddressEditor.Text ?? string.Empty;
        _formPerson.BloodType = BloodPicker.SelectedIndex > 0 ? BloodPicker.SelectedItem as string ?? "Belirtmedi" : "Belirtmedi";
        _formPerson.HealthStatus = string.IsNullOrWhiteSpace(HealthEditor.Text) ? "Belirtmedi" : HealthEditor.Text;
        _formPerson.VaccineStatus = string.IsNullOrWhiteSpace(VaccineEditor.Text) ? "Belirtmedi" : VaccineEditor.Text;
        _formPerson.PassportType = PassportTypePicker.SelectedItem as string ?? "Seçiniz";
        _formPerson.PassportNumber = PassportNumberEntry.Text ?? string.Empty;
        _formPerson.PassportStartDate = PassportStartEnabled.IsChecked ? PassportStartPicker.Date : null;
        _formPerson.PassportEndDate = PassportEndEnabled.IsChecked ? PassportEndPicker.Date : null;

        try
        {
            var isNew = _formPerson.Id == 0;
            await _repository.SaveAsync(_formPerson);
            var savedPerson = ClonePerson(_formPerson);
            await DisplayAlertAsync("Bilgi", isNew ? "Kaydedildi" : "Güncellendi", "Tamam");
            CloseWizard(clearSearch: true);
            await RefreshPeopleAsync();
            if (isNew)
            {
                PersonSaved?.Invoke(this, savedPerson);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Hata", ex.Message, "Tamam");
        }
    }

    private async Task ShowValidationErrorAsync(string message, int step)
    {
        ValidationLabel.Text = message;
        ValidationLabel.IsVisible = true;
        SetStep(step);
        await DisplayAlertAsync("Uyarı", message, "Tamam");
    }

    private enum NationalityMode
    {
        Unknown,
        Turkey,
        Foreign
    }
}

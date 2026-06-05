using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Views
{
    public partial class PersonsWizardView : Page
    {
        // Eğer bu modül başka bir wizard içinde embed edildiyse ve
        // kayıt sonrası kullanıcıya küçük bir 'Kaydedildi' bildirimi
        // gösterilmesini istemiyorsak bu bayrağı true yapın.
        public bool SuppressSavedNotification { get; set; } = false;
        public event Action<Person> PersonSelectionChanged;
        public event Action<int> PersonSaved;

        private enum StepOneState
        {
            Initial,
            SearchResults,
            NewPerson,
            Editing
        }

        private string currentNationalityValue = string.Empty;

        private void LoadNationalityRadioOptions()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var options = db.AppSettings.Where(x => x.Type == "Uyruk" && x.IsActive).OrderBy(x => x.SortOrder).Select(x => x.Value).ToList();
                    var panel = FindName("NationalityOptionsPanel") as ItemsControl;
                    if (panel == null) return;

                    panel.Items.Clear();

                    foreach (var opt in options)
                    {
                        var rb = new RadioButton
                        {
                            Content = opt,
                            Margin = new Thickness(4, 0, 8, 0),
                            Tag = opt,
                            GroupName = "NationalityGroup"
                        };

                        rb.Checked += (s, e) =>
                        {
                            currentNationalityValue = (s as RadioButton)?.Tag as string ?? string.Empty;
                            ApplyLiveRequiredHighlights();
                        };

                        panel.Items.Add(rb);
                    }

                    // Eğer 'TC' seçeneği varsa varsayılan olarak seçili yapalım
                    var defaultRb = panel.Items.Cast<object>().OfType<RadioButton>().FirstOrDefault(r => string.Equals((r.Tag as string)?.Trim(), "TC", StringComparison.OrdinalIgnoreCase));
                    if (defaultRb != null)
                    {
                        defaultRb.IsChecked = true;
                        currentNationalityValue = defaultRb.Tag as string ?? string.Empty;
                        ApplyLiveRequiredHighlights();
                    }
                }
            }
            catch { }
        }

        // suppress recursion when we modify TcTextBox.Text programmatically
        private bool _suppressTcTextChanged = false;

        private bool RequiresTcForCurrentNationality()
        {
            return GetNationalityMode() == NationalityMode.Turkey;
        }

        private enum NationalityMode
        {
            Unknown,
            Turkey,
            Foreign
        }

        private NationalityMode GetNationalityMode()
        {
            var nat = !string.IsNullOrWhiteSpace(currentNationalityValue) ? currentNationalityValue : (NationalityComboBox?.Text ?? string.Empty);
            if (string.IsNullOrWhiteSpace(nat) || string.Equals(nat, "Seçiniz", StringComparison.OrdinalIgnoreCase))
                return NationalityMode.Unknown;

            // normalize and remove diacritics
            var s = nat.Trim().ToLowerInvariant();
            string normalized = RemoveDiacritics(s).Replace(" ", string.Empty);

            // recognize TC and Turkish variants
            if (normalized == "tc" || normalized.Contains("turkiye") || normalized.Contains("turkey") || normalized.StartsWith("tur") || normalized.Contains("turk"))
                return NationalityMode.Turkey;

            // recognize yabanci (yabancı) and foreign variants
            if (normalized.Contains("yabanci") || normalized.Contains("foreign"))
                return NationalityMode.Foreign;

            return NationalityMode.Foreign;
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var formD = text.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();
            foreach (var ch in formD)
            {
                var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
                if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
        }

        private void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(PersonsDataGrid.SelectedItem is Person person))
            {
                MessageBox.Show("Lütfen silmek için bir kişi seçin.");
                return;
            }

            var result = MessageBox.Show($"{person.FirstName} {person.LastName} adlı kişiyi silmek istediğinize emin misiniz?", "Onay", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                using (var db = new AppDbContext())
                {
                    var dbPerson = db.Persons.FirstOrDefault(x => x.Id == person.Id);
                    if (dbPerson != null)
                    {
                        db.Persons.Remove(dbPerson);
                        db.SaveChanges();
                    }
                }

                MessageBox.Show("Kişi silindi.");
                LoadPersons();
                ResetModuleToEntryState();
                UpdateSteps();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void NoMatchScrollTimer_Tick(object sender, EventArgs e)
        {
            _noMatchScrollTimer?.Stop();

            try
            {
                if (RootScrollViewer != null && FormPanel != null && SearchTextBox != null)
                {
                    var searchBottom = SearchTextBox.TranslatePoint(new Point(0, SearchTextBox.ActualHeight), RootScrollViewer).Y;
                    double offset = Math.Max(0, searchBottom - 10);
                    RootScrollViewer.ScrollToVerticalOffset(offset);
                }
            }
            catch
            {
                // ignore
            }
        }

        private readonly BlurEffect _panelBlur = new BlurEffect { Radius = 10 };

        private int currentStep = 1;
        private int selectedPersonId = 0;
        private Button ToggleActiveButton;
        private StepOneState _stepOneState = StepOneState.Initial;
        private ICollectionView _personsView;
        private System.Windows.Threading.DispatcherTimer _noMatchScrollTimer;

        public PersonsWizardView()
        {
            InitializeComponent();

            LoadComboBox(GenderComboBox, "Cinsiyet");
            LoadComboBox(AgeRangeComboBox, "Yaş Aralığı");
            LoadComboBox(BloodComboBox, "Kan Grubu");
            LoadComboBox(PassportTypeComboBox, "Pasaport Türü");
            // Uyruk combobox'u AppSettings'ten al
            LoadComboBox(NationalityComboBox, "Uyruk");

            // Ensure clicking the combo box surface (not only the arrow) opens the dropdown
            GenderComboBox.PreviewMouseLeftButtonDown += ComboBox_PreviewMouseLeftButtonDown;
            AgeRangeComboBox.PreviewMouseLeftButtonDown += ComboBox_PreviewMouseLeftButtonDown;
            BloodComboBox.PreviewMouseLeftButtonDown += ComboBox_PreviewMouseLeftButtonDown;
            PassportTypeComboBox.PreviewMouseLeftButtonDown += ComboBox_PreviewMouseLeftButtonDown;

            // Uyruk seçeneklerini radyo butonları olarak yükle
            LoadNationalityRadioOptions();

            LoadPersons();
            ResetModuleToEntryState();
            UpdateSteps();

            Loaded += PersonsWizardView_Loaded;
            Loaded += PersonsWizardView_PostLoad;
            SettingDialog.OnSettingsChanged += LoadNationalityRadioOptions;
            // subscribe to notifications from other modules (like Lists/Health edits)
            Services.NotificationCenter.Subscribe(OnNotification);
            Unloaded += (s, e) => Services.NotificationCenter.Unsubscribe(OnNotification);
            FirstNameTextBox.TextChanged += RequiredField_TextChanged;
            LastNameTextBox.TextChanged += RequiredField_TextChanged;
            TcTextBox.TextChanged += RequiredField_TextChanged;
            PassportTextBox.TextChanged += RequiredField_TextChanged;
            GenderComboBox.SelectionChanged += GenderComboBox_SelectionChanged;
            NationalityComboBox.PreviewMouseLeftButtonDown += ComboBox_PreviewMouseLeftButtonDown;
            NationalityComboBox.SelectionChanged += NationalityComboBox_SelectionChanged;
            ApplyLiveRequiredHighlights();

            _noMatchScrollTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(650)
            };
            _noMatchScrollTimer.Tick += NoMatchScrollTimer_Tick;
        }

        private void PersonsWizardView_PostLoad(object sender, RoutedEventArgs e)
        {
            // Arama sonucu yoksa formun üst kısmında uyarıyı göster
            EvaluateSearchState();
        }

        public int SelectedPersonId => selectedPersonId;

        public bool HasActiveSelectedPerson =>
            PersonsDataGrid.SelectedItem is Person person && person.IsActive;

        public void SelectPersonForExternalFlow(int personId)
        {
            if (personId <= 0)
            {
                return;
            }

            currentStep = 1;
            UpdateSteps();
            RestoreSelectedPerson(personId);

            if (PersonsDataGrid.SelectedItem is Person person)
            {
                SearchTextBox.Text = $"{person.FirstName} {person.LastName}".Trim();
                SetStepOneState(StepOneState.SearchResults);
                NotifyPersonSelectionChanged(person);
            }
        }

        private void PersonsWizardView_Loaded(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Focus();
        }

        private void OnNotification(string key, object payload)
        {
            if (key != "PersonUpdated") return;
            // payload expected to be person id
            int id = 0;
            if (payload is int i) id = i;
            else if (payload is long l) id = (int)l;

            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    LoadPersons();
                    if (id > 0)
                    {
                        RestoreSelectedPerson(id);
                        if (PersonsDataGrid.SelectedItem is Person person)
                        {
                            PopulateForm(person);
                        }
                    }
                });
            }
            catch
            {
                // ignore
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentStep < 5)
            {
                currentStep++;

                if (currentStep == 5)
                {
                    LoadPreview();
                }

                UpdateSteps();
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentStep > 1)
            {
                currentStep--;
                UpdateSteps();
            }
        }

        private void UpdateSteps()
        {
            Step1Grid.Visibility = Visibility.Collapsed;
            Step2Grid.Visibility = Visibility.Collapsed;
            Step3Grid.Visibility = Visibility.Collapsed;
            Step4Grid.Visibility = Visibility.Collapsed;
            Step5Grid.Visibility = Visibility.Collapsed;

            var passiveColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2937"));
            var activeColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#06B6D4"));

            Step1Border.Background = passiveColor;
            Step2Border.Background = passiveColor;
            Step3Border.Background = passiveColor;
            Step4Border.Background = passiveColor;
            Step5Border.Background = passiveColor;

            switch (currentStep)
            {
                case 1:
                    Step1Grid.Visibility = Visibility.Visible;
                    Step1Border.Background = activeColor;
                    break;
                case 2:
                    Step2Grid.Visibility = Visibility.Visible;
                    Step2Border.Background = activeColor;
                    break;
                case 3:
                    Step3Grid.Visibility = Visibility.Visible;
                    Step3Border.Background = activeColor;
                    break;
                case 4:
                    Step4Grid.Visibility = Visibility.Visible;
                    Step4Border.Background = activeColor;
                    break;
                case 5:
                    Step5Grid.Visibility = Visibility.Visible;
                    Step5Border.Background = activeColor;
                    break;
            }

            NextButton.Visibility = currentStep == 5 ? Visibility.Collapsed : Visibility.Visible;
            BackButton.IsEnabled = currentStep > 1;
            UpdateStepOneVisualState();
        }

        private void UpdateFooterState()
        {
            if (currentStep != 1)
            {
                NextButton.IsEnabled = true;
                return;
            }

            NextButton.IsEnabled =
                _stepOneState == StepOneState.NewPerson ||
                _stepOneState == StepOneState.Editing;
        }

        private void BirthDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BirthDatePicker.SelectedDate != null)
            {
                int age = DateTime.Now.Year - BirthDatePicker.SelectedDate.Value.Year;

                if (DateTime.Now.DayOfYear < BirthDatePicker.SelectedDate.Value.DayOfYear)
                {
                    age--;
                }

                AgeTextBox.Text = age.ToString();
            }
            else
            {
                AgeTextBox.Clear();
            }
        }

        private void LoadPersons()
        {
            using (var db = new AppDbContext())
            {
                // Eğer veritabanında Nationality sütunu eksikse, runtime'da ekle (kullanıcıya veritabanı işlemi yaptırmamak için)
                db.EnsureNationalityColumnExists();
                var list = db.Persons
                    .OrderByDescending(x => x.Id)
                    .ToList();

                PersonsDataGrid.ItemsSource = list;
                _personsView = CollectionViewSource.GetDefaultView(PersonsDataGrid.ItemsSource);
                _personsView.Filter = FilterPersons;
            }
        }

        private bool FilterPersons(object obj)
        {
            if (!(obj is Person person))
            {
                return false;
            }

            var text = Normalize(SearchTextBox.Text);

            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            var firstName = Normalize(person.FirstName);
            var lastName = Normalize(person.LastName);
            var fullName = $"{firstName} {lastName}".Trim();
            var fullNameReversed = $"{lastName} {firstName}".Trim();
            var tc = Normalize(person.TcKimlikNo);

            return fullName.Contains(text)
                   || fullNameReversed.Contains(text)
                   || firstName.Contains(text)
                   || lastName.Contains(text)
                   || tc.Contains(text);
        }

        private string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            return string.Join(" ",
                input
                    .Trim()
                    .ToLowerInvariant()
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private void LoadComboBox(ComboBox comboBox, string type)
        {
            using (var db = new AppDbContext())
            {
                var values = db.AppSettings
                    .Where(x => x.Type == type && x.IsActive)
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.Id)
                    .Select(x => x.Value)
                    .ToList();

                values.Insert(0, "Seçiniz");

                // Eğer XAML veya başka kod tarafından ComboBox.Items doldurulmuşsa, ItemsSource ayarlamadan önce temizle
                if (comboBox.Items != null && comboBox.Items.Count > 0)
                {
                    comboBox.Items.Clear();
                }

                comboBox.ItemsSource = values;
                if (values.Count > 0)
                    comboBox.SelectedIndex = 0;
            }
        }

        private void ClearRequiredHighlights()
        {
            // Eğer UI kontrolleri henüz oluşturulmadıysa çık
            if (FirstNameTextBox == null && LastNameTextBox == null && TcTextBox == null && PassportTextBox == null && GenderComboBox == null)
            {
                return;
            }
            FirstNameTextBox.Tag = null;
            LastNameTextBox.Tag = null;
            TcTextBox.Tag = null;
            PassportTextBox.Tag = null;
            GenderComboBox.Tag = null;
        }

        private void ApplyLiveRequiredHighlights()
        {
            // UI öğeleri mevcut değilse (ör. constructor sırasında) güvenli çıkış
            if (FirstNameTextBox == null)
            {
                return;
            }
            if (FirstNameTextBox != null) FirstNameTextBox.Tag = string.IsNullOrWhiteSpace(FirstNameTextBox.Text) ? "RequiredError" : null;
            if (LastNameTextBox != null) LastNameTextBox.Tag = string.IsNullOrWhiteSpace(LastNameTextBox.Text) ? "RequiredError" : null;
            if (GenderComboBox != null) GenderComboBox.Tag = GenderComboBox.SelectedIndex <= 0 ? "RequiredError" : null;

            // TC veya Pasaport zorunluluğunu uyruk durumuna göre ayarla
            var mode = GetNationalityMode();

            // Her iki alan da düzenlenebilir kalsın; sadece zorunluluk Tag'leri uyruk durumuna göre ayarlansın
            // TC yalnızca Türkiye seçiliyse düzenlenebilir; yabancıysa devre dışı bırakarak giriş engelle
            if (TcTextBox != null)
            {
                // TC kutusu sadece yabancıysa kapatılsın; unknown (seçiniz) ve Türkiye durumunda yazılabilir olsun
                TcTextBox.IsEnabled = mode != NationalityMode.Foreign;
                if (mode == NationalityMode.Turkey)
                {
                    TcTextBox.Tag = string.IsNullOrWhiteSpace(TcTextBox.Text) ? "RequiredError" : null;
                }
                else
                {
                    TcTextBox.Tag = null;
                }
            }

            // Pasaport her zaman düzenlenebilir olsun; fakat yabancıysa zorunlu etiketi konur
            if (PassportTextBox != null)
            {
                PassportTextBox.IsEnabled = true;
                if (mode == NationalityMode.Foreign)
                {
                    PassportTextBox.Tag = string.IsNullOrWhiteSpace(PassportTextBox.Text) ? "RequiredError" : null;
                }
                else
                {
                    PassportTextBox.Tag = null;
                }
            }
        }

        private bool HighlightMissingRequiredFields()
        {
            // Eğer UI oluşturulmadıysa hata yokmuş gibi davran
            if (FirstNameTextBox == null)
            {
                return false;
            }

            ApplyLiveRequiredHighlights();

            bool hasError = false;

            if (string.IsNullOrWhiteSpace(FirstNameTextBox.Text))
            {
                FirstNameTextBox.Tag = "RequiredError";
                hasError = true;
            }

            if (string.IsNullOrWhiteSpace(LastNameTextBox.Text))
            {
                LastNameTextBox.Tag = "RequiredError";
                hasError = true;
            }

            var mode = GetNationalityMode();
            if (mode == NationalityMode.Turkey)
            {
                if (TcTextBox == null || string.IsNullOrWhiteSpace(TcTextBox.Text))
                {
                    if (TcTextBox != null) TcTextBox.Tag = "RequiredError";
                    hasError = true;
                }
            }
            else if (mode == NationalityMode.Foreign)
            {
                if (PassportTextBox == null || string.IsNullOrWhiteSpace(PassportTextBox.Text))
                {
                    if (PassportTextBox != null) PassportTextBox.Tag = "RequiredError";
                    hasError = true;
                }
            }

            if (GenderComboBox == null || GenderComboBox.SelectedIndex <= 0 || string.Equals(GenderComboBox.Text, "Seçiniz", StringComparison.OrdinalIgnoreCase))
            {
                if (GenderComboBox != null) GenderComboBox.Tag = "RequiredError";
                hasError = true;
            }

            return hasError;
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (currentStep != 1)
            {
                currentStep = 1;
                UpdateSteps();
            }

            selectedPersonId = 0;
            PersonsDataGrid.SelectedItem = null;
            if (ToggleActiveButton != null)
            {
                ToggleActiveButton.Visibility = Visibility.Collapsed;
            }
            EditSelectedButton.Visibility = Visibility.Collapsed;

            _personsView?.Refresh();
            EvaluateSearchState();
        }

        private void EvaluateSearchState()
        {
            var hasSearchText = !string.IsNullOrWhiteSpace(Normalize(SearchTextBox.Text));
            var hasMatches = _personsView?.Cast<object>().Any() == true;

            if (!hasSearchText)
            {
                ClearForm();
                SetStepOneState(StepOneState.Initial);
                return;
            }

            if (hasMatches)
            {
                ClearForm();
                SetStepOneState(StepOneState.SearchResults);
                SearchStateTextBlock.Text = "Kayıt bulundu . Kişiyi düzenleyecek ya da tamamen silecekseniz listeden kişiyi seçin.";
                // Arama sonucu bulunduğunda "kayıt bulunamadı" uyarısını gizle
                NoPersonNoticeTextBlock.Visibility = Visibility.Collapsed;
                // Herhangi bir zamanlayıcı varsa durdur
                _noMatchScrollTimer?.Stop();
                return;
            }

            // Sonuç yok: direkt formu arama kutusunun hemen altına getir
            ClearForm();
            SetStepOneState(StepOneState.NewPerson);
            SearchStateTextBlock.Text = "Aradığınız kişi sistemde kayıtlı değil. Aşağıdaki formdan kişiyi kaydederek ilerleyin.";
            NoPersonNoticeTextBlock.Visibility = Visibility.Collapsed;
            // Artık otomatik kaydırma yapmak istemiyoruz; scroll'u sabit en yukarıya al
            _noMatchScrollTimer?.Stop();
            try
            {
                RootScrollViewer?.ScrollToVerticalOffset(0);
            }
            catch
            {
                // ignore
            }
        }

        private void SetStepOneState(StepOneState state)
        {
            _stepOneState = state;
            UpdateStepOneVisualState();
        }

        private void UpdateStepOneVisualState()
        {
            if (currentStep != 1)
            {
                UpdateFooterState();
                return;
            }

            switch (_stepOneState)
            {
                case StepOneState.Initial:
                    ResultsPanel.Visibility = Visibility.Visible;
                    ResultsPanel.Effect = _panelBlur;
                    ResultsOverlay.Visibility = Visibility.Visible;
                    ResultsOverlayTitle.Text = "Önce kişiyi arayın";
                    ResultsOverlayText.Text = "Arama kutusuna ad, soyad, ad soyad ya da TC kimlik yazın. Kayıt bulunduğunda sonuç listesi açılacak. Kayıt Yoksa Kayıt formu aktif olacak ";

                    FormPanel.Effect = _panelBlur;
                    FormOverlay.Visibility = Visibility.Visible;
                    FormOverlayTitle.Text = "Kayıt formu beklemede";
                    FormOverlayText.Text = "Önce yukarıdan arama yapın. Kayıt bulunamazsa bu form yeni kişi kaydı için açılacak.";
                    FormCaptionTextBlock.Text = "Yeni kişi kaydı oluşturmak ya da seçili kişiyi güncellemek için bu alanı kullanın.";
                    SearchStateTextBlock.Text = "Aramayı başlatın. Sistem tüm eşleşmeleri sonuçlarda gösterecek.Kişi kaydı yoksa kayıt formu otomatik açılacak.";
                    NoPersonNoticeTextBlock.Visibility = Visibility.Collapsed;
                    break;

                case StepOneState.SearchResults:
                    ResultsPanel.Visibility = Visibility.Visible;
                    ResultsPanel.Effect = null;
                    ResultsOverlay.Visibility = Visibility.Collapsed;

                    FormPanel.Effect = _panelBlur;
                    FormOverlay.Visibility = Visibility.Visible;
                    FormOverlayTitle.Text = "Kayıt bulundu";
                    FormOverlayText.Text = "Düzenleme yapmak istiyorsanız listeden kişiyi seçip Düzenle butonuna tıklayın. Kişiyi veri tabanınızdan kalıcı olarak silmek istiyorsanız sağdaki butonu kullanabilirsiniz.";
                    FormCaptionTextBlock.Text = "Seçili kaydı düzenlemek için önce listeden kişiyi seçip Düzenle butonuna tıklayın.";
                    NoPersonNoticeTextBlock.Visibility = Visibility.Collapsed;
                    break;

                case StepOneState.NewPerson:
                    // Sonuç panelini gizleyip formu doğrudan arama kutusunun altına taşıyalım
                    ResultsPanel.Visibility = Visibility.Collapsed;
                    ResultsOverlay.Visibility = Visibility.Collapsed;

                    FormPanel.Effect = null;
                    FormOverlay.Visibility = Visibility.Collapsed;
                    FormCaptionTextBlock.Text = "Bu kişi sistemde bulunamadı. Aşağıdaki alanları doldurup adımlarda ilerleyebilirsiniz.";
                    NoPersonNoticeTextBlock.Visibility = Visibility.Visible;
                    break;

                case StepOneState.Editing:
                    ResultsPanel.Visibility = Visibility.Visible;
                    ResultsPanel.Effect = null;
                    ResultsOverlay.Visibility = Visibility.Collapsed;

                    FormPanel.Effect = null;
                    FormOverlay.Visibility = Visibility.Collapsed;
                    FormCaptionTextBlock.Text = "Seçili kişi düzenleme modunda. Değişiklikleri yapıp son adımda güncelleyebilirsiniz.";
                    SearchStateTextBlock.Text = "Düzenleme modundasınız. Kişinin Kişisel bilgilerini adımlar arasında ilerleyerek güncelleyebilirsiniz.Veritabanından tamamen silmek isterseniz sil butonuna tıklayabilirsiniz";
                    NoPersonNoticeTextBlock.Visibility = Visibility.Collapsed;
                    break;
            }

            UpdateFooterState();
        }

        private void ClearForm()
        {
            ClearRequiredHighlights();

            FirstNameTextBox.Clear();
            LastNameTextBox.Clear();
            TcTextBox.Clear();
            PhoneTextBox.Clear();
            AddressTextBox.Clear();
            HealthTextBox.Clear();
            VaccineTextBox.Clear();
            PassportTextBox.Clear();
            AgeTextBox.Clear();

            GenderComboBox.SelectedIndex = 0;
            AgeRangeComboBox.SelectedIndex = 0;
            BloodComboBox.SelectedIndex = 0;
            PassportTypeComboBox.SelectedIndex = 0;

            BirthDatePicker.SelectedDate = null;
            PassportStartPicker.SelectedDate = null;
            PassportEndPicker.SelectedDate = null;

            selectedPersonId = 0;

            SaveButton.Content = "Kaydet";
            if (ToggleActiveButton != null)
            {
                ToggleActiveButton.Visibility = Visibility.Collapsed;
                ToggleActiveButton.Content = "Pasif Yap";
            }
            EditSelectedButton.Visibility = Visibility.Collapsed;
            ApplyLiveRequiredHighlights();
        }

        private void PopulateForm(Person person)
        {
            FirstNameTextBox.Text = person.FirstName;
            LastNameTextBox.Text = person.LastName;
            TcTextBox.Text = person.TcKimlikNo;
            PhoneTextBox.Text = person.PhoneNumber;
            AddressTextBox.Text = person.Address;
            HealthTextBox.Text = person.HealthStatus;
            VaccineTextBox.Text = person.VaccineStatus;
            PassportTextBox.Text = person.PassportNumber;

            BirthDatePicker.SelectedDate = person.BirthDate;
            PassportStartPicker.SelectedDate = person.PassportStartDate;
            PassportEndPicker.SelectedDate = person.PassportEndDate;

            GenderComboBox.Text = string.IsNullOrWhiteSpace(person.Gender) ? "Seçiniz" : person.Gender;
            AgeRangeComboBox.Text = string.IsNullOrWhiteSpace(person.AgeRange) ? "Seçiniz" : person.AgeRange;
            BloodComboBox.Text = string.IsNullOrWhiteSpace(person.BloodType) ? "Seçiniz" : person.BloodType;
            PassportTypeComboBox.Text = string.IsNullOrWhiteSpace(person.PassportType) ? "Seçiniz" : person.PassportType;
            NationalityComboBox.Text = string.IsNullOrWhiteSpace(person.Nationality) ? "Seçiniz" : person.Nationality;
        }

        private void PersonsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PersonsDataGrid.SelectedItem is Person person)
            {
                selectedPersonId = person.Id;
                PopulateForm(person);

                SaveButton.Content = "Güncelle";
                EditSelectedButton.Visibility = Visibility.Visible;
                DeleteSelectedButton.Visibility = Visibility.Visible;

                SearchStateTextBlock.Text = "Düzenle ile kaydı güncelleyebilir veya Sil Butonunu tıklayarak kişiyi sisteminizden tamamen silebilirsiniz";

                if (_stepOneState != StepOneState.Editing)
                {
                    SetStepOneState(StepOneState.SearchResults);
                }

                NotifyPersonSelectionChanged(person);
            }
            else
            {
                selectedPersonId = 0;
                if (ToggleActiveButton != null)
                {
                    ToggleActiveButton.Visibility = Visibility.Collapsed;
                }
                EditSelectedButton.Visibility = Visibility.Collapsed;
                DeleteSelectedButton.Visibility = Visibility.Collapsed;
                SaveButton.Content = "Kaydet";
                PersonSelectionChanged?.Invoke(null);

                if (_stepOneState == StepOneState.SearchResults)
                {
                    SearchStateTextBlock.Text = "Kayıt bulunduysa listeden kişiyi seçin. Pasif bir kişiyi de burada görebilir, aktif yapabilir veya Düzenle ile güncelleyebilirsiniz.";
                }
            }
        }

        private void EditSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(PersonsDataGrid.SelectedItem is Person person))
            {
                MessageBox.Show("Lütfen düzenlemek için bir kişi seçin.");
                return;
            }

            selectedPersonId = person.Id;
            PopulateForm(person);
            SaveButton.Content = "Güncelle";
            SetStepOneState(StepOneState.Editing);
            // Form alanına kaydır
            RootScrollViewer?.ScrollToVerticalOffset(FormPanel.TranslatePoint(new Point(0,0), RootScrollViewer).Y - 10);
        }

        private void LoadPreview()
        {
            PreviewTextBlock.Text =
                "AD SOYAD : " + FirstNameTextBox.Text + " " + LastNameTextBox.Text +
                "\nTC : " + TcTextBox.Text +
                "\nCİNSİYET : " + GenderComboBox.Text +
                "\nDOĞUM TARİHİ : " + BirthDatePicker.SelectedDate?.ToShortDateString() +
                "\nYAŞ : " + AgeTextBox.Text +
                "\nYAŞ ARALIĞI : " + AgeRangeComboBox.Text +
                "\nTELEFON : " + PhoneTextBox.Text +
                "\nADRES : " + AddressTextBox.Text +
                "\nKAN GRUBU : " + BloodComboBox.Text +
                "\nHASTALIK DURUMU : " + HealthTextBox.Text +
                "\nAŞI DURUMU : " + VaccineTextBox.Text +
                "\nPASAPORT TÜRÜ : " + PassportTypeComboBox.Text +
                "\nPASAPORT NO : " + PassportTextBox.Text +
                "\nPASAPORT BAŞLANGIÇ : " + PassportStartPicker.SelectedDate?.ToShortDateString() +
                    "\nPASAPORT BİTİŞ : " + PassportEndPicker.SelectedDate?.ToShortDateString() +
                    "\nUYRUK : " + (string.Equals(NationalityComboBox.Text, "Seçiniz", StringComparison.OrdinalIgnoreCase) ? string.Empty : NationalityComboBox.Text);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int savedPersonId = 0;

                if (HighlightMissingRequiredFields())
                {
                    MessageBox.Show("Zorunlu alanları doldurun.");

                    // Öncelikle 1. adımda (kimlik) eksik alanlar varsa kullanıcıyı oraya taşı
                    try
                    {
                        var mode = GetNationalityMode();
                        bool missingStep1 = (FirstNameTextBox != null && FirstNameTextBox.Tag as string == "RequiredError")
                                            || (LastNameTextBox != null && LastNameTextBox.Tag as string == "RequiredError")
                                            || (mode == NationalityMode.Turkey && TcTextBox != null && TcTextBox.Tag as string == "RequiredError")
                                            || (GenderComboBox != null && GenderComboBox.Tag as string == "RequiredError");

                        if (missingStep1)
                        {
                            currentStep = 1;
                            UpdateSteps();

                            // İlk eksik alanı bulup odak ver
                            if (FirstNameTextBox != null && FirstNameTextBox.Tag as string == "RequiredError")
                            {
                                FirstNameTextBox.Focus();
                                FirstNameTextBox.BringIntoView();
                            }
                            else if (LastNameTextBox != null && LastNameTextBox.Tag as string == "RequiredError")
                            {
                                LastNameTextBox.Focus();
                                LastNameTextBox.BringIntoView();
                            }
                            else if (mode == NationalityMode.Turkey && TcTextBox != null && TcTextBox.Tag as string == "RequiredError")
                            {
                                TcTextBox.Focus();
                                TcTextBox.BringIntoView();
                            }
                            else if (GenderComboBox != null && GenderComboBox.Tag as string == "RequiredError")
                            {
                                GenderComboBox.Focus();
                                GenderComboBox.BringIntoView();
                            }

                            return;
                        }

                        // Eğer 1.adım tamamsa ama pasaport gerekli ve eksikse pasaport adımına götür
                        if (PassportTextBox != null && PassportTextBox.Tag as string == "RequiredError")
                        {
                            currentStep = 4;
                            UpdateSteps();
                            PassportTextBox.Focus();
                            PassportTextBox.BringIntoView();
                            return;
                        }
                    }
                    catch
                    {
                        // ignore focus failures
                    }

                    return;
                }

                // TC uzunluğunu yalnızca Türkiye vatandaşı için kontrol et (sadece rakamlar sayılır)
                if (GetNationalityMode() == NationalityMode.Turkey)
                {
                    var digitsOnly = new string((TcTextBox.Text ?? string.Empty).Where(char.IsDigit).ToArray());
                    if (digitsOnly.Length != 11)
                    {
                        TcTextBox.Tag = "RequiredError";
                        MessageBox.Show("TC Kimlik No 11 haneli olmalıdır.");
                        try
                        {
                            currentStep = 1;
                            UpdateSteps();
                            TcTextBox.Focus();
                            TcTextBox.BringIntoView();
                        }
                        catch { }
                        return;
                    }
                }

                using (var db = new AppDbContext())
                {
                    bool exists = false;
                    if (GetNationalityMode() == NationalityMode.Turkey && !string.IsNullOrWhiteSpace(TcTextBox.Text))
                    {
                        var digitsOnly = new string(TcTextBox.Text.Where(char.IsDigit).ToArray());
                        exists = selectedPersonId > 0
                            ? db.Persons.Any(x => x.TcKimlikNo == digitsOnly && x.Id != selectedPersonId)
                            : db.Persons.Any(x => x.TcKimlikNo == digitsOnly);

                        if (exists)
                        {
                            TcTextBox.Tag = "RequiredError";
                            MessageBox.Show("Bu TC zaten kayıtlı.");
                            return;
                        }
                    }

                    Person person;

                    if (selectedPersonId > 0)
                    {
                        person = db.Persons.FirstOrDefault(x => x.Id == selectedPersonId);

                        if (person == null)
                        {
                            MessageBox.Show("Kişi bulunamadı.");
                            return;
                        }
                    }
                    else
                    {
                        person = new Person
                        {
                            IsActive = true
                        };

                        db.Persons.Add(person);
                    }

                    person.FirstName = FirstNameTextBox.Text;
                    person.LastName = LastNameTextBox.Text;
                    person.TcKimlikNo = new string((TcTextBox.Text ?? string.Empty).Where(char.IsDigit).ToArray());
                    person.Gender = GenderComboBox.Text;
                    person.BirthDate = BirthDatePicker.SelectedDate ?? DateTime.Now;
                    person.AgeRange = AgeRangeComboBox.Text;
                    var natValue = !string.IsNullOrWhiteSpace(currentNationalityValue) ? currentNationalityValue : NationalityComboBox.Text;
                    person.Nationality = string.Equals(natValue, "Seçiniz", StringComparison.OrdinalIgnoreCase) ? null : natValue;
                    person.PhoneNumber = PhoneTextBox.Text;
                    person.Address = AddressTextBox.Text;
                    person.BloodType = BloodComboBox.Text;
                    person.BloodType = string.IsNullOrWhiteSpace(BloodComboBox.Text) || string.Equals(BloodComboBox.Text, "Seçiniz", StringComparison.OrdinalIgnoreCase)
                        ? "Belirtmedi"
                        : BloodComboBox.Text;

                    person.HealthStatus = string.IsNullOrWhiteSpace(HealthTextBox.Text) ? "Belirtmedi" : HealthTextBox.Text;
                    person.VaccineStatus = string.IsNullOrWhiteSpace(VaccineTextBox.Text) ? "Belirtmedi" : VaccineTextBox.Text;
                    person.PassportType = PassportTypeComboBox.Text;
                    person.PassportNumber = PassportTextBox.Text;
                    person.PassportStartDate = PassportStartPicker.SelectedDate;
                    person.PassportEndDate = PassportEndPicker.SelectedDate;

                    db.SaveChanges();
                    savedPersonId = person.Id;
                }

                ClearRequiredHighlights();
                PersonSaved?.Invoke(savedPersonId);

                if (!SuppressSavedNotification)
                {
                    MessageBox.Show(selectedPersonId > 0 ? "Güncellendi" : "Kaydedildi");
                }

                LoadPersons();
                ResetModuleToEntryState();
                UpdateSteps();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void RequiredField_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                // Öntanımlı davranış: boşsa RequiredError
                textBox.Tag = string.IsNullOrWhiteSpace(textBox.Text) ? "RequiredError" : null;

                if (textBox == TcTextBox)
                {
                    if (_suppressTcTextChanged) return;

                    // Sadece rakamlara izin ver: bütün boşlukları ve harfleri çıkart
                    var original = TcTextBox.Text ?? string.Empty;
                    var digitsOnly = new string(original.Where(char.IsDigit).ToArray());

                    if (digitsOnly != original)
                    {
                        try
                        {
                            _suppressTcTextChanged = true;
                            var caret = TcTextBox.CaretIndex;
                            TcTextBox.Text = digitsOnly;
                            TcTextBox.CaretIndex = Math.Min(caret, TcTextBox.Text.Length);
                        }
                        finally
                        {
                            _suppressTcTextChanged = false;
                        }
                    }

                    // Tag güncellemesi: 11 rakam değilse RequiredError
                    TcTextBox.Tag = digitsOnly.Length == 11 ? null : "RequiredError";
                    ApplyLiveRequiredHighlights();
                    return;
                }

                // Eğer Pasaport alanı değiştiyse gereksinimleri tekrar uygula
                if (textBox == PassportTextBox)
                {
                    ApplyLiveRequiredHighlights();
                }
            }
        }

        private void NationalityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Uyruk değiştiğinde hangi alanın zorunlu olduğunu güncelle
            ApplyLiveRequiredHighlights();

            // Eğer yabancı seçildiyse TC'yi temizle ve devre dışı bırak (güvenlik)
            var mode = GetNationalityMode();
            if (mode == NationalityMode.Foreign)
            {
                if (TcTextBox != null)
                {
                    try
                    {
                        _suppressTcTextChanged = true;
                        TcTextBox.Text = string.Empty;
                    }
                    finally { _suppressTcTextChanged = false; }
                }
            }
        }

        private void GenderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Sadece SelectedIndex kontrolü ile validasyonu yapıyoruz (daha güvenilir)
            if (GenderComboBox.SelectedIndex > 0)
            {
                GenderComboBox.Tag = null;
            }
            else
            {
                GenderComboBox.Tag = "RequiredError";
            }
        }

        private void ComboBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is ComboBox cb)
            {
                // Eğer zaten açıksa normal davranış sürsün
                if (!cb.IsDropDownOpen)
                {
                    cb.IsDropDownOpen = true;
                    e.Handled = true;
                }
            }
        }

        private void ToggleActiveButton_Click(object sender, RoutedEventArgs e)
        {
            // Pasif/aktif toggle özelliği kaldırıldı
            return;
        }

        private void RestoreSelectedPerson(int personId)
        {
            if (!(PersonsDataGrid.ItemsSource is System.Collections.IEnumerable items))
            {
                return;
            }

            foreach (var item in items)
            {
                if (item is Person person && person.Id == personId)
                {
                    PersonsDataGrid.SelectedItem = person;
                    PersonsDataGrid.ScrollIntoView(person);
                    break;
                }
            }
        }

        private void NotifyPersonSelectionChanged(Person person)
        {
            PersonSelectionChanged?.Invoke(person);
        }

        private void ResetModuleToEntryState()
        {
            currentStep = 1;
            SearchTextBox.Text = string.Empty;
            PersonsDataGrid.SelectedItem = null;
            ClearForm();
            _personsView?.Refresh();
            SetStepOneState(StepOneState.Initial);
            SearchTextBox.Focus();
        }
    }
}

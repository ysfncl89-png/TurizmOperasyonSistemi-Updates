using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
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

        private void TcTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Eğer TC alanı etkin değilse odaklanmayı engelle
            try
            {
                if (TcTextBox != null && !TcTextBox.IsEnabled)
                {
                    e.Handled = true;
                }
            }
            catch { }
        }

        private void TcTextBox_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            try
            {
                if (TcTextBox != null && !TcTextBox.IsEnabled)
                {
                    e.Handled = true;
                }
            }
            catch { }
        }

        private void ScrollToAndFocus(FrameworkElement element)
        {
            if (element == null)
            {
                return;
            }

            try
            {
                // Use BeginInvoke so layout updates settle before measuring
                element.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        element.Focus();
                        element.UpdateLayout();

                        if (RootScrollViewer != null)
                        {
                            // Transform element position relative to ScrollViewer
                            var transform = element.TransformToAncestor(RootScrollViewer) as GeneralTransform;
                            if (transform != null)
                            {
                                var pt = transform.Transform(new Point(0, 0));
                                // Calculate offset so element is slightly below top
                                double offset = pt.Y + RootScrollViewer.VerticalOffset - 24;
                                if (double.IsNaN(offset) || double.IsInfinity(offset)) offset = 0;
                                RootScrollViewer.ScrollToVerticalOffset(Math.Max(0, offset));
                            }
                        }
                    }
                    catch { }
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch { }
        }



        private void NewPersonButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Yeni kayıt akışını başlat
                // Eğer listede bir seçim varsa bunu temizle ki Düzenle/Sil butonları görünmesin
                try { PersonsDataGrid.SelectedItem = null; } catch { }
                selectedPersonId = 0;
                ClearForm();
                // ek güvenlik: butonları gizle
                try { EditSelectedButton.Visibility = Visibility.Collapsed; DeleteSelectedButton.Visibility = Visibility.Collapsed; if (ToggleActiveButton!=null) ToggleActiveButton.Visibility = Visibility.Collapsed; } catch { }
                try { NotifyPersonSelectionChanged(null); } catch { }
                SetStepOneState(StepOneState.NewPerson);
                currentStep = 1;
                UpdateSteps();

                // Forma kaydır ve ilk alanı odakla
                RootScrollViewer?.ScrollToVerticalOffset(FormPanel.TranslatePoint(new Point(0, 0), RootScrollViewer).Y - 10);
                FirstNameTextBox?.Focus();
            }
            catch
            {
                // ignore
            }
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
                            Tag = null,
                            GroupName = "NationalityGroup"
                        };

                        rb.Checked += (s, e) =>
                        {
                            currentNationalityValue = (s as RadioButton)?.Content as string ?? string.Empty;
                            ApplyLiveRequiredHighlights();
                            // Kullanıcı radyo seçtiğinde varsa hata işaretlerini temizle
                            try
                            {
                                if (NationalityOptionsPanel != null)
                                {
                                    foreach (var rr in NationalityOptionsPanel.Items.Cast<object>().OfType<RadioButton>()) rr.Tag = null;
                                }
                                if (NationalityComboBox != null) NationalityComboBox.Tag = null;
                            }
                            catch { }
                            // Not changing label text here; label remains 'TC Kimlik No' even if TC textbox is disabled.
                        };

                        // Ensure radio button text is visible on dark theme
                        rb.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
                        rb.FontWeight = FontWeights.SemiBold;

                        panel.Items.Add(rb);
                    }

                    // Not: otomatik varsayılan seçim yapmıyoruz; kullanıcı uyruk seçeneğini manuel işaretlemeli
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
                NeonMessageBoxWindow.Show("Lütfen silmek için bir kişi seçin.", "Uyarı", false);
                return;
            }

            var confirm = NeonMessageBoxWindow.Show($"{person.FirstName} {person.LastName} adlı kişiyi veri tabanından KALICI OLARAK silmek istediğinize emin misiniz?", "Onay", true);
            if (confirm != true)
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

                NeonMessageBoxWindow.Show("Kişi silindi.", "Bilgi", false);
                LoadPersons();
                ResetModuleToEntryState();
                UpdateSteps();
            }
            catch (Exception ex)
            {
                NeonMessageBoxWindow.Show(ex.Message, "Hata", false);
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

            // Prevent entering TC when nationality not selected or not Turkey
            TcTextBox.PreviewMouseLeftButtonDown += TcTextBox_PreviewMouseLeftButtonDown;
            TcTextBox.PreviewGotKeyboardFocus += TcTextBox_PreviewGotKeyboardFocus;

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
            // Eğer 1. adımda ve yeni kişi formundaysak veya düzenleme modundaysak,
            // geri ile arama sonuçlarına dönsün (kullanıcı vazgeçebilsin)
            if (currentStep == 1 && (_stepOneState == StepOneState.NewPerson || _stepOneState == StepOneState.Editing))
            {
                try
                {
                    SetStepOneState(StepOneState.SearchResults);
                    // Arama durumuna göre NoMatchActionPanel görünürlüğünü güncelle
                    EvaluateSearchState();
                    // Seçili öğe varsa seçimi koru ama formu temizlemeden önce Save/Güncelle metnini ve butonları ayarla
                    if (PersonsDataGrid.SelectedItem is Person sel)
                    {
                        // Formda düzenleme görünümündeyken geri gelindiği için form alanlarına dokunma; sadece buton görünürlüğünü ayarla
                        EditSelectedButton.Visibility = Visibility.Visible;
                        DeleteSelectedButton.Visibility = Visibility.Visible;
                        SaveButton.Content = "Güncelle";
                    }
                    UpdateSteps();
                    // Sonuç paneline kaydır
                    RootScrollViewer?.ScrollToVerticalOffset(ResultsPanel.TranslatePoint(new Point(0, 0), RootScrollViewer).Y - 10);
                }
                catch
                {
                    // ignore
                    UpdateSteps();
                }

                // Eğer DataGrid'de bir seçim varsa, geri döndüğümüzde Düzenle/Sil butonlarını görünür yap
                try
                {
                    if (PersonsDataGrid.SelectedItem is Person sel)
                    {
                        selectedPersonId = sel.Id;
                        EditSelectedButton.Visibility = Visibility.Visible;
                        DeleteSelectedButton.Visibility = Visibility.Visible;
                        SaveButton.Content = "Güncelle";
                    }
                }
                catch
                {
                    // ignore
                }

                return;
            }

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
            // Normalde geri düğmesi sadece adımlar arasında gezinme içindir
            // Ancak 1. adımda "Yeni Kişi" formundaysa, kullanıcı geri ile arama sonuçlarına dönebilmelidir
            BackButton.IsEnabled = currentStep > 1 || (currentStep == 1 && (_stepOneState == StepOneState.NewPerson || _stepOneState == StepOneState.Editing));
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
            // Uyruk seçimi zorunluluğu: ya radyo seçeneklerinden birisi seçili olmalı ya da NationalityComboBox'ta geçerli bir seçim olmalı
            bool nationalitySelected = false;
            try
            {
                if (!string.IsNullOrWhiteSpace(currentNationalityValue)) nationalitySelected = true;
                else if (NationalityOptionsPanel != null)
                {
                    var any = NationalityOptionsPanel.Items.Cast<object>().OfType<RadioButton>().Any(r => r.IsChecked == true);
                    if (any) nationalitySelected = true;
                }
                if (!nationalitySelected && NationalityComboBox != null && NationalityComboBox.SelectedIndex > 0 && !string.Equals(NationalityComboBox.Text, "Seçiniz", StringComparison.OrdinalIgnoreCase)) nationalitySelected = true;
            }
            catch { }


            // Keep TC label text constant ("TC Kimlik No") even if TC textbox is disabled for foreign nationals.

            // Her iki alan da düzenlenebilir kalsın; sadece zorunluluk Tag'leri uyruk durumuna göre ayarlansın
            // TC yalnızca Türkiye seçiliyse düzenlenebilir; yabancıysa devre dışı bırakarak giriş engelle
            if (TcTextBox != null)
            {
                // TC kutusu yalnızca Türkiye seçiliyse aktif olsun; uyruk seçilmemişse (Unknown) ya da yabancıysa pasif kalsın
                TcTextBox.IsEnabled = mode == NationalityMode.Turkey;
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
                // TC alanı zorunlu: Tag'in RequiredError olması veya boş olması halinde hata kabul et
                if (TcTextBox == null || string.IsNullOrWhiteSpace(TcTextBox.Text) || (TcTextBox.Tag as string) == "RequiredError")
                {
                    if (TcTextBox != null) TcTextBox.Tag = "RequiredError";
                    hasError = true;
                }
            }
            else if (mode == NationalityMode.Foreign)
            {
                // Yabancıysa pasaport numarası zorunlu: Tag veya boşluk kontrol et
                if (PassportTextBox == null || string.IsNullOrWhiteSpace(PassportTextBox.Text) || (PassportTextBox.Tag as string) == "RequiredError")
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

            // Uyruk seçimi zorunluluğu kontrolü (HighlightMissingRequiredFields içinde de hesapla)
            bool nationalitySelected = false;
            try
            {
                if (!string.IsNullOrWhiteSpace(currentNationalityValue)) nationalitySelected = true;
                else if (NationalityOptionsPanel != null)
                {
                    var any = NationalityOptionsPanel.Items.Cast<object>().OfType<RadioButton>().Any(r => r.IsChecked == true);
                    if (any) nationalitySelected = true;
                }
                if (!nationalitySelected && NationalityComboBox != null && NationalityComboBox.SelectedIndex > 0 && !string.Equals(NationalityComboBox.Text, "Seçiniz", StringComparison.OrdinalIgnoreCase)) nationalitySelected = true;
            }
            catch { }

            if (!nationalitySelected)
            {
                try
                {
                    if (NationalityOptionsPanel != null)
                    {
                        var firstRb = NationalityOptionsPanel.Items.Cast<object>().OfType<RadioButton>().FirstOrDefault();
                        if (firstRb != null)
                        {
                            firstRb.Tag = "RequiredError";
                        }
                    }

                    if (NationalityComboBox != null)
                    {
                        NationalityComboBox.Tag = "RequiredError";
                    }
                }
                catch { }

                hasError = true;
            }
            else
            {
                try
                {
                    if (NationalityOptionsPanel != null)
                    {
                        foreach (var rb in NationalityOptionsPanel.Items.Cast<object>().OfType<RadioButton>()) rb.Tag = null;
                    }

                    if (NationalityComboBox != null) NationalityComboBox.Tag = null;
                }
                catch { }
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
                // Eğer sonuçlar varsa:
                // - Kullanıcı TC (rakam içeren) arıyorsa, yalnızca TC eşleşmesi varsa "tam eşleşme" kabul et.
                // - Kullanıcı isimle arıyorsa, isim benzerliği yeni kayıt yapmayı engellemesin; bu durumda
                //   her zaman "Aradığım kişi listede yok" butonunu göstererek kullanıcıya yeni kayıt hakkı ver.
                try
                {
                    var digitQuery = new string((SearchTextBox.Text ?? string.Empty).Where(char.IsDigit).ToArray());
                    var matches = _personsView?.Cast<object>().OfType<Person>().ToList() ?? new System.Collections.Generic.List<Person>();

                    if (!string.IsNullOrWhiteSpace(digitQuery) && digitQuery.Length >= 3)
                    {
                        // TC araması: eşleşen TC varsa butonu gizle
                        bool tcMatch = matches.Any(p => !string.IsNullOrWhiteSpace(p.TcKimlikNo) && new string(p.TcKimlikNo.Where(char.IsDigit).ToArray()).Contains(digitQuery));
                        NoMatchActionPanel.Visibility = tcMatch ? Visibility.Collapsed : Visibility.Visible;
                    }
                    else
                    {
                        // İsim araması: her durumda yeni kayıt butonunu erişilebilir tut
                        NoMatchActionPanel.Visibility = Visibility.Visible;
                    }
                }
                catch
                {
                    try { NoMatchActionPanel.Visibility = Visibility.Visible; } catch { }
                }

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
            // UpdateSteps ile footer (Geri/İleri) butonlarının durumu güncellensin
            if (currentStep == 1)
            {
                UpdateSteps();
            }
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
                    try { NoMatchActionPanel.Visibility = Visibility.Collapsed; } catch { }
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
                    // "Aradığım kişi listede yok" aksiyon panelini göster
                    try { NoMatchActionPanel.Visibility = Visibility.Visible; } catch { }
                    FormCaptionTextBlock.Text = "Bu kişi sistemde bulunamadı. Aşağıdaki alanları doldurup adımlarda ilerleyebilirsiniz.";
                    NoPersonNoticeTextBlock.Visibility = Visibility.Visible;

                    // Yeni kayıt formu açıldığında kimlik adımındaki zorunlu alanların neon ile görünmesini garanti et
                    try
                    {
                        if (FirstNameTextBox != null) FirstNameTextBox.Tag = "RequiredError";
                        if (LastNameTextBox != null) LastNameTextBox.Tag = "RequiredError";
                        if (GenderComboBox != null) GenderComboBox.Tag = "RequiredError";

                        // Uyruk radyo seçeneklerini zorunlu göster
                        if (NationalityOptionsPanel != null)
                        {
                            foreach (var rb in NationalityOptionsPanel.Items.Cast<object>().OfType<RadioButton>())
                            {
                                rb.Tag = "RequiredError";
                            }
                        }

                        if (NationalityComboBox != null) NationalityComboBox.Tag = "RequiredError";

                        // TC alanı uyruk seçilmeden kapalı kalsın (ApplyLiveRequiredHighlights bunu zaten sağlar)
                        ApplyLiveRequiredHighlights();
                    }
                    catch { }
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
            var tcLabelText = "TC";
            try { var lbl = FindName("TcLabel") as TextBlock; if (lbl != null) tcLabelText = lbl.Text; } catch { }

            PreviewTextBlock.Text =
                "AD SOYAD : " + FirstNameTextBox.Text + " " + LastNameTextBox.Text +
                "\n" + tcLabelText + " : " + TcTextBox.Text +
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
                NeonMessageBoxWindow.Show("Zorunlu alanları doldurun.", "Uyarı", false);

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
                                ScrollToAndFocus(FirstNameTextBox);
                            }
                            else if (LastNameTextBox != null && LastNameTextBox.Tag as string == "RequiredError")
                            {
                                ScrollToAndFocus(LastNameTextBox);
                            }
                            else if (mode == NationalityMode.Turkey && TcTextBox != null && TcTextBox.Tag as string == "RequiredError")
                            {
                                ScrollToAndFocus(TcTextBox);
                            }
                            else if (GenderComboBox != null && GenderComboBox.Tag as string == "RequiredError")
                            {
                                ScrollToAndFocus(GenderComboBox);
                            }

                            return;
                        }

                        // Eğer 1.adım tamamsa ama pasaport gerekli ve eksikse pasaport adımına götür
                        if (PassportTextBox != null && PassportTextBox.Tag as string == "RequiredError")
                        {
                            currentStep = 4;
                            UpdateSteps();
                            ScrollToAndFocus(PassportTextBox);
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
                            NeonMessageBoxWindow.Show("TC Kimlik No 11 haneli olmalıdır.", "Uyarı", false);
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
                            NeonMessageBoxWindow.Show("Bu TC zaten kayıtlı.", "Uyarı", false);
                            return;
                        }
                    }

                    Person person;

                    if (selectedPersonId > 0)
                    {
                        person = db.Persons.FirstOrDefault(x => x.Id == selectedPersonId);

                        if (person == null)
                        {
                            NeonMessageBoxWindow.Show("Kişi bulunamadı.", "Uyarı", false);
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
                    NeonMessageBoxWindow.Show(selectedPersonId > 0 ? "Güncellendi" : "Kaydedildi", "Bilgi", false);
                }

                LoadPersons();
                ResetModuleToEntryState();
                UpdateSteps();
            }
            catch (Exception ex)
            {
                NeonMessageBoxWindow.Show(ex.Message, "Hata", false);
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
            // ComboBox üzerinden seçim yapıldıysa currentNationalityValue'yu güncelle
            try
            {
                if (NationalityComboBox != null && !string.IsNullOrWhiteSpace(NationalityComboBox.Text) && !string.Equals(NationalityComboBox.Text, "Seçiniz", StringComparison.OrdinalIgnoreCase))
                {
                    currentNationalityValue = NationalityComboBox.Text;
                }
                else if (NationalityComboBox != null && string.Equals(NationalityComboBox.Text, "Seçiniz", StringComparison.OrdinalIgnoreCase))
                {
                    currentNationalityValue = string.Empty;
                }
            }
            catch { }
            // Not changing the TC label text here - keep it as "TC Kimlik No" even if the textbox is disabled.
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

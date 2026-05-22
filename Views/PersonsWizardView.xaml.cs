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
        public event Action<Person> PersonSelectionChanged;
        public event Action<int> PersonSaved;

        private enum StepOneState
        {
            Initial,
            SearchResults,
            NewPerson,
            Editing
        }

        private readonly BlurEffect _panelBlur = new BlurEffect { Radius = 10 };

        private int currentStep = 1;
        private int selectedPersonId = 0;
        private StepOneState _stepOneState = StepOneState.Initial;
        private ICollectionView _personsView;

        public PersonsWizardView()
        {
            InitializeComponent();

            LoadComboBox(GenderComboBox, "Cinsiyet");
            LoadComboBox(AgeRangeComboBox, "Yaş Aralığı");
            LoadComboBox(BloodComboBox, "Kan Grubu");
            LoadComboBox(PassportTypeComboBox, "Pasaport Türü");

            LoadPersons();
            ResetModuleToEntryState();
            UpdateSteps();

            Loaded += PersonsWizardView_Loaded;
            FirstNameTextBox.TextChanged += RequiredField_TextChanged;
            LastNameTextBox.TextChanged += RequiredField_TextChanged;
            TcTextBox.TextChanged += RequiredField_TextChanged;
            GenderComboBox.SelectionChanged += GenderComboBox_SelectionChanged;
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

                comboBox.ItemsSource = values;
                comboBox.SelectedIndex = 0;
            }
        }

        private void ClearRequiredHighlights()
        {
            FirstNameTextBox.Tag = null;
            LastNameTextBox.Tag = null;
            TcTextBox.Tag = null;
            GenderComboBox.Tag = null;
        }

        private bool HighlightMissingRequiredFields()
        {
            ClearRequiredHighlights();

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

            if (string.IsNullOrWhiteSpace(TcTextBox.Text))
            {
                TcTextBox.Tag = "RequiredError";
                hasError = true;
            }

            if (GenderComboBox.SelectedIndex <= 0 || string.Equals(GenderComboBox.Text, "Seçiniz", StringComparison.OrdinalIgnoreCase))
            {
                GenderComboBox.Tag = "RequiredError";
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
            ToggleActiveButton.Visibility = Visibility.Collapsed;
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
                SearchStateTextBlock.Text = "Kayıt bulunduysa listeden kişiyi seçin. Pasif bir kişiyi de burada görebilir, aktif yapabilir veya Düzenle ile güncelleyebilirsiniz.";
                return;
            }

            ClearForm();
            SetStepOneState(StepOneState.NewPerson);
            SearchStateTextBlock.Text = "Aradığınız kişi sistemde kayıtlı değil. Aşağıdaki formdan kişiyi kaydederek ilerleyin.";
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
                    ResultsPanel.Effect = _panelBlur;
                    ResultsOverlay.Visibility = Visibility.Visible;
                    ResultsOverlayTitle.Text = "Önce kişiyi arayın";
                    ResultsOverlayText.Text = "Arama kutusuna ad, soyad, ad soyad ya da TC kimlik yazın. Kayıt bulunduğunda sonuç listesi açılacak. Kayıt Yoksa Kayıt formu aktif olacak ";

                    FormPanel.Effect = _panelBlur;
                    FormOverlay.Visibility = Visibility.Visible;
                    FormOverlayTitle.Text = "Kayıt formu beklemede";
                    FormOverlayText.Text = "Önce yukarıdan arama yapın. Kayıt bulunamazsa bu form yeni kişi kaydı için açılacak.";
                    FormCaptionTextBlock.Text = "Yeni kişi kaydı oluşturmak ya da seçili kişiyi güncellemek için bu alanı kullanın.";
                    SearchStateTextBlock.Text = "Aramayı başlatın. Sistem pasif kişiler dahil tüm eşleşmeleri sonuçlarda gösterecek.";
                    break;

                case StepOneState.SearchResults:
                    ResultsPanel.Effect = null;
                    ResultsOverlay.Visibility = Visibility.Collapsed;

                    FormPanel.Effect = _panelBlur;
                    FormOverlay.Visibility = Visibility.Visible;
                    FormOverlayTitle.Text = "Kayıt bulundu";
                    FormOverlayText.Text = "Düzenleme yapmak istiyorsanız listeden kişiyi seçip Düzenle butonuna tıklayın. Sadece aktif pasif durumunu değiştirmek istiyorsanız sağdaki butonu kullanabilirsiniz.";
                    FormCaptionTextBlock.Text = "Seçili kaydı düzenlemek için önce listeden kişiyi seçip Düzenle butonuna tıklayın.";
                    break;

                case StepOneState.NewPerson:
                    ResultsPanel.Effect = _panelBlur;
                    ResultsOverlay.Visibility = Visibility.Visible;
                    ResultsOverlayTitle.Text = "Kayıt bulunamadı";
                    ResultsOverlayText.Text = "Aradığınız kişi sistemde kayıtlı değil. Aşağıdaki formdan kişiyi kaydederek ilerleyin.";

                    FormPanel.Effect = null;
                    FormOverlay.Visibility = Visibility.Collapsed;
                    FormCaptionTextBlock.Text = "Bu kişi sistemde bulunamadı. Aşağıdaki alanları doldurup adımlarda ilerleyebilirsiniz.";
                    break;

                case StepOneState.Editing:
                    ResultsPanel.Effect = null;
                    ResultsOverlay.Visibility = Visibility.Collapsed;

                    FormPanel.Effect = null;
                    FormOverlay.Visibility = Visibility.Collapsed;
                    FormCaptionTextBlock.Text = "Seçili kişi düzenleme modunda. Değişiklikleri yapıp son adımda güncelleyebilirsiniz.";
                    SearchStateTextBlock.Text = "Düzenleme modundasınız. Kişi pasifse aktif yapabilir, aktifse pasif yapabilir ve adımlar arasında ilerleyerek güncelleyebilirsiniz.";
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
            ToggleActiveButton.Visibility = Visibility.Collapsed;
            ToggleActiveButton.Content = "Pasif Yap";
            EditSelectedButton.Visibility = Visibility.Collapsed;
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
        }

        private void PersonsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PersonsDataGrid.SelectedItem is Person person)
            {
                selectedPersonId = person.Id;
                PopulateForm(person);

                SaveButton.Content = "Güncelle";
                ToggleActiveButton.Visibility = Visibility.Visible;
                ToggleActiveButton.Content = person.IsActive ? "Pasif Yap" : "Aktif Yap";
                EditSelectedButton.Visibility = Visibility.Visible;

                SearchStateTextBlock.Text = person.IsActive
                    ? "Bu kişi aktif kayıtta. İsterseniz Pasif Yap ile durumunu değiştirebilir ya da Düzenle ile kayıt adımlarına geçebilirsiniz."
                    : "Bu kişi pasif durumda. İsterseniz Aktif Yap ile yeniden aktif edebilir ya da Düzenle ile kayıt adımlarına geçebilirsiniz.";

                if (_stepOneState != StepOneState.Editing)
                {
                    SetStepOneState(StepOneState.SearchResults);
                }

                NotifyPersonSelectionChanged(person);
            }
            else
            {
                selectedPersonId = 0;
                ToggleActiveButton.Visibility = Visibility.Collapsed;
                EditSelectedButton.Visibility = Visibility.Collapsed;
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
            FirstNameTextBox.BringIntoView();
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
                "\nPASAPORT BİTİŞ : " + PassportEndPicker.SelectedDate?.ToShortDateString();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int savedPersonId = 0;

                if (HighlightMissingRequiredFields())
                {
                    MessageBox.Show("Zorunlu alanları doldurun.");
                    return;
                }

                if (TcTextBox.Text.Length != 11)
                {
                    TcTextBox.Tag = "RequiredError";
                    MessageBox.Show("TC Kimlik No 11 haneli olmalıdır.");
                    return;
                }

                using (var db = new AppDbContext())
                {
                    bool exists = selectedPersonId > 0
                        ? db.Persons.Any(x => x.TcKimlikNo == TcTextBox.Text && x.Id != selectedPersonId)
                        : db.Persons.Any(x => x.TcKimlikNo == TcTextBox.Text);

                    if (exists)
                    {
                        TcTextBox.Tag = "RequiredError";
                        MessageBox.Show("Bu TC zaten kayıtlı.");
                        return;
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
                    person.TcKimlikNo = TcTextBox.Text;
                    person.Gender = GenderComboBox.Text;
                    person.BirthDate = BirthDatePicker.SelectedDate ?? DateTime.Now;
                    person.AgeRange = AgeRangeComboBox.Text;
                    person.PhoneNumber = PhoneTextBox.Text;
                    person.Address = AddressTextBox.Text;
                    person.BloodType = BloodComboBox.Text;
                    person.HealthStatus = HealthTextBox.Text;
                    person.VaccineStatus = VaccineTextBox.Text;
                    person.PassportType = PassportTypeComboBox.Text;
                    person.PassportNumber = PassportTextBox.Text;
                    person.PassportStartDate = PassportStartPicker.SelectedDate;
                    person.PassportEndDate = PassportEndPicker.SelectedDate;

                    db.SaveChanges();
                    savedPersonId = person.Id;
                }

                ClearRequiredHighlights();
                PersonSaved?.Invoke(savedPersonId);

                MessageBox.Show(selectedPersonId > 0 ? "Güncellendi" : "Kaydedildi");

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
            if (sender is TextBox textBox && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                textBox.Tag = null;
            }
        }

        private void GenderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GenderComboBox.SelectedIndex > 0 && !string.Equals(GenderComboBox.Text, "Seçiniz", StringComparison.OrdinalIgnoreCase))
            {
                GenderComboBox.Tag = null;
            }
        }

        private void ToggleActiveButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedPersonId == 0)
            {
                MessageBox.Show("Lütfen bir kişi seçin.");
                return;
            }

            using (var db = new AppDbContext())
            {
                var person = db.Persons.FirstOrDefault(p => p.Id == selectedPersonId);

                if (person != null)
                {
                    person.IsActive = !person.IsActive;
                    db.SaveChanges();

                    MessageBox.Show(person.IsActive ? "Aktif" : "Pasif");
                }
            }

            int personIdToRestore = selectedPersonId;

            LoadPersons();
            _personsView?.Refresh();
            RestoreSelectedPerson(personIdToRestore);
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

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.ComponentModel;
using System.Globalization;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Views
{
    public partial class PersonsWizardView : Page
    {
        private int currentStep = 1;
        private int selectedPersonId = 0;

        private ICollectionView _personsView;

        public PersonsWizardView()
        {
            InitializeComponent();

            LoadComboBox(GenderComboBox, "Cinsiyet");
            LoadComboBox(AgeRangeComboBox, "Yaş Aralığı");
            LoadComboBox(BloodComboBox, "Kan Grubu");
            LoadComboBox(PassportTypeComboBox, "Pasaport Türü");

            LoadPersons();

            UpdateSteps();
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentStep < 5)
            {
                currentStep++;

                if (currentStep == 5)
                    LoadPreview();

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

        // STEP YÖNETİMİ
        private void UpdateSteps()
        {
            Step1Grid.Visibility = Visibility.Collapsed;
            Step2Grid.Visibility = Visibility.Collapsed;
            Step3Grid.Visibility = Visibility.Collapsed;
            Step4Grid.Visibility = Visibility.Collapsed;
            Step5Grid.Visibility = Visibility.Collapsed;

            var passiveColor = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)
                System.Windows.Media.ColorConverter.ConvertFromString("#1F2937"));

            var activeColor = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)
                System.Windows.Media.ColorConverter.ConvertFromString("#06B6D4"));

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

            NextButton.Visibility =
                currentStep == 5
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }

        // YAŞ HESAPLAMA
        private void BirthDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BirthDatePicker.SelectedDate != null)
            {
                int age =
                    DateTime.Now.Year -
                    BirthDatePicker.SelectedDate.Value.Year;

                if (DateTime.Now.DayOfYear <
                    BirthDatePicker.SelectedDate.Value.DayOfYear)
                {
                    age--;
                }

                AgeTextBox.Text = age.ToString();
            }
        }

        // PERSON LİSTESİ + FILTER SETUP
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

        // 🔥 SMART SEARCH + PASİF FİLTRESİ
        private bool FilterPersons(object obj)
        {
            if (obj == null) return false;

            var person = obj as Person;
            if (person == null) return false;

            // 🔹 Pasifleri gizle/göster kontrolü
            if (person.IsActive == false && ShowPassiveCheckBox.IsChecked != true)
                return false;

            var rawText = SearchTextBox.Text ?? "";

            var text = Normalize(rawText);

            if (string.IsNullOrWhiteSpace(text))
                return true;

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

        // 🔧 NORMALIZER (case + space + culture safe)
        private string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            return string.Join(" ",
                input
                    .Trim()
                    .ToLowerInvariant()
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            );
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _personsView?.Refresh();
        }

        // 🔹 CHECKBOX FILTER REFRESH
        private void ShowPassiveCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _personsView?.Refresh();
        }

        // COMBOBOX DOLDUR
        private void LoadComboBox(ComboBox comboBox, string type)
        {
            using (var db = new AppDbContext())
            {
                var values = db.AppSettings
                    .Where(x => x.Type == type && x.IsActive)
                    .OrderBy(x => x.SortOrder)
                    .Select(x => x.Value)
                    .ToList();

                values.Insert(0, "Seçiniz");

                comboBox.ItemsSource = values;
                comboBox.SelectedIndex = 0;
            }
        }

        // ÖNİZLEME
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

        // FORM TEMİZLE
        private void ClearForm()
        {
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
        }

        // DATAGRID SEÇİM
        private void PersonsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PersonsDataGrid.SelectedItem is Person person)
            {
                selectedPersonId = person.Id;

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

                GenderComboBox.Text = person.Gender;
                AgeRangeComboBox.Text = person.AgeRange;
                BloodComboBox.Text = person.BloodType;
                PassportTypeComboBox.Text = person.PassportType;

                SaveButton.Content = "Güncelle";

                ToggleActiveButton.Visibility = Visibility.Visible;
                ToggleActiveButton.Content =
                    person.IsActive ? "Pasif Yap" : "Aktif Yap";
            }
            else
            {
                selectedPersonId = 0;
                ToggleActiveButton.Visibility = Visibility.Collapsed;
            }
        }

        // SAVE
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(FirstNameTextBox.Text) ||
                    string.IsNullOrWhiteSpace(LastNameTextBox.Text) ||
                    string.IsNullOrWhiteSpace(TcTextBox.Text))
                {
                    MessageBox.Show("Zorunlu alanları doldurun.");
                    return;
                }

                if (TcTextBox.Text.Length != 11)
                {
                    MessageBox.Show("TC Kimlik No 11 haneli olmalıdır.");
                    return;
                }

                using (var db = new AppDbContext())
                {
                    bool exists =
                        selectedPersonId > 0
                            ? db.Persons.Any(x => x.TcKimlikNo == TcTextBox.Text && x.Id != selectedPersonId)
                            : db.Persons.Any(x => x.TcKimlikNo == TcTextBox.Text);

                    if (exists)
                    {
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

                    MessageBox.Show(selectedPersonId > 0 ? "Güncellendi" : "Kaydedildi");

                    LoadPersons();
                    ClearForm();

                    currentStep = 1;
                    UpdateSteps();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        // ACTIVE/PASSIVE
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

                    ToggleActiveButton.Content =
                        person.IsActive ? "Pasif Yap" : "Aktif Yap";

                    LoadPersons();

                    _personsView?.Refresh();

                    MessageBox.Show(person.IsActive ? "Aktif" : "Pasif");
                }
            }
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            string query = Normalize(SearchTextBox.Text);

            using (var db = new AppDbContext())
            {
                var filtered = db.Persons
                    .ToList()
                    .Where(p =>
                    {
                        var full = Normalize($"{p.FirstName} {p.LastName}");
                        var fullReverse = Normalize($"{p.LastName} {p.FirstName}");
                        var tc = Normalize(p.TcKimlikNo);

                        return full.Contains(query)
                            || fullReverse.Contains(query)
                            || tc.Contains(query);
                    })
                    .ToList();

                PersonsDataGrid.ItemsSource = filtered;

                if (filtered.Count == 0)
                {
                    MessageBox.Show("Aramanızla eşleşen kayıt bulunamadı.");
                }
            }
        }
    }
}
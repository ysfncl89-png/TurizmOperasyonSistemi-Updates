using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.ComponentModel;
using System.Windows.Media;
using System.Collections.Generic;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Views
{
    public partial class RegistrationsWizardView : Page
    {
        private int currentStep = 1;
        private int selectedPersonId = 0;
        private ICollectionView _personsView;

        private string selectedJoinCity = "";

        public RegistrationsWizardView()
        {
            InitializeComponent();
            LoadPersons();
            LoadSettingsToComboBoxes();
        }

        private const int MaxStep = 9;

        private void LoadPersons()
        {
            using (var db = new AppDbContext())
            {
                var list = db.Persons.OrderByDescending(x => x.Id).ToList();

                PersonsDataGrid.ItemsSource = list;
                _personsView = CollectionViewSource.GetDefaultView(PersonsDataGrid.ItemsSource);
                _personsView.Filter = FilterPersons;
            }
        }

        private bool FilterPersons(object obj)
        {
            if (obj == null) return false;

            var person = obj as Person;
            if (person == null) return false;

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

        private string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            return string.Join(" ",
                input.Trim().ToLowerInvariant()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _personsView?.Refresh();
            CheckSearchValidity();
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            _personsView?.Refresh();
            CheckSearchValidity();
        }

        private void ShowPassiveCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _personsView?.Refresh();
            CheckSearchValidity();
        }

        private void CheckSearchValidity()
        {
            if (_personsView == null) return;

            bool anyItem = _personsView.Cast<object>().Any();

            NoPersonPanel.Visibility =
                (!anyItem && !string.IsNullOrWhiteSpace(SearchTextBox.Text))
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void PersonsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PersonsDataGrid.SelectedItem is Person person)
            {
                selectedPersonId = person.Id;

                if (!person.IsActive)
                {
                    NextButton.IsEnabled = false;
                    ToggleActiveButton.Visibility = Visibility.Visible;
                }
                else
                {
                    NextButton.IsEnabled = true;
                    ToggleActiveButton.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                selectedPersonId = 0;
                NextButton.IsEnabled = false;
                ToggleActiveButton.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowQuickAddButton_Click(object sender, RoutedEventArgs e)
        {
            NoPersonPanel.Visibility = Visibility.Collapsed;
            QuickAddFormPanel.Visibility = Visibility.Visible;

            LoadSettingsToWizardForm();

            QuickFirstNameTextBox.Text = SearchTextBox.Text;
            QuickFirstNameTextBox.Focus();
        }

        private void LoadSettingsToWizardForm()
        {
            using (var db = new AppDbContext())
            {
                var activeSettings = db.AppSettings
                    .Where(x => x.IsActive)
                    .OrderBy(x => x.SortOrder)
                    .ToList();

                QuickPassportTypeComboBox.ItemsSource = activeSettings.Where(x => x.Type == "Pasaport Türü").ToList();
                QuickGenderComboBox.ItemsSource = activeSettings.Where(x => x.Type == "Cinsiyet").ToList();
                QuickBloodTypeComboBox.ItemsSource = activeSettings.Where(x => x.Type == "Kan Grubu").ToList();
                QuickAgeRangeComboBox.ItemsSource = activeSettings.Where(x => x.Type == "Yaş Aralığı").ToList();
            }
        }

        private void QuickBirthDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (QuickBirthDatePicker.SelectedDate != null)
            {
                DateTime birthDate = QuickBirthDatePicker.SelectedDate.Value;
                int age = DateTime.Today.Year - birthDate.Year;

                if (birthDate.Date > DateTime.Today.AddYears(-age)) age--;

                string targetRange = "Yetişkin";

                if (age >= 0 && age <= 2) targetRange = "Bebek (0-2)";
                else if (age > 2 && age <= 12) targetRange = "Çocuk (2-12)";
                else if (age > 12 && age < 65) targetRange = "Yetişkin";
                else targetRange = "Yaşlı / Kıdemli";

                QuickAgeRangeComboBox.SelectedValue = targetRange;
            }
        }

        private void SaveQuickPersonButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(QuickFirstNameTextBox.Text))
            {
                MessageBox.Show("Lütfen kişinin 'Adı' alanını boş bırakmayınız.");
                return;
            }

            if (string.IsNullOrWhiteSpace(QuickLastNameTextBox.Text))
            {
                MessageBox.Show("Lütfen kişinin 'Soyadı' alanını boş bırakmayınız.");
                return;
            }

            if (string.IsNullOrWhiteSpace(QuickTcTextBox.Text))
            {
                MessageBox.Show("Lütfen 'TC Kimlik No' alanını boş bırakmayınız.");
                return;
            }

            if (QuickBirthDatePicker.SelectedDate == null)
            {
                MessageBox.Show("Lütfen doğum tarihi seçiniz.");
                return;
            }

            if (QuickGenderComboBox.SelectedValue == null)
            {
                MessageBox.Show("Lütfen cinsiyet seçiniz.");
                return;
            }

            string tcNo = QuickTcTextBox.Text.Trim();

            if (tcNo.Length != 11 || !tcNo.All(char.IsDigit))
            {
                MessageBox.Show("TC Kimlik No hatalı.");
                return;
            }

            using (var db = new AppDbContext())
            {
                if (db.Persons.Any(x => x.TcKimlikNo == tcNo && x.IsActive))
                {
                    MessageBox.Show("Bu TC zaten kayıtlı.");
                    ClearQuickAddForm();
                    SearchTextBox.Text = tcNo;
                    SearchTextBox.Focus();
                    return;
                }
            }

            string passportType = QuickPassportTypeComboBox.SelectedValue?.ToString() ?? "Belirtilmedi";
            string gender = QuickGenderComboBox.SelectedValue.ToString();
            string ageRange = QuickAgeRangeComboBox.SelectedValue?.ToString() ?? "Yetişkin";
            string bloodType = QuickBloodTypeComboBox.SelectedValue?.ToString() ?? "Belirtilmedi";

            if (MessageBox.Show("Kaydı onaylıyor musunuz?", "Onay", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                using (var db = new AppDbContext())
                {
                    var newPerson = new Person
                    {
                        TcKimlikNo = tcNo,
                        FirstName = QuickFirstNameTextBox.Text.Trim(),
                        LastName = QuickLastNameTextBox.Text.Trim(),
                        PhoneNumber = QuickPhoneTextBox.Text.Trim(),
                        Address = QuickAddressTextBox.Text.Trim(),
                        AgeRange = ageRange,
                        BirthDate = QuickBirthDatePicker.SelectedDate.Value,
                        BloodType = bloodType,
                        Gender = gender,
                        PassportNumber = QuickPassportNumberTextBox.Text?.Trim() ?? "Yok",
                        HealthStatus = QuickHealthStatusTextBox.Text?.Trim() ?? "Belirtilmedi",
                        VaccineStatus = QuickVaccineStatusTextBox.Text?.Trim() ?? "Belirtilmedi",
                        PassportType = passportType,
                        IsActive = true
                    };

                    db.Persons.Add(newPerson);
                    db.SaveChanges();

                    selectedPersonId = newPerson.Id;
                    LoadPersons();
                    ClearQuickAddForm();
                    SwitchToStep(2);
                }
            }
        }

        private void ClearQuickAddForm()
        {
            QuickTcTextBox.Clear();
            QuickFirstNameTextBox.Clear();
            QuickLastNameTextBox.Clear();
            QuickPhoneTextBox.Clear();
            QuickBirthDatePicker.SelectedDate = null;
            QuickAddFormPanel.Visibility = Visibility.Collapsed;
        }

        private void SwitchToStep(int step)
        {
            currentStep = step;

            Step1Grid.Visibility = Visibility.Collapsed;
            Step2Grid.Visibility = Visibility.Collapsed;
            Step3Grid.Visibility = Visibility.Collapsed;
            Step4Grid.Visibility = Visibility.Collapsed;
            Step5Grid.Visibility = Visibility.Collapsed;
            Step6Grid.Visibility = Visibility.Collapsed;
            Step7Grid.Visibility = Visibility.Collapsed;
            Step8Grid.Visibility = Visibility.Collapsed;
            Step9Grid.Visibility = Visibility.Collapsed;

            switch (step)
            {
                case 1: Step1Grid.Visibility = Visibility.Visible; break;
                case 2: Step2Grid.Visibility = Visibility.Visible; break;
                case 3: Step3Grid.Visibility = Visibility.Visible; break;
                case 4: Step4Grid.Visibility = Visibility.Visible; break;
                case 5: Step5Grid.Visibility = Visibility.Visible; break;
                case 6: Step6Grid.Visibility = Visibility.Visible; break;
                case 7: Step7Grid.Visibility = Visibility.Visible; break;
                case 8: Step8Grid.Visibility = Visibility.Visible; break;
                case 9:
                    Step9Grid.Visibility = Visibility.Visible;
                    LoadPreviewData();
                    break;
            }

            BackButton.Visibility = step > 1 ? Visibility.Visible : Visibility.Collapsed;

            NextButton.Content = step == MaxStep ? "Kaydet" : "İleri";

            UpdateNextButtonState();
        }

        private void UpdateNextButtonState()
        {
            switch (currentStep)
            {
                case 1:
                    NextButton.IsEnabled = PersonsDataGrid.SelectedItem is Person p && p.IsActive;
                    break;

                case 2:
                    NextButton.IsEnabled =
                        TourTypeComboBox.SelectedItem != null &&
                        PeriodComboBox.SelectedItem != null &&
                        GroupTypeComboBox.SelectedItem != null &&
                        DaysCountComboBox.SelectedItem != null;
                    break;

                default:
                    NextButton.IsEnabled = true;
                    break;
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentStep == 1 && selectedPersonId == 0)
            {
                MessageBox.Show("Lütfen bir kişi seçiniz.");
                return;
            }

            if (currentStep == 2)
            {
                if (TourTypeComboBox.SelectedItem == null ||
                    PeriodComboBox.SelectedItem == null ||
                    GroupTypeComboBox.SelectedItem == null ||
                    DaysCountComboBox.SelectedItem == null)
                {
                    MessageBox.Show("Lütfen tüm tur bilgilerini doldurunuz.");
                    return;
                }
            }

            if (currentStep == MaxStep)
            {
                MessageBox.Show("KAYIT VERİTABANINA YAZILACAK");
                return;
            }

            SwitchToStep(currentStep + 1);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentStep > 1)
            {
                SwitchToStep(currentStep - 1);
            }
        }

        private void LoadSettingsToComboBoxes()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    // 1. TUR TÜRLERİ
                    TourTypeComboBox.ItemsSource = db.AppSettings
                        .Where(s => (s.Type == "Tur Türü" || s.Type == "TourType") && s.IsActive)
                        .OrderBy(s => s.SortOrder)
                        .Select(s => s.Value)
                        .ToList();

                    // 2. GRUP TÜRLERİ
                    GroupTypeComboBox.ItemsSource = db.AppSettings
                        .Where(s => (s.Type == "Grup Türü" || s.Type == "GroupType") && s.IsActive)
                        .OrderBy(s => s.SortOrder)
                        .Select(s => s.Value)
                        .ToList();

                    // 3. KAÇ GÜNLÜK GRUP
                    DaysCountComboBox.ItemsSource = db.AppSettings
                        .Where(s => (s.Type == "Kaç Günlük Grup" || s.Type == "CountOfDays") && s.IsActive)
                        .OrderBy(s => s.SortOrder)
                        .Select(s => s.Value)
                        .ToList();

                    // 4. DÖNEM SEÇİMİ
                    PeriodComboBox.ItemsSource = db.AppSettings
                        .Where(s => s.Type == "Dönem" && s.IsActive)
                        .OrderBy(s => s.SortOrder)
                        .Select(s => s.Value)
                        .ToList();

                    // 5. VİZE TALEBİ
                    VisaRequestComboBox.ItemsSource = db.AppSettings
                        .Where(x => (x.Type == "Vize Talebi" || x.Type == "VisaType") && x.IsActive)
                        .OrderBy(x => x.SortOrder)
                        .Select(x => x.Value)
                        .ToList();

                    // 6. BİLET TALEBİ
                    TicketRequestComboBox.ItemsSource = db.AppSettings
                        .Where(x => (x.Type == "Bilet Talebi" || x.Type == "TicketType") && x.IsActive)
                        .OrderBy(x => x.SortOrder)
                        .Select(x => x.Value)
                        .ToList();

                    // 7. KARGO TALEBİ
                    CargoRequestComboBox.ItemsSource = db.AppSettings
                        .Where(x => (x.Type == "Kargo Talebi" || x.Type == "CargoType") && x.IsActive)
                        .OrderBy(x => x.SortOrder)
                        .Select(x => x.Value)
                        .ToList();

                    // 8. ODA TÜRÜ
                    RoomTypeComboBox.ItemsSource = db.AppSettings
                        .Where(x => (x.Type == "Oda Türü" || x.Type == "RoomType") && x.IsActive)
                        .OrderBy(x => x.SortOrder)
                        .Select(x => x.Value)
                        .ToList();

                    // 9. KATILIM YAPILACAK ŞEHİR (Başındaki nokta temizlendi ve SQLite uyumlu yapıldı)
                    JoinCityComboBox.ItemsSource = db.AppSettings
                        .Where(x => x.Type == "Katılım Yapılacak Şehir" && x.IsActive)
                        .OrderBy(x => x.SortOrder)
                        .Select(x => x.Value)
                        .ToList();

                    // 10. AKTARMA DURUMU
                    TransferComboBox.ItemsSource = new List<string> { "Var", "Yok" };

                    // 11. AKTARMA ŞEHRİ
                    TransferCityComboBox.ItemsSource = db.AppSettings
                        .Where(x => x.Type == "Aktarma Şehri" && x.IsActive)
                        .OrderBy(x => x.SortOrder)
                        .Select(x => x.Value)
                        .ToList();

                    // 12. PARA BİRİMİ
                    CurrencyComboBox.ItemsSource = db.AppSettings
                        .Where(x => (x.Type == "Para Birimi" || x.Type == "CurrencyType") && x.IsActive)
                        .OrderBy(x => x.SortOrder)
                        .Select(x => x.Value)
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ayarlar verileri yüklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TransferComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // ComboBox elemanının null olup olmadığını ve seçilen değeri güvenli kontrol ediyoruz
            if (TransferComboBox.SelectedItem != null && TransferComboBox.SelectedItem.ToString() == "Var")
            {
                TransferPanel.Visibility = Visibility.Visible;
            }
            else
            {
                TransferPanel.Visibility = Visibility.Collapsed;

                // Eğer kullanıcı "Yok" seçerse, daha önce seçtiği aktarma şehrini temizleyelim
                if (TransferCityComboBox != null)
                    TransferCityComboBox.SelectedIndex = -1;
            }
        }

        private void PaymentFields_TextChanged(object sender, TextChangedEventArgs e)
        {
            decimal total = 0;
            decimal paid = 0;

            decimal.TryParse(PriceTextBox.Text, out total);
            decimal.TryParse(PaidTextBox.Text, out paid);

            RemainingDebtTextBox.Text = (total - paid).ToString("N2");
        }

        private void LoadPreviewData()
        {
            PreviewTextBlock.Text =
                $"Kişi: {QuickFirstNameTextBox.Text} {QuickLastNameTextBox.Text}\n" +
                $"Tur: {TourTypeComboBox.Text}\n";
        }

        private void TourDetails_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (currentStep == 2)
            {
                bool isValid =
                    TourTypeComboBox.SelectedItem != null &&
                    PeriodComboBox.SelectedItem != null &&
                    GroupTypeComboBox.SelectedItem != null &&
                    DaysCountComboBox.SelectedItem != null;

                NextButton.IsEnabled = isValid;
            }
        }

        private void ToggleActiveButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedPersonId == 0) return;

            using (var db = new AppDbContext())
            {
                var person = db.Persons.FirstOrDefault(x => x.Id == selectedPersonId);

                if (person != null)
                {
                    person.IsActive = true;
                    db.SaveChanges();

                    LoadPersons();

                    ToggleActiveButton.Visibility = Visibility.Collapsed;
                    NextButton.IsEnabled = true;
                }
            }
        }
    }
}
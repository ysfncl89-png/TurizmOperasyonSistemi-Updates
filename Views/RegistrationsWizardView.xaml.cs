using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.ComponentModel;
using System.Windows.Media;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Views
{
    public partial class RegistrationsWizardView : Page
    {
        private int currentStep = 1;
        private int selectedPersonId = 0;
        private ICollectionView _personsView;

        public RegistrationsWizardView()
        {
            InitializeComponent();
            LoadPersons();
        }

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

            LoadSettingsToWizardForm(); // 🔥 İSTENEN EKLENTİ

            QuickFirstNameTextBox.Text = SearchTextBox.Text;
            QuickFirstNameTextBox.Focus();
        }

        // =====================================================
        // ⚡ 1. SETTINGS LOAD (DINAMIK COMBOBOX DOLDURMA)
        // =====================================================
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

        // =====================================================
        // ⚡ 2. DOĞUM TARİHİ -> YAŞ ARALIĞI OTOMATİK SEÇİM
        // =====================================================
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

        // =====================================================
        // 👁️ 3. KAYIT + ÖNİZLEME + DB INSERT
        // =====================================================
        private void SaveQuickPersonButton_Click(object sender, RoutedEventArgs e)
        {
            // ==========================================
            // 🚨 1. ADIM: ZORUNLU ALAN KONTROLLERİ
            // ==========================================

            if (string.IsNullOrWhiteSpace(QuickFirstNameTextBox.Text))
            {
                MessageBox.Show("Lütfen kişinin 'Adı' alanını boş bırakmayınız.", "Eksik Bilgi", MessageBoxButton.OK, MessageBoxImage.Warning);
                QuickFirstNameTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(QuickLastNameTextBox.Text))
            {
                MessageBox.Show("Lütfen kişinin 'Soyadı' alanını boş bırakmayınız.", "Eksik Bilgi", MessageBoxButton.OK, MessageBoxImage.Warning);
                QuickLastNameTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(QuickTcTextBox.Text))
            {
                MessageBox.Show("Lütfen 'TC Kimlik No' alanını boş bırakmayınız.", "Eksik Bilgi", MessageBoxButton.OK, MessageBoxImage.Warning);
                QuickTcTextBox.Focus();
                return;
            }

            if (QuickBirthDatePicker.SelectedDate == null)
            {
                MessageBox.Show("Lütfen 'Doğum Tarihi' alanını seçiniz.", "Eksik Bilgi", MessageBoxButton.OK, MessageBoxImage.Warning);
                QuickBirthDatePicker.Focus();
                return;
            }

            if (QuickGenderComboBox.SelectedValue == null)
            {
                MessageBox.Show("Lütfen 'Cinsiyet' alanını seçiniz.", "Eksik Bilgi", MessageBoxButton.OK, MessageBoxImage.Warning);
                QuickGenderComboBox.Focus();
                return;
            }

            // ==========================================
            // 🔢 2. ADIM: TC KİMLİK NO ÖZEL VALİDASYONLARI
            // ==========================================
            string tcNo = QuickTcTextBox.Text.Trim();

            if (tcNo.Length != 11)
            {
                MessageBox.Show("TC Kimlik Numarası tam olarak 11 haneden oluşmalıdır.", "Hatalı TC Kimlik", MessageBoxButton.OK, MessageBoxImage.Error);
                QuickTcTextBox.Focus();
                return;
            }

            if (!tcNo.All(char.IsDigit))
            {
                MessageBox.Show("TC Kimlik Numarası sadece rakamlardan oluşabilir. Lütfen harf veya özel karakter girmeyiniz.", "Hatalı TC Kimlik", MessageBoxButton.OK, MessageBoxImage.Error);
                QuickTcTextBox.Focus();
                return;
            }

            // ==========================================
            // 🚨 2B. ADIM: MÜKERRER KAYIT (TC KONTROLÜ) - 🔥 YENİ EKLENEN KONTROL
            // ==========================================
            using (var db = new AppDbContext())
            {
                bool tcZatenVarMi = db.Persons.Any(x => x.TcKimlikNo == tcNo && x.IsActive == true);

                if (tcZatenVarMi)
                {
                    MessageBox.Show($"'{tcNo}' TC numaralı kişi sistemde zaten kayıtlıdır!\nOtomatik olarak arama listesine yönlendiriliyorsunuz.",
                                    "Mükerrer Kayıt Hatası",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Error);

                    // 1. Formu temizle ve paneli gizle (Zaten senin yazdığın metot)
                    ClearQuickAddForm();

                    // 2. Girilen TC'yi ana arama kutusuna aktar ki listede direkt önümüze çıksın
                    SearchTextBox.Text = tcNo;

                    // 3. Arama kutusuna odaklan ki kullanıcı direkt seçebilsin
                    SearchTextBox.Focus();

                    return; // İşlemi bitir, sonraki kodlara geçme
                }
            }

            // ==========================================
            // 🔓 TÜM KONTROLLERİ GEÇTİ, ŞİMDİ İŞLEME DEVAM ET
            // ==========================================

            string passportType = QuickPassportTypeComboBox.SelectedValue?.ToString() ?? "Belirtilmedi";
            string gender = QuickGenderComboBox.SelectedValue.ToString();
            string ageRange = QuickAgeRangeComboBox.SelectedValue?.ToString() ?? "Yetişkin";
            string bloodType = QuickBloodTypeComboBox.SelectedValue?.ToString() ?? "Belirtilmedi";

            // 📜 Kayıt Önizleme Onay Ekranı
            string previewMessage = $"👤 YENİ KİŞİ KAYIT ÖNİZLEME\n" +
                                   $"----------------------------------------\n" +
                                   $"TC Kimlik No: {tcNo}\n" +
                                   $"Adı Soyadı: {QuickFirstNameTextBox.Text.Trim()} {QuickLastNameTextBox.Text.Trim()}\n" +
                                   $"Telefon: {QuickPhoneTextBox.Text.Trim()}\n" +
                                   $"Doğum Tarihi: {QuickBirthDatePicker.SelectedDate.Value.ToShortDateString()} ({ageRange})\n" +
                                   $"Cinsiyet: {gender} | Kan Grubu: {bloodType}\n" +
                                   $"Pasaport No & Türü: {QuickPassportNumberTextBox.Text.Trim()} ({passportType})\n" +
                                   $"Sağlık / Aşı Durumu: {QuickHealthStatusTextBox.Text.Trim()} / {QuickVaccineStatusTextBox.Text.Trim()}\n" +
                                   $"Adres: {QuickAddressTextBox.Text.Trim()}\n\n" +
                                   $"Yukarıdaki bilgilerin doğruluğunu onaylıyor musunuz?";

            MessageBoxResult result = MessageBox.Show(previewMessage, "Kayıt Önizleme ve Veritabanı Onayı", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
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
                        HealthStatus = QuickHealthStatusTextBox.Text.Trim(),
                        VaccineStatus = QuickVaccineStatusTextBox.Text.Trim(),
                        PassportNumber = QuickPassportNumberTextBox.Text.Trim(),
                        PassportType = passportType,
                        PassportStartDate = QuickPassportStartDatePicker.SelectedDate,
                        PassportEndDate = QuickPassportEndDatePicker.SelectedDate,
                        IsActive = true
                    };

                    db.Persons.Add(newPerson);
                    db.SaveChanges();

                    selectedPersonId = newPerson.Id;

                    MessageBox.Show($"{newPerson.FirstName} {newPerson.LastName} başarıyla kaydedildi. Tur Bilgileri adımına geçiliyor.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);

                    ClearQuickAddForm();

                    // Veritabanına yeni eklenen kişinin DataGrid'e yansıması için listeyi yeniliyoruz
                    LoadPersons();

                    SwitchToStep(2);
                }
            }
        }
        private void TextBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            // Girilen karakter rakam değilse yazılmasını engeller
            e.Handled = !e.Text.All(char.IsDigit);
        }

        // =====================================================
        // 🧹 TEMİZLEME
        // =====================================================
        private void ClearQuickAddForm()
        {
            QuickTcTextBox.Clear();
            QuickFirstNameTextBox.Clear();
            QuickLastNameTextBox.Clear();
            QuickPhoneTextBox.Clear();
            QuickBirthDatePicker.SelectedDate = null;

            QuickPassportNumberTextBox.Clear();
            QuickAddressTextBox.Clear();
            QuickHealthStatusTextBox.Clear();
            QuickVaccineStatusTextBox.Clear();

            QuickPassportTypeComboBox.SelectedIndex = -1;
            QuickGenderComboBox.SelectedIndex = -1;
            QuickAgeRangeComboBox.SelectedIndex = -1;
            QuickBloodTypeComboBox.SelectedIndex = -1;

            QuickPassportStartDatePicker.SelectedDate = null;
            QuickPassportEndDatePicker.SelectedDate = null;

            QuickAddFormPanel.Visibility = Visibility.Collapsed;
        }

        private void SwitchToStep(int step)
        {
            currentStep = step;

            if (step == 2)
            {
                Step1Grid.Visibility = Visibility.Collapsed;
                Step2Grid.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Visible;
                NextButton.IsEnabled = false;
            }
            else
            {
                Step1Grid.Visibility = Visibility.Visible;
                Step2Grid.Visibility = Visibility.Collapsed;
                BackButton.Visibility = Visibility.Collapsed;
                NextButton.IsEnabled = true;
            }
        }

        private void ToggleActiveButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedPersonId == 0) return;

            using (var db = new AppDbContext())
            {
                var person = db.Persons.FirstOrDefault(p => p.Id == selectedPersonId);

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

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentStep == 1 && selectedPersonId != 0)
                SwitchToStep(2);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentStep == 2)
                SwitchToStep(1);
        }

        private void TourDetails_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }
    }
}
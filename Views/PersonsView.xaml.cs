using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Views
{
    public partial class PersonsView : Page
    {
        private int selectedPersonId = 0;

        public PersonsView()
        {
            InitializeComponent();

            LoadPersons();

            LoadComboBox(GenderComboBox, "Cinsiyet");

            LoadComboBox(AgeRangeComboBox, "Yaş Aralığı");
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(FirstNameTextBox.Text) ||
                    string.IsNullOrWhiteSpace(LastNameTextBox.Text) ||
                    string.IsNullOrWhiteSpace(TcTextBox.Text) ||
                    GenderComboBox.SelectedItem == null ||
                    BirthDatePicker.SelectedDate == null)
                {
                    MessageBox.Show("Lütfen zorunlu alanları doldurun.");
                    return;
                }

                if (TcTextBox.Text.Length != 11)
                {
                    MessageBox.Show("TC Kimlik No 11 haneli olmalıdır.");
                    return;
                }

                if (!TcTextBox.Text.All(char.IsDigit))
                {
                    MessageBox.Show("TC Kimlik No sadece rakamlardan oluşmalıdır.");
                    return;
                }

                using (var controlDb = new AppDbContext())
                {
                    bool tcExists;

                    if (selectedPersonId > 0)
                    {
                        tcExists = controlDb.Persons.Any(x =>
                            x.TcKimlikNo == TcTextBox.Text &&
                            x.Id != selectedPersonId);
                    }
                    else
                    {
                        tcExists = controlDb.Persons.Any(x =>
                            x.TcKimlikNo == TcTextBox.Text);
                    }

                    if (tcExists)
                    {
                        MessageBox.Show("Bu TC Kimlik No zaten kayıtlı.");
                        return;
                    }
                }

                using (var db = new AppDbContext())
                {
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
                        person = new Person();

                        db.Persons.Add(person);
                    }

                    person.FirstName = FirstNameTextBox.Text;

                    person.LastName = LastNameTextBox.Text;

                    person.TcKimlikNo = TcTextBox.Text;

                    person.PhoneNumber = PhoneTextBox.Text;

                    person.Gender =
                        (GenderComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

                    person.BirthDate =
                        BirthDatePicker.SelectedDate ?? DateTime.Now;

                    person.BloodType =
                        (BloodComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

                    person.PassportType =
                        (PassportTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

                    person.PassportNumber = PassportTextBox.Text;

                    person.PassportStartDate =
                        PassportStartPicker.SelectedDate ?? DateTime.Now;

                    person.PassportEndDate =
                        PassportEndPicker.SelectedDate ?? DateTime.Now;

                    person.Address = AddressTextBox.Text;

                    person.HealthStatus = HealthTextBox.Text;

                    person.VaccineStatus = VaccineTextBox.Text;

                    person.AgeRange =
                        AgeRangeComboBox.SelectedItem?.ToString() ?? "";

                    db.SaveChanges();

                    MessageBox.Show("Kayıt başarıyla kaydedildi.");

                    selectedPersonId = 0;

                    ClearForm();

                    LoadPersons();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        private void LoadPersons()
        {
            using (var db = new AppDbContext())
            {
                if (ShowPassiveCheckBox.IsChecked == true)
                {
                    PersonsDataGrid.ItemsSource =
                        db.Persons.ToList();
                }
                else
                {
                    PersonsDataGrid.ItemsSource =
                        db.Persons
                        .Where(x => x.IsActive)
                        .ToList();
                }
            }
        }

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
            }
        }

        private void ClearForm()
        {
            FirstNameTextBox.Clear();

            LastNameTextBox.Clear();

            TcTextBox.Clear();

            PhoneTextBox.Clear();

            PassportTextBox.Clear();

            AddressTextBox.Clear();

            HealthTextBox.Clear();

            VaccineTextBox.Clear();

            GenderComboBox.SelectedIndex = -1;

            BloodComboBox.SelectedIndex = -1;

            PassportTypeComboBox.SelectedIndex = -1;

            AgeRangeComboBox.SelectedIndex = -1;

            BirthDatePicker.SelectedDate = null;

            PassportStartPicker.SelectedDate = null;

            PassportEndPicker.SelectedDate = null;
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            using (var db = new AppDbContext())
            {
                string search = SearchTextBox.Text.ToLower();

                var filteredPersons = db.Persons
                    .Where(x =>
                        x.FirstName.ToLower().Contains(search) ||
                        x.LastName.ToLower().Contains(search) ||
                        x.TcKimlikNo.Contains(search) ||
                        x.PhoneNumber.Contains(search))
                    .ToList();

                PersonsDataGrid.ItemsSource = filteredPersons;
            }
        }

        private void PersonsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PersonsDataGrid.SelectedItem is Person selectedPerson)
            {
                selectedPersonId = selectedPerson.Id;

                FirstNameTextBox.Text = selectedPerson.FirstName;

                LastNameTextBox.Text = selectedPerson.LastName;

                TcTextBox.Text = selectedPerson.TcKimlikNo;

                PhoneTextBox.Text = selectedPerson.PhoneNumber;

                AddressTextBox.Text = selectedPerson.Address;

                PassportTextBox.Text = selectedPerson.PassportNumber;

                HealthTextBox.Text = selectedPerson.HealthStatus;

                VaccineTextBox.Text = selectedPerson.VaccineStatus;

                BirthDatePicker.SelectedDate = selectedPerson.BirthDate;

                PassportStartPicker.SelectedDate = selectedPerson.PassportStartDate;

                PassportEndPicker.SelectedDate = selectedPerson.PassportEndDate;

                SelectComboBoxItem(GenderComboBox, selectedPerson.Gender);

                SelectComboBoxItem(BloodComboBox, selectedPerson.BloodType);

                SelectComboBoxItem(PassportTypeComboBox, selectedPerson.PassportType);

                SetComboBox(AgeRangeComboBox, selectedPerson.AgeRange);
            }
        }

        private void SelectComboBoxItem(ComboBox comboBox, string value)
        {
            foreach (ComboBoxItem item in comboBox.Items)
            {
                if (item.Content.ToString() == value)
                {
                    comboBox.SelectedItem = item;

                    break;
                }
            }
        }

        private void SetComboBox(ComboBox comboBox, string value)
        {
            foreach (var item in comboBox.Items)
            {
                if (item.ToString() == value)
                {
                    comboBox.SelectedItem = item;

                    break;
                }
            }
        }

        private void PassiveButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedPersonId == 0)
            {
                MessageBox.Show("Lütfen pasif yapılacak kişiyi seçin.");
                return;
            }

            using (var db = new AppDbContext())
            {
                var person = db.Persons
                    .FirstOrDefault(x => x.Id == selectedPersonId);

                if (person != null)
                {
                    person.IsActive = false;

                    db.SaveChanges();

                    MessageBox.Show("Kişi pasif yapıldı.");

                    selectedPersonId = 0;

                    ClearForm();

                    LoadPersons();
                }
            }
        }

        private void ShowPassiveCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            LoadPersons();
        }
    }
}
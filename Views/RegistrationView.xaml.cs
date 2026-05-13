using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;
using Microsoft.EntityFrameworkCore;

namespace TurizmOperasyonSistemi.Views
{
    public partial class RegistrationView : Page
    {
        private Person? selectedPerson;
        private int selectedRegistrationId = 0;

        public RegistrationView()
        {
            InitializeComponent();

            LoadPersons();

            LoadRegistrations();
        }

        private void LoadPersons()
        {
            using (var db = new AppDbContext())
            {
                PersonsDataGrid.ItemsSource = db.Persons
                    .Where(x => x.IsActive)
                    .ToList();
            }
        }

        private void LoadRegistrations()
        {
            using (var db = new AppDbContext())
            {
                var registrations = db.Registrations
                    .Select(x => new
                    {
                        x.Id,

                        AdSoyad =
                            x.Person.FirstName + " " +
                            x.Person.LastName,

                        x.TourType,
                        x.Period,
                        x.GroupType,
                        x.DayCount,
                        x.VisaStatus,
                        x.TicketStatus,
                        x.CargoStatus,
                        x.RoomType,
                        x.TourPrice,
                        x.PaidAmount,
                        x.RemainingDebt,
                        x.IsActive
                    })
                    .ToList();

                RegistrationsDataGrid.ItemsSource = registrations;
            }
        }

        private void PersonSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            using (var db = new AppDbContext())
            {
                string search = PersonSearchTextBox.Text.ToLower();

                var persons = db.Persons
                    .Where(x =>
                        x.IsActive &&
                        (
                            x.FirstName.ToLower().Contains(search) ||
                            x.LastName.ToLower().Contains(search) ||
                            x.TcKimlikNo.Contains(search)
                        ))
                    .ToList();

                PersonsDataGrid.ItemsSource = persons;
            }
        }

        private void PersonsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PersonsDataGrid.SelectedItem is Person person)
            {
                selectedPerson = person;
            }
        }

        private void SaveRegistrationButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedPerson == null)
            {
                MessageBox.Show("Lütfen bir kişi seçin.");
                return;
            }

            using (var db = new AppDbContext())
            {
                if (selectedRegistrationId == 0)
                {
                    string selectedTourType =
                        (TourTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

                    string selectedGroupType =
                        (GroupTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

                    int selectedDayCount =
                        int.Parse((DayCountComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "0");

                    bool exists = db.Registrations.Any(x =>
                        x.PersonId == selectedPerson.Id &&
                        x.TourType == selectedTourType &&
                        x.Period == PeriodTextBox.Text &&
                        x.GroupType == selectedGroupType &&
                        x.DayCount == selectedDayCount &&
                        x.IsActive &&
                        x.Id != selectedRegistrationId);

                    if (exists)
                    {
                        MessageBox.Show("Bu kişi zaten aynı tur grubuna kayıtlı.");

                        return;
                    }
                }

                Registration registration;

                if (selectedRegistrationId > 0)
                {
                    registration = db.Registrations.Find(selectedRegistrationId);

                    if (registration == null)
                        return;
                }
                else
                {
                    registration = new Registration();

                    registration.PersonId = selectedPerson.Id;

                    db.Registrations.Add(registration);
                }

                registration.TourType =
                    (TourTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

                registration.Period = PeriodTextBox.Text;

                registration.GroupType =
                    (GroupTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

                registration.DayCount =
                    int.Parse((DayCountComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "0");

                registration.VisaStatus =
                    (VisaComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

                registration.TicketStatus =
                    (TicketComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

                registration.CargoStatus =
                    (CargoComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

                registration.RoomType =
                    (RoomComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

                registration.TourPrice =
                    decimal.TryParse(PriceTextBox.Text, out decimal price) ? price : 0;

                registration.PaidAmount =
                    decimal.TryParse(PaidTextBox.Text, out decimal paid) ? paid : 0;

                registration.RemainingDebt =
                    registration.TourPrice - registration.PaidAmount;

                db.SaveChanges();

                if (selectedRegistrationId > 0)
                {
                    MessageBox.Show("Kayıt güncellendi.");
                }
                else
                {
                    MessageBox.Show("Tur kaydı oluşturuldu.");
                }

                LoadRegistrations();
            }
        }

        private void RegistrationsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RegistrationsDataGrid.SelectedItem == null)
                return;

            dynamic selected = RegistrationsDataGrid.SelectedItem;

            selectedRegistrationId = selected.Id;

            PeriodTextBox.Text = selected.Period;
            PriceTextBox.Text = selected.TourPrice.ToString();
            PaidTextBox.Text = selected.PaidAmount.ToString();

            SetComboBox(TourTypeComboBox, selected.TourType);
            SetComboBox(GroupTypeComboBox, selected.GroupType);
            SetComboBox(DayCountComboBox, selected.DayCount.ToString());
            SetComboBox(VisaComboBox, selected.VisaStatus);
            SetComboBox(TicketComboBox, selected.TicketStatus);
            SetComboBox(CargoComboBox, selected.CargoStatus);
            SetComboBox(RoomComboBox, selected.RoomType);
        }

        private void SetComboBox(ComboBox comboBox, string value)
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
    }
}
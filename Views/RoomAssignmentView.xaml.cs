using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Views
{
    public partial class RoomAssignmentView : Page
    {
        private int selectedRegistrationId = 0;

        public RoomAssignmentView()
        {
            InitializeComponent();

            LoadComboBox(TourTypeComboBox, "TourType");
            LoadComboBox(GroupTypeComboBox, "GroupType");
            LoadComboBox(RoomComboBox, "RoomType");

            LoadPeriods();
            LoadDayCounts();
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

        private void LoadPeriods()
        {
            using (var db = new AppDbContext())
            {
                PeriodComboBox.ItemsSource = db.Registrations
                    .Select(x => x.Period)
                    .Distinct()
                    .ToList();
            }
        }

        private void LoadDayCounts()
        {
            DayCountComboBox.ItemsSource =
                new int[] { 7, 10, 14, 15, 20, 30 };
        }

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            using (var db = new AppDbContext())
            {
                var query = db.Registrations
                    .Where(x => x.IsActive)
                    .AsQueryable();

                // TOUR TYPE

                if (TourTypeComboBox.SelectedItem != null)
                {
                    string selectedTourType =
                        TourTypeComboBox.SelectedItem.ToString();

                    query = query.Where(x =>
                        x.TourType == selectedTourType);
                }

                // PERIOD

                if (PeriodComboBox.SelectedItem != null)
                {
                    string selectedPeriod =
                        PeriodComboBox.SelectedItem.ToString();

                    query = query.Where(x =>
                        x.Period == selectedPeriod);
                }

                // GROUP TYPE

                if (GroupTypeComboBox.SelectedItem != null)
                {
                    string selectedGroupType =
                        GroupTypeComboBox.SelectedItem.ToString();

                    query = query.Where(x =>
                        x.GroupType == selectedGroupType);
                }

                // DAY COUNT

                if (DayCountComboBox.SelectedItem != null)
                {
                    int selectedDayCount =
                        (int)DayCountComboBox.SelectedItem;

                    query = query.Where(x =>
                        x.DayCount == selectedDayCount);
                }

                var registrations = query
                    .Select(x => new
                    {
                        x.Id,

                        AdSoyad =
                            x.Person.FirstName + " " +
                            x.Person.LastName,

                        x.RoomType,

                        OdaDurumu =
                            db.RoomAssignments.Any(r =>
                                r.RegistrationId == x.Id &&
                                r.IsActive)
                            ? "Yerleştirildi"
                            : "Bekliyor"
                    })
                    .ToList();

                RegistrationsDataGrid.ItemsSource = registrations;
            }
        }

        private void AssignButton_Click(object sender, RoutedEventArgs e)
        {
            if (RegistrationsDataGrid.SelectedItem == null)
            {
                MessageBox.Show("Lütfen kişi seçin.");
                return;
            }

            if (RoomComboBox.SelectedItem == null)
            {
                MessageBox.Show("Lütfen oda tipi seçin.");
                return;
            }

            dynamic selected = RegistrationsDataGrid.SelectedItem;

            selectedRegistrationId = selected.Id;

            using (var db = new AppDbContext())
            {
                var registration = db.Registrations
                    .Find(selectedRegistrationId);

                if (registration == null)
                    return;

                string selectedRoomType =
                    RoomComboBox.SelectedItem?.ToString() ?? "";

                if (registration.RoomType != selectedRoomType)
                {
                    MessageBox.Show(
                        "Kişinin oda tercihi ile seçilen oda uyuşmuyor.");

                    return;
                }

                var room = db.Rooms
                    .FirstOrDefault(x =>
                        x.RoomType == selectedRoomType &&
                        x.IsActive);

                if (room == null)
                {
                    MessageBox.Show("Uygun oda bulunamadı.");
                    return;
                }

                bool alreadyAssigned =
                    db.RoomAssignments.Any(x =>
                        x.RegistrationId == registration.Id &&
                        x.IsActive);

                if (alreadyAssigned)
                {
                    MessageBox.Show("Bu kişi zaten odaya yerleştirildi.");
                    return;
                }

                RoomAssignment assignment =
                    new RoomAssignment();

                assignment.RegistrationId =
                    registration.Id;

                assignment.RoomId =
                    room.Id;

                db.RoomAssignments.Add(assignment);

                db.SaveChanges();

                MessageBox.Show("Kişi odaya yerleştirildi.");

                LoadButton_Click(null, null);
            }
        }
    }
}
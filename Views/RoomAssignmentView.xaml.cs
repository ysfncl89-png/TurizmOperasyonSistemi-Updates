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
                comboBox.ItemsSource = db.AppSettings
                    .Where(x => x.Type == type && x.IsActive)
                    .Select(x => x.Value)
                    .ToList();
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
                string tourType =
                    TourTypeComboBox.SelectedItem?.ToString() ?? "";

                string period =
                    PeriodComboBox.SelectedItem?.ToString() ?? "";

                string groupType =
                    GroupTypeComboBox.SelectedItem?.ToString() ?? "";

                int dayCount =
                    DayCountComboBox.SelectedItem != null
                    ? (int)DayCountComboBox.SelectedItem
                    : 0;

                var registrations = db.Registrations
                    .Where(x =>
                        x.IsActive &&
                        x.TourType == tourType &&
                        x.Period == period &&
                        x.GroupType == groupType &&
                        x.DayCount == dayCount)
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
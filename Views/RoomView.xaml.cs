using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Views
{
    public partial class RoomView : Page
    {
        private int selectedRoomId = 0;

        public RoomView()
        {
            InitializeComponent();

            LoadHotels();
            LoadRooms();
        }

        private void LoadHotels()
        {
            using (var db = new AppDbContext())
            {
                HotelComboBox.ItemsSource = db.Hotels
                    .Where(x => x.IsActive)
                    .ToList();

                HotelComboBox.DisplayMemberPath = "HotelName";
                HotelComboBox.SelectedValuePath = "Id";
            }
        }

        private void LoadRooms()
        {
            using (var db = new AppDbContext())
            {
                var rooms = db.Rooms
                    .Include(x => x.Hotel)
                    .AsQueryable();

                if (ShowPassiveCheckBox.IsChecked == false)
                {
                    rooms = rooms.Where(x => x.IsFull == false);
                }

                RoomsDataGrid.ItemsSource = rooms.ToList();
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (HotelComboBox.SelectedItem == null)
                {
                    MessageBox.Show("Lütfen otel seçin.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(RoomNumberTextBox.Text))
                {
                    MessageBox.Show("Oda numarası zorunludur.");
                    return;
                }

                using (var db = new AppDbContext())
                {
                    Room room;

                    if (selectedRoomId > 0)
                    {
                        room = db.Rooms.Find(selectedRoomId);

                        if (room == null)
                            return;
                    }
                    else
                    {
                        room = new Room();
                        db.Rooms.Add(room);
                    }

                    room.HotelId = (int)HotelComboBox.SelectedValue;

                    room.RoomNumber = RoomNumberTextBox.Text;

                    room.RoomType =
                        (RoomTypeComboBox.SelectedItem as ComboBoxItem)?
                        .Content?.ToString() ?? "";

                    int capacity = 0;

                    int.TryParse(CapacityTextBox.Text, out capacity);

                    room.Capacity = capacity;

                    room.GenderType =
                        (GenderTypeComboBox.SelectedItem as ComboBoxItem)?
                        .Content?.ToString() ?? "";

                    db.SaveChanges();

                    if (selectedRoomId > 0)
                    {
                        MessageBox.Show("Oda güncellendi.");
                    }
                    else
                    {
                        MessageBox.Show("Oda kaydedildi.");
                    }

                    ClearForm();
                    LoadRooms();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        private void ClearForm()
        {
            RoomNumberTextBox.Clear();
            CapacityTextBox.Clear();

            HotelComboBox.SelectedIndex = -1;
            RoomTypeComboBox.SelectedIndex = -1;
            GenderTypeComboBox.SelectedIndex = -1;

            selectedRoomId = 0;
        }

        private void RoomsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RoomsDataGrid.SelectedItem is Room selectedRoom)
            {
                selectedRoomId = selectedRoom.Id;

                RoomNumberTextBox.Text = selectedRoom.RoomNumber;

                CapacityTextBox.Text = selectedRoom.Capacity.ToString();

                SetComboBoxValue(RoomTypeComboBox, selectedRoom.RoomType);

                SetComboBoxValue(GenderTypeComboBox, selectedRoom.GenderType);

                HotelComboBox.SelectedValue = selectedRoom.HotelId;
            }
        }

        private void PassiveButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedRoomId == 0)
            {
                MessageBox.Show("Lütfen oda seçin.");
                return;
            }

            using (var db = new AppDbContext())
            {
                var room = db.Rooms.Find(selectedRoomId);

                if (room == null)
                    return;

                room.IsFull = true;

                db.SaveChanges();
            }

            MessageBox.Show("Oda pasif yapıldı.");

            ClearForm();
            LoadRooms();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            using (var db = new AppDbContext())
            {
                string search = SearchTextBox.Text.ToLower();

                var rooms = db.Rooms
                    .Include(x => x.Hotel)
                    .Where(x =>
                        x.RoomNumber.ToLower().Contains(search) ||
                        x.RoomType.ToLower().Contains(search) ||
                        x.GenderType.ToLower().Contains(search) ||
                        x.Hotel.HotelName.ToLower().Contains(search));

                if (ShowPassiveCheckBox.IsChecked == false)
                {
                    rooms = rooms.Where(x => x.IsFull == false);
                }

                RoomsDataGrid.ItemsSource = rooms.ToList();
            }
        }

        private void ShowPassiveCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            LoadRooms();
        }

        private void SetComboBoxValue(ComboBox comboBox, string value)
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
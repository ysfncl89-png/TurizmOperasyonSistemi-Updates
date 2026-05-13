using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Views
{
    public partial class HotelView : Page
    {
        private int selectedHotelId = 0;

        public HotelView()
        {
            InitializeComponent();
            LoadHotels();
        }

        private void LoadHotels()
        {
            using (var db = new AppDbContext())
            {
                if (ShowPassiveCheckBox.IsChecked == true)
                {
                    HotelsDataGrid.ItemsSource = db.Hotels.ToList();
                }
                else
                {
                    HotelsDataGrid.ItemsSource =
                        db.Hotels
                        .Where(x => x.IsActive)
                        .ToList();
                }
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(HotelNameTextBox.Text))
                {
                    MessageBox.Show("Otel adı zorunludur.");
                    return;
                }

                using (var db = new AppDbContext())
                {
                    Hotel hotel;

                    if (selectedHotelId > 0)
                    {
                        hotel = db.Hotels.Find(selectedHotelId);

                        if (hotel == null)
                            return;
                    }
                    else
                    {
                        hotel = new Hotel();
                        db.Hotels.Add(hotel);
                    }

                    hotel.HotelName = HotelNameTextBox.Text;
                    hotel.City = CityTextBox.Text;
                    hotel.Country = CountryTextBox.Text;

                    int capacity = 0;
                    int.TryParse(CapacityTextBox.Text, out capacity);

                    hotel.Capacity = capacity;

                    hotel.Notes = NotesTextBox.Text;

                    if (selectedHotelId == 0)
                    {
                        hotel.IsActive = true;
                    }

                    db.SaveChanges();

                    if (selectedHotelId > 0)
                    {
                        MessageBox.Show("Otel güncellendi.");
                    }
                    else
                    {
                        MessageBox.Show("Otel kaydedildi.");
                    }

                    ClearForm();
                    LoadHotels();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        private void ClearForm()
        {
            HotelNameTextBox.Clear();
            CityTextBox.Clear();
            CountryTextBox.Clear();
            CapacityTextBox.Clear();
            NotesTextBox.Clear();

            selectedHotelId = 0;
        }

        private void HotelsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HotelsDataGrid.SelectedItem is Hotel selectedHotel)
            {
                selectedHotelId = selectedHotel.Id;

                HotelNameTextBox.Text = selectedHotel.HotelName;
                CityTextBox.Text = selectedHotel.City;
                CountryTextBox.Text = selectedHotel.Country;
                CapacityTextBox.Text = selectedHotel.Capacity.ToString();
                NotesTextBox.Text = selectedHotel.Notes;
            }
        }

        private void PassiveButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedHotelId == 0)
            {
                MessageBox.Show("Lütfen bir otel seçin.");
                return;
            }

            using (var db = new AppDbContext())
            {
                var hotel = db.Hotels.Find(selectedHotelId);

                if (hotel == null)
                    return;

                hotel.IsActive = false;

                db.SaveChanges();
            }

            MessageBox.Show("Otel pasif yapıldı.");

            ClearForm();
            LoadHotels();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            using (var db = new AppDbContext())
            {
                string search = SearchTextBox.Text.ToLower();

                var hotels = db.Hotels.Where(x =>
                    x.HotelName.ToLower().Contains(search) ||
                    x.City.ToLower().Contains(search) ||
                    x.Country.ToLower().Contains(search));

                if (ShowPassiveCheckBox.IsChecked == false)
                {
                    hotels = hotels.Where(x => x.IsActive);
                }

                HotelsDataGrid.ItemsSource = hotels.ToList();
            }
        }

        private void ShowPassiveCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            LoadHotels();
        }
    }
}
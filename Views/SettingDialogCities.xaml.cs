using System.Collections.Generic;
using System.Windows;

namespace TurizmOperasyonSistemi.Views
{
    public partial class SettingDialogCities : Window
    {
        public List<string> SelectedCities { get; set; } = new List<string>();

        // Şehir modeli
        public class CityItem
        {
            public string Name { get; set; }
        }

        public SettingDialogCities(string title)
        {
            InitializeComponent();

            this.Title = title;

            // 81 il
            var cities = new List<CityItem>
            {
                new CityItem { Name = "Adana" },
                new CityItem { Name = "Adıyaman" },
                new CityItem { Name = "Afyonkarahisar" },
                new CityItem { Name = "Ağrı" },
                new CityItem { Name = "Amasya" },
                new CityItem { Name = "Ankara" },
                new CityItem { Name = "Antalya" },
                new CityItem { Name = "Artvin" },
                new CityItem { Name = "Aydın" },
                new CityItem { Name = "Balıkesir" },
                new CityItem { Name = "Bilecik" },
                new CityItem { Name = "Bingöl" },
                new CityItem { Name = "Bitlis" },
                new CityItem { Name = "Bolu" },
                new CityItem { Name = "Burdur" },
                new CityItem { Name = "Bursa" },
                new CityItem { Name = "Çanakkale" },
                new CityItem { Name = "Çankırı" },
                new CityItem { Name = "Çorum" },
                new CityItem { Name = "Denizli" },
                new CityItem { Name = "Diyarbakır" },
                new CityItem { Name = "Edirne" },
                new CityItem { Name = "Elazığ" },
                new CityItem { Name = "Erzincan" },
                new CityItem { Name = "Erzurum" },
                new CityItem { Name = "Eskişehir" },
                new CityItem { Name = "Gaziantep" },
                new CityItem { Name = "Giresun" },
                new CityItem { Name = "Gümüşhane" },
                new CityItem { Name = "Hakkari" },
                new CityItem { Name = "Hatay" },
                new CityItem { Name = "Isparta" },
                new CityItem { Name = "Mersin" },
                new CityItem { Name = "İstanbul" },
                new CityItem { Name = "İzmir" },
                new CityItem { Name = "Kars" },
                new CityItem { Name = "Kastamonu" },
                new CityItem { Name = "Kayseri" },
                new CityItem { Name = "Kırıkkale" },
                new CityItem { Name = "Kırklareli" },
                new CityItem { Name = "Kırşehir" },
                new CityItem { Name = "Kocaeli" },
                new CityItem { Name = "Konya" },
                new CityItem { Name = "Kütahya" },
                new CityItem { Name = "Malatya" },
                new CityItem { Name = "Manisa" },
                new CityItem { Name = "Kahramanmaraş" },
                new CityItem { Name = "Mardin" },
                new CityItem { Name = "Muğla" },
                new CityItem { Name = "Muş" },
                new CityItem { Name = "Nevşehir" },
                new CityItem { Name = "Niğde" },
                new CityItem { Name = "Ordu" },
                new CityItem { Name = "Rize" },
                new CityItem { Name = "Sakarya" },
                new CityItem { Name = "Samsun" },
                new CityItem { Name = "Siirt" },
                new CityItem { Name = "Sinop" },
                new CityItem { Name = "Sivas" },
                new CityItem { Name = "Tekirdağ" },
                new CityItem { Name = "Tokat" },
                new CityItem { Name = "Trabzon" },
                new CityItem { Name = "Tunceli" },
                new CityItem { Name = "Uşak" },
                new CityItem { Name = "Van" },
                new CityItem { Name = "Yozgat" },
                new CityItem { Name = "Zonguldak" },
                new CityItem { Name = "Aksaray" },
                new CityItem { Name = "Bayburt" },
                new CityItem { Name = "Karaman" },
                new CityItem { Name = "Batman" },
                new CityItem { Name = "Şırnak" },
                new CityItem { Name = "Bartın" },
                new CityItem { Name = "Ardahan" },
                new CityItem { Name = "Iğdır" },
                new CityItem { Name = "Yalova" },
                new CityItem { Name = "Karabük" },
                new CityItem { Name = "Kilis" },
                new CityItem { Name = "Osmaniye" },
                new CityItem { Name = "Düzce" }
            };

            CitiesDataGrid.ItemsSource = cities;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedCities.Clear();

            foreach (CityItem item in CitiesDataGrid.SelectedItems)
            {
                SelectedCities.Add(item.Name);
            }

            MessageBox.Show($"{SelectedCities.Count} şehir seçildi.");

            this.DialogResult = true;
            this.Close();
        }
    }
}
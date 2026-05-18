using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace TurizmOperasyonSistemi.Views
{
    public partial class SettingDialogCities : Window
    {
        public List<string> SelectedCities { get; set; } = new List<string>();
        public string SelectedCity { get; internal set; }

        public SettingDialogCities()
        {
            InitializeComponent();

            // Türkiye’deki 81 il
            var cities = new List<string> {
                "Adana","Adıyaman","Afyonkarahisar","Ağrı","Amasya","Ankara","Antalya","Artvin","Aydın",
                "Balıkesir","Bilecik","Bingöl","Bitlis","Bolu","Burdur","Bursa","Çanakkale","Çankırı","Çorum",
                "Denizli","Diyarbakır","Edirne","Elazığ","Erzincan","Erzurum","Eskişehir","Gaziantep","Giresun","Gümüşhane",
                "Hakkari","Hatay","Isparta","Mersin","İstanbul","İzmir","Kars","Kastamonu","Kayseri","Kırıkkale","Kırklareli",
                "Kırşehir","Kocaeli","Konya","Kütahya","Malatya","Manisa","Kahramanmaraş","Mardin","Muğla","Muş",
                "Nevşehir","Niğde","Ordu","Rize","Sakarya","Samsun","Siirt","Sinop","Sivas","Tekirdağ","Tokat",
                "Trabzon","Tunceli","Uşak","Van","Yozgat","Zonguldak","Aksaray","Bayburt","Karaman","Kırıkkale","Batman",
                "Şırnak","Bartın","Ardahan","Iğdır","Yalova","Karabük","Kilis","Osmaniye","Düzce"
            };

            CitiesDataGrid.ItemsSource = cities.ConvertAll(c => new { Name = c });
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedCities.Clear();
            foreach (dynamic item in CitiesDataGrid.SelectedItems)
            {
                SelectedCities.Add(item.Name);
            }

            // Burada dilersen DB'ye kaydedebilirsin

            MessageBox.Show($"{SelectedCities.Count} şehir seçildi.");
            this.Close();
        }
        private void SettingButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            string settingName = button.Content.ToString();

            if (settingName == "Katılım Yapılacak Şehir")
            {
                var dialog = new SettingDialogCities();
                dialog.ShowDialog();
                return;
            }

            // Diğer kartlar için mevcut kod
        }
    }
}
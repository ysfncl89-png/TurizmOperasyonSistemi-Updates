using System.Windows;
using System.Windows.Controls;

namespace TurizmOperasyonSistemi.Views
{

    public partial class SettingsView : Page
    {
        private List<string> cities = new List<string> {
                "Adana","Adıyaman","Afyonkarahisar","Ağrı","Amasya","Ankara","Antalya","Artvin","Aydın",
                "Balıkesir","Bilecik","Bingöl","Bitlis","Bolu","Burdur","Bursa","Çanakkale","Çankırı","Çorum",
                "Denizli","Diyarbakır","Edirne","Elazığ","Erzincan","Erzurum","Eskişehir","Gaziantep","Giresun","Gümüşhane",
                "Hakkari","Hatay","Isparta","Mersin","İstanbul","İzmir","Kars","Kastamonu","Kayseri","Kırıkkale","Kırklareli",
                "Kırşehir","Kocaeli","Konya","Kütahya","Malatya","Manisa","Kahramanmaraş","Mardin","Muğla","Muş",
                "Nevşehir","Niğde","Ordu","Rize","Sakarya","Samsun","Siirt","Sinop","Sivas","Tekirdağ","Tokat",
                "Trabzon","Tunceli","Uşak","Van","Yozgat","Zonguldak","Aksaray","Bayburt","Karaman","Kırıkkale","Batman",
                "Şırnak","Bartın","Ardahan","Iğdır","Yalova","Karabük","Kilis","Osmaniye","Düzce"
            };

        public SettingsView()

        {
            InitializeComponent();

        }

        private void SettingButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;

            string settingName = button.Content.ToString();
            string settingType = "";

            switch (settingName)
            {
                case "Tur Türü":
                    settingType = "Tur Türü";
                    break;

                case "Grup Türü":
                    settingType = "Grup Türü";
                    break;

                case "Oda Türü":
                    settingType = "Oda Türü";
                    break;

                case "Vize Talebi":
                    settingType = "Vize Talebi";
                    break;

                case "Bilet Talebi":
                    settingType = "Bilet Talebi";
                    break;

                case "Kargo Talebi":
                    settingType = "Kargo Talebi";
                    break;
                     
                case "Para Birimi":
                    settingType = "Para Birimi";
                    break;

                case "Katılım Yapılacak Şehir":
                    settingType = "Katılım Yapılacak Şehir";
                    break;

                case "Aktarma Var mı ?":
                    settingType = "Aktarma Var mı ?";
                    break;

                case "Aktarma Şehri":
                    settingType = "Aktarma Şehri";
                    break;

                case "Dönem":
                    settingType = "Dönem";
                    break;

                case "Kaç Günlük Grup":
                    settingType = "Kaç Günlük Grup";
                    break;


                default:
                    settingType = settingName;
                    break;
            }

            if (settingType == "Dönem")
            {
                PeriodDialog dialog = new PeriodDialog();

                dialog.ShowDialog();
            }
            else if (settingType == "Katılım Yapılacak Şehir"
                  || settingType == "Aktarma Şehri")
            {
                SettingDialogCities dialog = new SettingDialogCities(settingType);

                dialog.ShowDialog();
            }
            else
            {
                SettingDialog dialog = new SettingDialog(settingType);

                dialog.ShowDialog();
            }
        }

    }
}
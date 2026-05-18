using System.Windows;
using System.Windows.Controls;

namespace TurizmOperasyonSistemi.Views
{
    public partial class SettingsView : Page
    {
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
                    settingType = "TourType";
                    break;

                case "Grup Türü":
                    settingType = "GroupType";
                    break;

                case "Oda Türü":
                    settingType = "RoomType";
                    break;

                case "Vize Talebi":
                    settingType = "VisaType";
                    break;

                case "Bilet Talebi":
                    settingType = "TicketType";
                    break;

                case "Kargo Talebi":
                    settingType = "CargoType";
                    break;

                case "Para Birimi":
                    settingType = "CurrencyType";
                    break;

                case "Dönem":
                    settingType = "Dönem";
                    break;

                case "Kaç Günlük Grup":
                    settingType = "CountOfDays";
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
            else
            {
                SettingDialog dialog = new SettingDialog(settingType);

                dialog.ShowDialog();
            }
        }

    }
}
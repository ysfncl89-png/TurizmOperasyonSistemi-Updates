using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TurizmOperasyonSistemi.Views
{
    /// <summary>
    /// HotelAllocationView.xaml etkileşim mantığı
    /// </summary>
    public partial class HotelAllocationView : UserControl
    {
        private int currentStep = 1;

        public HotelAllocationView()
        {
            InitializeComponent();

            // TODO: Veritabanından ComboBox'ları dolduracak metotlar buraya gelecek.
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentStep == 1)
            {
                // 1. Adımdan 2. Adıma Geçiş
                Step1Grid.Visibility = Visibility.Collapsed;
                Step2Grid.Visibility = Visibility.Visible;

                Tab1.Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#334155"));

                Tab2.Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#06B6D4")); // Turkuaz

                BackButton.Visibility = Visibility.Visible;

                currentStep = 2;
            }
            else if (currentStep == 2)
            {
                // 2. Adımdan 3. Adıma (Odalama Motoruna) Geçiş
                Step2Grid.Visibility = Visibility.Collapsed;
                Step3Grid.Visibility = Visibility.Visible;

                Tab2.Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#334155"));

                Tab3.Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#06B6D4")); // Turkuaz

                NextButton.Content = "Kaydet ve Bitir";

                currentStep = 3;
            }
            else if (currentStep == 3)
            {
                // Veritabanına kaydetme aşaması
                MessageBox.Show(
                    "Odalama işlemi başarıyla kaydedildi!",
                    "Başarılı",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentStep == 3)
            {
                Step3Grid.Visibility = Visibility.Collapsed;
                Step2Grid.Visibility = Visibility.Visible;

                Tab3.Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#334155"));

                Tab2.Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#06B6D4"));

                NextButton.Content = "İleri ▶";

                currentStep = 2;
            }
            else if (currentStep == 2)
            {
                Step2Grid.Visibility = Visibility.Collapsed;
                Step1Grid.Visibility = Visibility.Visible;

                Tab2.Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#334155"));

                Tab1.Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#06B6D4"));

                BackButton.Visibility = Visibility.Collapsed;

                currentStep = 1;
            }
        }
    }
}
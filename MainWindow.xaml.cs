using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TurizmOperasyonSistemi.Views;

namespace TurizmOperasyonSistemi
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            MainFrame.Navigate(new HomeView());
        }

        private void ResetMenuColors()
        {
            HomeButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2937"));
            PersonsButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2937"));
            RegistrationButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2937"));
            HotelButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2937"));
            ListsButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2937"));
            SettingsButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2937"));
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            ResetMenuColors();

            HomeButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#06B6D4"));

            MainFrame.Navigate(new HomeView());
        }

        private void PersonsButton_Click(object sender, RoutedEventArgs e)
        {
            ResetMenuColors();

            PersonsButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B5CF6"));

            MainFrame.Navigate(new PersonsView());
        }

        private void RegistrationButton_Click(object sender, RoutedEventArgs e)
        {
            ResetMenuColors();

            RegistrationButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));

            MainFrame.Navigate(new RegistrationView());
        }

        private void HotelButton_Click(object sender, RoutedEventArgs e)
        {
            ResetMenuColors();

            HotelButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));

            MainFrame.Navigate(new HotelView());
        }
        private void RoomAssignmentButton_Click(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(new RoomAssignmentView());
        }

        private void ListsButton_Click(object sender, RoutedEventArgs e)
        {
            ResetMenuColors();

            ListsButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));

            MainFrame.Navigate(new ListsView());
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ResetMenuColors();

            SettingsButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EC4899"));

            MainFrame.Navigate(new SettingsView());

        }
    }
}
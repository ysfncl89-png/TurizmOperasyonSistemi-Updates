using TOSLite.Mobile.Views;

namespace TOSLite.Mobile
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
            MobileTheme.Apply(this);
        }

        private async void OnPersonsClicked(object? sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(PersonsPage));
        }

        private async void OnRegistrationsClicked(object? sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(RegistrationsPage));
        }

        private async void OnSettingsClicked(object? sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(SettingsPage));
        }

        private async void OnRoomingClicked(object? sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(RoomingPage));
        }

        private async void OnMobileSyncClicked(object? sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(MobileSyncPage));
        }
    }
}

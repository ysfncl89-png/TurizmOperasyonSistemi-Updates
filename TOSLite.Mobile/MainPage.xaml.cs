using TOSLite.Mobile.Views;

namespace TOSLite.Mobile
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
        }

        private async void OnPersonsClicked(object? sender, EventArgs e)
        {
            await NavigateFromCardAsync(sender, "//persons");
        }

        private async void OnRegistrationsClicked(object? sender, EventArgs e)
        {
            await NavigateFromCardAsync(sender, "//registrations");
        }

        private async void OnRoomingClicked(object? sender, EventArgs e)
        {
            await NavigateFromCardAsync(sender, nameof(RoomingPage));
        }

        private async void OnPaymentsClicked(object? sender, EventArgs e)
        {
            await NavigateFromCardAsync(sender, "//payments");
        }

        private async void OnCargoDocumentsClicked(object? sender, EventArgs e)
        {
            await NavigateFromCardAsync(sender, "cargo-documents");
        }

        private async void OnListsClicked(object? sender, EventArgs e)
        {
            await NavigateFromCardAsync(sender, "lists");
        }

        private async void OnSettingsClicked(object? sender, EventArgs e)
        {
            await NavigateFromCardAsync(sender, "settings");
        }

        private async void OnMobileSyncClicked(object? sender, EventArgs e)
        {
            await NavigateFromCardAsync(sender, nameof(MobileSyncPage));
        }

        private static async Task NavigateFromCardAsync(object? sender, string route)
        {
            if (sender is TapGestureRecognizer { Parent: VisualElement card })
            {
                await card.ScaleTo(0.985, 70, Easing.CubicOut);
                await card.ScaleTo(1, 90, Easing.CubicIn);
            }

            await Shell.Current.GoToAsync(route);
        }
    }
}

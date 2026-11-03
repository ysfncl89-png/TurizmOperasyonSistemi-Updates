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
            await Shell.Current.GoToAsync(nameof(PersonsPage));
        }
    }
}

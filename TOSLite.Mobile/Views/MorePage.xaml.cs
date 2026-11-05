namespace TOSLite.Mobile.Views;

public partial class MorePage : ContentPage
{
    public MorePage()
    {
        InitializeComponent();
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

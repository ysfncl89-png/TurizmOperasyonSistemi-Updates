namespace TOSLite.Mobile
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Routing.RegisterRoute(nameof(Views.PersonsPage), typeof(Views.PersonsPage));
            Routing.RegisterRoute(nameof(Views.RegistrationsPage), typeof(Views.RegistrationsPage));
            Routing.RegisterRoute(nameof(Views.RegistrationFormPage), typeof(Views.RegistrationFormPage));
            Routing.RegisterRoute(nameof(Views.SettingsPage), typeof(Views.SettingsPage));
            Routing.RegisterRoute(nameof(Views.RoomingPage), typeof(Views.RoomingPage));
            Routing.RegisterRoute(nameof(Views.MobileSyncPage), typeof(Views.MobileSyncPage));
        }
    }
}

namespace TOSLite.Mobile
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Routing.RegisterRoute(nameof(Views.PersonsPage), typeof(Views.PersonsPage));
        }
    }
}

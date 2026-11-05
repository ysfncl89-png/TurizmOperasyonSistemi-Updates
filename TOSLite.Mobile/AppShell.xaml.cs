using Microsoft.Extensions.DependencyInjection;
using TOSLite.Mobile.Views;

namespace TOSLite.Mobile
{
    public partial class AppShell : Shell
    {
        public AppShell(IServiceProvider services)
        {
            InitializeComponent();

            var tabBar = new TabBar();
            tabBar.Items.Add(CreateTab("Ana Sayfa", "home", "home.svg", () => new MainPage()));
            tabBar.Items.Add(CreateTab("Kişiler", "persons", "people.svg", () => services.GetRequiredService<PersonsPage>()));
            tabBar.Items.Add(CreateTab("Tur Kayıtları", "registrations", "tour.svg", () => services.GetRequiredService<RegistrationsPage>()));
            tabBar.Items.Add(CreateTab("Finans", "payments", "list.svg", () => services.GetRequiredService<PaymentsPage>()));
            tabBar.Items.Add(CreateTab("Daha Fazla+", "more", "settings.svg", () => new MorePage()));
            Items.Add(tabBar);

            Routing.RegisterRoute(nameof(RegistrationFormPage), typeof(RegistrationFormPage));
            Routing.RegisterRoute(nameof(RoomingPage), typeof(RoomingPage));
            Routing.RegisterRoute(nameof(MobileSyncPage), typeof(MobileSyncPage));
            Routing.RegisterRoute("lists", typeof(ListsPage));
            Routing.RegisterRoute("settings", typeof(SettingsPage));
            Routing.RegisterRoute("cargo-documents", typeof(CargoDocumentsPage));
        }

        private static Tab CreateTab(string title, string route, string icon, Func<Page> pageFactory)
        {
            var tab = new Tab { Title = title };
            tab.Items.Add(new ShellContent
            {
                Title = title,
                Route = route,
                Icon = icon,
                ContentTemplate = new DataTemplate(pageFactory)
            });
            return tab;
        }
    }
}

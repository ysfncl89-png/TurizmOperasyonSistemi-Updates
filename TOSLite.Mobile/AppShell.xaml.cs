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

            RegisterRoute<RegistrationFormPage>(services, nameof(RegistrationFormPage));
            RegisterRoute<RoomingPage>(services, nameof(RoomingPage));
            RegisterRoute<MobileSyncPage>(services, nameof(MobileSyncPage));
            RegisterRoute<ListsPage>(services, "lists");
            RegisterRoute<SettingsPage>(services, "settings");
            RegisterRoute<CargoDocumentsPage>(services, "cargo-documents");
        }

        private static void RegisterRoute<TPage>(IServiceProvider services, string route) where TPage : Page =>
            Routing.RegisterRoute(route, new ServiceRouteFactory<TPage>(services));

        private sealed class ServiceRouteFactory<TPage>(IServiceProvider services) : RouteFactory where TPage : Page
        {
            public override Element GetOrCreate() => services.GetRequiredService<TPage>();

            public override Element GetOrCreate(IServiceProvider routeServices) => services.GetRequiredService<TPage>();
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

using Microsoft.Extensions.Logging;
using TOSLite.Mobile.Data;
using TOSLite.Mobile.Views;
using TOSLite.Mobile.Services;
using ZXing.Net.Maui.Controls;
using CommunityToolkit.Maui;

namespace TOSLite.Mobile
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .UseBarcodeReader()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            builder.Services.AddSingleton<PersonRepository>();
            builder.Services.AddSingleton<RegistrationRepository>();
            builder.Services.AddSingleton<SettingsRepository>();
            builder.Services.AddSingleton<RoomAllocationRepository>();
            builder.Services.AddSingleton<PaymentRepository>();
            builder.Services.AddSingleton<CargoQueueRepository>();
            builder.Services.AddSingleton<MobileSyncService>();
            builder.Services.AddSingleton<IPassportTextRecognitionService, PassportTextRecognitionService>();
            builder.Services.AddSingleton<ListExportService>();
            builder.Services.AddSingleton<DownloadFileService>();
            builder.Services.AddTransient<PersonsPage>();
            builder.Services.AddTransient<RegistrationsPage>();
            builder.Services.AddTransient<PaymentsPage>();
            builder.Services.AddTransient<CargoDocumentsPage>();
            builder.Services.AddTransient<RegistrationFormPage>();
            builder.Services.AddTransient<SettingsPage>();
            builder.Services.AddTransient<RoomingPage>();
            builder.Services.AddTransient<ListsPage>();
            builder.Services.AddTransient<MobileSyncPage>();

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}

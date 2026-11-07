using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.Content.PM;
using Android.OS;

namespace TOSLite.Mobile
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void AttachBaseContext(Context? @base)
        {
            if (@base is null)
            {
                base.AttachBaseContext(null);
                return;
            }

            var locale = global::Java.Util.Locale.ForLanguageTag("tr-TR");
            global::Java.Util.Locale.Default = locale;
            var configuration = new Configuration(@base.Resources?.Configuration);
            configuration.SetLocale(locale);
            configuration.SetLayoutDirection(locale);
            base.AttachBaseContext(@base.CreateConfigurationContext(configuration));
        }
    }
}

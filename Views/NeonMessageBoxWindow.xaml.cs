using System.Windows;
using System.Windows.Media.Effects;

namespace TurizmOperasyonSistemi.Views
{
    public partial class NeonMessageBoxWindow : Window
    {
        public NeonMessageBoxWindow()
        {
            InitializeComponent();

            // apply neon-like shadow to primary button
            var neon = new DropShadowEffect
            {
                Color = System.Windows.Media.Color.FromArgb(0xAA, 0x06, 0xB6, 0xD4),
                BlurRadius = 44,
                ShadowDepth = 10,
                Opacity = 1.0
            };

            BtnPrimary.Effect = neon;
            BtnPrimary.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(6, 182, 212));
            BtnPrimary.Foreground = System.Windows.Media.Brushes.White;
            BtnPrimary.FontWeight = FontWeights.Bold;

            BtnSecondary.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(36, 41, 48));
            BtnSecondary.Foreground = System.Windows.Media.Brushes.White;

            BtnPrimary.Click += (s, e) => { this.DialogResult = true; this.Close(); };
            BtnSecondary.Click += (s, e) => { this.DialogResult = false; this.Close(); };
        }

        public static bool? Show(
            string message,
            string title = "Uyarı",
            bool showCancel = false,
            string primaryButtonText = "Tamam",
            string secondaryButtonText = "İptal")
        {
            var win = new NeonMessageBoxWindow();
            win.TitleText.Text = title;
            win.MessageText.Text = message;
            win.BtnPrimary.Content = primaryButtonText;
            win.BtnSecondary.Content = secondaryButtonText;
            win.BtnSecondary.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
            // Safely set owner: only when there is a shown/loaded main window. Setting Owner to a
            // Window that has not been shown throws InvalidOperationException on some systems.
            try
            {
                var app = Application.Current;
                Window owner = null;
                if (app != null)
                {
                    // Prefer the active window if available
                    owner = app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? app.MainWindow;
                }

                if (owner != null && owner.IsLoaded && owner.IsVisible)
                {
                    win.Owner = owner;
                    win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
                else
                {
                    win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }
            catch
            {
                // Fallback: center on screen if any error occurs while setting owner
                win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            return win.ShowDialog();
        }
    }
}
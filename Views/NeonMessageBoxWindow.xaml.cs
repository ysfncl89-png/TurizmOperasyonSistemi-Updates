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

        public static bool? Show(string message, string title = "Uyarı", bool showCancel = false)
        {
            var win = new NeonMessageBoxWindow();
            win.TitleText.Text = title;
            win.MessageText.Text = message;
            win.BtnSecondary.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
            win.Owner = Application.Current?.MainWindow;
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            return win.ShowDialog();
        }
    }
}
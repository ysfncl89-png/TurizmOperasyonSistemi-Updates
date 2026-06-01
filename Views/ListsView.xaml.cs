using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using TurizmOperasyonSistemi.Data;

namespace TurizmOperasyonSistemi.Views
{
    /// <summary>
    /// ListsView.xaml etkileşim mantığı
    /// </summary>
    public partial class ListsView : Page
    {
        public ListsView()
        {
            InitializeComponent();
        }
        private void Border_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is Border b)
            {
                // find option buttons inside the border's visual tree (direct children in Grid)
                var grid = b.Child as Grid;
                if (grid == null) return;

                var options = grid.Children.OfType<Button>().Where(btn => btn.Name != "tk_center" && btn.Name != "kb_center" && btn.Name != "oo_center" && btn.Name != "od_center" && btn.Name != "kg_center").ToList();
                if (options.Count == 0) return;

                ArrangeOptionsInPolygon(grid, options);
            }
        }

        private void Border_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is Border b)
            {
                var grid = b.Child as Grid;
                if (grid == null) return;

                var options = grid.Children.OfType<Button>().Where(btn => btn.Name != "tk_center" && btn.Name != "kb_center" && btn.Name != "oo_center" && btn.Name != "od_center" && btn.Name != "kg_center").ToList();
                foreach (var btn in options)
                {
                    var tt = btn.RenderTransform as TranslateTransform;
                    if (tt == null) continue;
                    // animate back to 0,0
                    var daX = new System.Windows.Media.Animation.DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(180)));
                    var daY = new System.Windows.Media.Animation.DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(180)));
                    tt.BeginAnimation(TranslateTransform.XProperty, daX);
                    tt.BeginAnimation(TranslateTransform.YProperty, daY);

                    // fade out label and button
                    var daOp = new System.Windows.Media.Animation.DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(120)));
                    btn.BeginAnimation(Button.OpacityProperty, daOp);
                    var lbl = btn.Content as StackPanel;
                    // label inside StackPanel second child
                    if (lbl != null && lbl.Children.Count > 1 && lbl.Children[1] is TextBlock tb)
                    {
                        tb.BeginAnimation(TextBlock.OpacityProperty, daOp);
                    }
                }
            }
        }

        private void ArrangeOptionsInPolygon(Grid grid, System.Collections.Generic.List<Button> options)
        {
            int n = options.Count;
            if (n <= 0 || grid == null) return;

            // compute button diameter (prefer ActualWidth)
            double diameter = 0;
            foreach (var b in options)
            {
                if (b.ActualWidth > 0) { diameter = b.ActualWidth; break; }
                if (b.Width > 0) { diameter = b.Width; break; }
            }
            if (diameter <= 0) diameter = 80;

            // safe minimal and maximal radius based on grid size and button size
            double minDim = Math.Min(grid.ActualWidth, grid.ActualHeight);
            if (double.IsNaN(minDim) || minDim <= 0) minDim = diameter * 4.0; // fallback derived from button size

            // desired base radius: half of smaller grid dimension, minus button diameter and a margin
            double baseRadius = minDim / 2.0 - diameter - 10.0;

            // compute clamping bounds so buttons stay inside the card
            double minRadius = Math.Max(diameter / 2.0 + 4.0, 20.0);
            double maxRadius = Math.Max(minRadius, (minDim / 2.0) - (diameter / 2.0) - 4.0);

            double clampedRadius = baseRadius;
            if (double.IsNaN(clampedRadius) || double.IsInfinity(clampedRadius)) clampedRadius = minRadius;
            clampedRadius = Math.Min(clampedRadius, maxRadius);
            clampedRadius = Math.Max(clampedRadius, minRadius);

            // start at top (12 o'clock)
            double startAngleRad = -Math.PI / 2.0;

            // Special-case: Tur Kayıtları (card2) with exactly 7 options -> use fixed coordinates (no automatic radius)
            if (grid.Name == "card2" && n == 7)
            {
                var coords = new System.Collections.Generic.Dictionary<string, (double x, double y)>
                {
                    { "tk_o1", (0, -130) },   // Genel
                    { "tk_o2", (-95, -70) },  // Vize
                    { "tk_o3", (130, 0) },    // Bilet
                    { "tk_o4", (95, 90) },    // Aktarma
                    { "tk_o5", (-95, 90) },   // Havayolu
                    { "tk_o6", (-130, 0) },   // Referans
                    { "tk_o7", (95, -70) }    // Pasaport
                };

                foreach (var btn in options)
                {
                    if (!coords.TryGetValue(btn.Name, out var pos))
                    {
                        // if a button name is unexpected, skip it
                        continue;
                    }

                    double x = pos.x;
                    double y = pos.y;

                    if (!(btn.RenderTransform is TranslateTransform tt))
                    {
                        tt = new TranslateTransform(0, 0);
                        btn.RenderTransform = tt;
                    }

                    var animX = new System.Windows.Media.Animation.DoubleAnimation(x, new Duration(TimeSpan.FromMilliseconds(420)))
                    {
                        EasingFunction = new System.Windows.Media.Animation.ElasticEase { Oscillations = 2, Springiness = 5, EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                    };
                    var animY = new System.Windows.Media.Animation.DoubleAnimation(y, new Duration(TimeSpan.FromMilliseconds(420)))
                    {
                        EasingFunction = new System.Windows.Media.Animation.ElasticEase { Oscillations = 2, Springiness = 5, EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                    };
                    tt.BeginAnimation(TranslateTransform.XProperty, animX);
                    tt.BeginAnimation(TranslateTransform.YProperty, animY);

                    var daOp = new System.Windows.Media.Animation.DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(180)));
                    btn.BeginAnimation(Button.OpacityProperty, daOp);
                    if (btn.Content is StackPanel sp && sp.Children.Count > 1 && sp.Children[1] is TextBlock tb)
                    {
                        tb.BeginAnimation(TextBlock.OpacityProperty, daOp);
                    }
                }

                return;
            }

            // General polygon distribution: evenly spaced around circle
            for (int i = 0; i < n; i++)
            {
                // clockwise distribution
                double angle = startAngleRad - i * (2.0 * Math.PI / n);
                double x = Math.Cos(angle) * clampedRadius;
                double y = Math.Sin(angle) * clampedRadius;

                var btn = options[i];
                if (!(btn.RenderTransform is TranslateTransform tt))
                {
                    tt = new TranslateTransform(0, 0);
                    btn.RenderTransform = tt;
                }

                var animX = new System.Windows.Media.Animation.DoubleAnimation(x, new Duration(TimeSpan.FromMilliseconds(420)))
                {
                    EasingFunction = new System.Windows.Media.Animation.ElasticEase { Oscillations = 2, Springiness = 5, EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };
                var animY = new System.Windows.Media.Animation.DoubleAnimation(y, new Duration(TimeSpan.FromMilliseconds(420)))
                {
                    EasingFunction = new System.Windows.Media.Animation.ElasticEase { Oscillations = 2, Springiness = 5, EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };
                tt.BeginAnimation(TranslateTransform.XProperty, animX);
                tt.BeginAnimation(TranslateTransform.YProperty, animY);

                var daOp = new System.Windows.Media.Animation.DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(180)));
                btn.BeginAnimation(Button.OpacityProperty, daOp);
                if (btn.Content is StackPanel sp2 && sp2.Children.Count > 1 && sp2.Children[1] is TextBlock tb2)
                {
                    tb2.BeginAnimation(TextBlock.OpacityProperty, daOp);
                }
            }
        }
        private void LoadComboBox(ComboBox comboBox, string type)
        {
            using (var db = new AppDbContext())
            {
                var values = db.AppSettings
                    .Where(x => x.Type == type && x.IsActive)
                    .OrderBy(x => x.SortOrder)
                    .Select(x => x.Value)
                    .ToList();

                values.Insert(0, "Seçiniz");

                comboBox.ItemsSource = values;
            }
        }
    }
}

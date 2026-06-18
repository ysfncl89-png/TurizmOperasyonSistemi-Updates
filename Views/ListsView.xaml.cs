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
using TurizmOperasyonSistemi.Models;
using TurizmOperasyonSistemi.Views;

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
            // attach click handler for m button
            try
            {
                var btn = this.FindName("kb_opt1") as Button;
                if (btn != null)
                {
                    btn.Click += Kb_opt1_Click;
                }

                var btn2 = this.FindName("kb_opt2") as Button;
                if (btn2 != null)
                {
                    btn2.Click += Kb_opt2_Click;
                }
                // Tur Kayıtları - Genel butonu
                var tkGenel = this.FindName("tk_o1") as Button;
                if (tkGenel != null)
                {
                    tkGenel.Click += Tk_o1_Click;
                }
                // other tur kayitlari buttons -> wire to open PersonsGeneralView with specific mode
                var tkVize = this.FindName("tk_o2") as Button;
                if (tkVize != null) tkVize.Click += (s, e) => OpenPersonsGeneralWithMode(PersonsGeneralView.ViewMode.Vize);
                var tkBilet = this.FindName("tk_o3") as Button;
                if (tkBilet != null) tkBilet.Click += (s, e) => OpenPersonsGeneralWithMode(PersonsGeneralView.ViewMode.Bilet);
                var tkAktarma = this.FindName("tk_o4") as Button;
                if (tkAktarma != null) tkAktarma.Click += (s, e) => OpenPersonsGeneralWithMode(PersonsGeneralView.ViewMode.Aktarma);
                var tkHavayolu = this.FindName("tk_o5") as Button;
                if (tkHavayolu != null) tkHavayolu.Click += (s, e) => OpenPersonsGeneralWithMode(PersonsGeneralView.ViewMode.Havayolu);
                var tkReferans = this.FindName("tk_o6") as Button;
                if (tkReferans != null) tkReferans.Click += (s, e) => OpenPersonsGeneralWithMode(PersonsGeneralView.ViewMode.Referans);
                var tkPasaport = this.FindName("tk_o7") as Button;
                if (tkPasaport != null) tkPasaport.Click += (s, e) => OpenPersonsGeneralWithMode(PersonsGeneralView.ViewMode.Pasaport);
                var odlCenter = this.FindName("odl_center") as Button;
                if (odlCenter != null) odlCenter.Click += Odl_center_Click;
                var odCenter = this.FindName("od_center") as Button;
                if (odCenter != null) odCenter.Click += Od_center_Click;
                var kgCenter = this.FindName("kg_center") as Button;
                if (kgCenter != null) kgCenter.Click += Kg_center_Click;
            }
            catch
            {
                // ignore
            }

            // Subscribe to registration changes so passive kayıtlı kişiler kartlardaki listelere gitmesin
            try
            {
                TurizmOperasyonSistemi.Services.NotificationCenter.Subscribe(OnRegistrationNotification);
            }
            catch { }

        }

        private void Kg_center_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var personsView = new PersonsGeneralView();
                try { personsView.ConfigureMode(PersonsGeneralView.ViewMode.Genel); } catch { }
                // enable cargo-only behavior so ApplyFilters will restrict results
                try { personsView.CargoOnlyMode = true; } catch { }
                try { personsView.CargoListCardMode = true; } catch { }

                if (this.NavigationService != null)
                {
                    this.NavigationService.Navigate(personsView);
                    return;
                }

                var win = new Window
                {
                    Title = "Kargo Listesi - Filtreler",
                    Content = new Frame { Content = personsView, NavigationUIVisibility = NavigationUIVisibility.Hidden },
                    Width = 1100,
                    Height = 700,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Application.Current?.MainWindow
                };
                win.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void Od_center_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var personsView = new PersonsGeneralView();
                // ensure PaymentsListCardMode set before configuring so UI overrides apply
                try { personsView.PaymentsListCardMode = true; } catch { }
                try { personsView.ConfigureMode(PersonsGeneralView.ViewMode.Genel); } catch { }

                if (this.NavigationService != null)
                {
                    this.NavigationService.Navigate(personsView);
                    return;
                }

                var win = new Window
                {
                    Title = "Tur Kayıtları - Genel (Ödeme Listesi)",
                    Content = new Frame { Content = personsView, NavigationUIVisibility = NavigationUIVisibility.Hidden },
                    Width = 1100,
                    Height = 700,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Application.Current?.MainWindow
                };
                win.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }



        private void OnRegistrationNotification(string key, object payload)
        {
            try
            {
                if (key != "RegistrationSaved") return;

                int? personId = null;
                bool addToPool = true;

                if (payload is int pid) personId = pid;
                else if (payload != null)
                {
                    try
                    {
                        var t = payload.GetType();
                        var pidProp = t.GetProperty("PersonId");
                        if (pidProp != null)
                        {
                            var v = pidProp.GetValue(payload);
                            if (v is int vi) personId = vi;
                            else if (v is long vl) personId = (int)vl;
                        }
                        var addProp = t.GetProperty("AddToPool");
                        if (addProp != null)
                        {
                            var av = addProp.GetValue(payload);
                            if (av is bool b) addToPool = b;
                        }
                    }
                    catch { }
                }

                if (!personId.HasValue) return;

                // Eğer payload'ta AddToPool bilgisi yoksa DB'ye bakarak kişinin aktif bir kaydı olup olmadığını kontrol et
                try
                {
                    var t = payload?.GetType();
                    var addPropPresent = false;
                    if (t != null)
                    {
                        addPropPresent = t.GetProperty("AddToPool") != null;
                    }

                    if (!addPropPresent)
                    {
                        try
                        {
                            using var db = new TurizmOperasyonSistemi.Data.AppDbContext();
                            var hasActive = db.Registrations.Any(r => r.PersonId == personId.Value && r.IsActive);
                            addToPool = hasActive;
                        }
                        catch { }
                    }
                }
                catch { }

                // Eğer AddToPool == false ise, sayfada bulunan herhangi bir ListBox / ItemsControl içinden bu kişiyi çıkar
                if (!addToPool)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            RemovePersonFromAllLists(personId.Value);
                        }
                        catch { }
                    }));
                }
                else
                {
                    // Aktif hale gelirse, basit yaklaşım: yoksa yeniden yükleme gerekli olabilir; şimdilik pasif -> aktif olaylarında hiçbir şey yapmıyoruz
                }
            }
            catch { }
        }

        private void RemovePersonFromAllLists(int personId)
        {
            // find all ItemsControl and ListBox children and remove matching items
            try
            {
                var toProcess = new List<ItemsControl>();
                CollectVisualChildren(this, toProcess);

                foreach (var ic in toProcess)
                {
                    try
                    {
                        // prefer ItemsSource if it's a mutable IList
                        var src = ic.ItemsSource as System.Collections.IList;
                        if (src != null)
                        {
                            RemoveFromIListByPersonId(src, personId);
                            continue;
                        }

                        // fallback to Items collection
                        var items = ic.Items;
                        if (items != null && items.Count > 0)
                        {
                            RemoveFromIListByPersonId(items, personId);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void RemoveFromIListByPersonId(System.Collections.IList list, int personId)
        {
            if (list == null) return;
            var snapshot = new object[list.Count];
            list.CopyTo(snapshot, 0);
            foreach (var item in snapshot)
            {
                try
                {
                    if (item == null) continue;
                    // if item is Person
                    var personType = item.GetType();
                    var idProp = personType.GetProperty("Id");
                    if (idProp != null)
                    {
                        var val = idProp.GetValue(item);
                        if (val is int vid && vid == personId)
                        {
                            try { list.Remove(item); } catch { }
                            continue;
                        }
                    }

                    // if item has Person or PersonId property
                    var pidProp = personType.GetProperty("PersonId");
                    if (pidProp != null)
                    {
                        var v = pidProp.GetValue(item);
                        if (v is int v2 && v2 == personId)
                        {
                            try { list.Remove(item); } catch { }
                            continue;
                        }
                    }
                    var personProp = personType.GetProperty("Person");
                    if (personProp != null)
                    {
                        var p = personProp.GetValue(item);
                        if (p != null)
                        {
                            var pIdProp = p.GetType().GetProperty("Id");
                            if (pIdProp != null)
                            {
                                var pv = pIdProp.GetValue(p);
                                if (pv is int pvi && pvi == personId)
                                {
                                    try { list.Remove(item); } catch { }
                                    continue;
                                }
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private void CollectVisualChildren(DependencyObject parent, List<ItemsControl> accum)
        {
            if (parent == null) return;
            int c = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < c; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is ItemsControl ic)
                {
                    accum.Add(ic);
                }
                CollectVisualChildren(child, accum);
            }
        }
        private void Kb_opt2_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var personsView = new PersonsHealthView();

                // If running inside a NavigationService-enabled host, navigate there
                if (this.NavigationService != null)
                {
                    this.NavigationService.Navigate(personsView);
                    return;
                }

                // Otherwise open a new window with the view
                var win = new Window
                {
                    Title = "Sağlık - Kişiler",
                    Content = new Frame { Content = personsView, NavigationUIVisibility = NavigationUIVisibility.Hidden },
                    Width = 900,
                    Height = 700,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Application.Current?.MainWindow
                };
                win.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
        private void Odl_center_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var page = new OdaAramaPage();
                page.IsEmbeddedMode = true;

                // If this view is hosted in a NavigationService, navigate to the page
                if (this.NavigationService != null)
                {
                    this.NavigationService.Navigate(page);
                    return;
                }

                // Otherwise open a new window that hosts the page inside a Frame
                var win = new Window
                {
                    Title = "Oda Listesi",
                    Content = new Frame { Content = page, NavigationUIVisibility = NavigationUIVisibility.Hidden },
                    Width = 1000,
                    Height = 700,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Application.Current?.MainWindow
                };
                win.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void Tk_o1_Click(object sender, RoutedEventArgs e)
        {
            OpenPersonsGeneralWithMode(PersonsGeneralView.ViewMode.Genel);
        }

        private void OpenPersonsGeneralWithMode(PersonsGeneralView.ViewMode mode)
        {
            try
            {
                var personsView = new PersonsGeneralView();
                // mark this instance as opened from Lists -> Tur Kayıtları card so PersonsGeneralView
                // can apply Tur Kayıtları-specific UI rules (hide İşlem/Detay)
                try { personsView.OpenedFromListsTurKayitlari = true; } catch { }
                try { personsView.ConfigureMode(mode); } catch { }

                if (this.NavigationService != null)
                {
                    this.NavigationService.Navigate(personsView);
                    return;
                }

                var win = new Window
                {
                    Title = "Tur Kayıtları - " + mode.ToString(),
                    Content = new Frame { Content = personsView, NavigationUIVisibility = NavigationUIVisibility.Hidden },
                    Width = 1100,
                    Height = 700,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Application.Current?.MainWindow
                };
                win.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void Kb_opt1_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var personsView = new PersonsSearchView();

                // If running inside a NavigationService-enabled host, navigate there
                if (this.NavigationService != null)
                {
                    this.NavigationService.Navigate(personsView);
                    return;
                }

                // Otherwise open a new window with the view
                var win = new Window
                {
                    Title = "Kişiler",
                    Content = new Frame { Content = personsView, NavigationUIVisibility = NavigationUIVisibility.Hidden },
                    Width = 900,
                    Height = 700,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Application.Current?.MainWindow
                };
                win.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
        private void Border_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is Border b)
            {
                // find option buttons inside the border's visual tree (direct children in Grid)
                var grid = b.Child as Grid;
                if (grid == null) return;

                var options = grid.Children.OfType<Button>().Where(btn => btn.Name != "tk_center" && btn.Name != "kb_center" && btn.Name != "od_center" && btn.Name != "odl_center" && btn.Name != "kg_center").ToList();
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

                var options = grid.Children.OfType<Button>().Where(btn => btn.Name != "tk_center" && btn.Name != "kb_center" && btn.Name != "od_center" && btn.Name != "odl_center" && btn.Name != "kg_center").ToList();
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

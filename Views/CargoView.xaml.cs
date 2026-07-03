using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TurizmOperasyonSistemi.Views
{
    public partial class CargoView : UserControl
    {
        public CargoView()
        {
            InitializeComponent();

            // Arka plan resmi kaynakta varsa güvenli şekilde yükle
            this.Loaded += CargoView_Loaded;
        }

        private void CargoView_Loaded(object? sender, System.Windows.RoutedEventArgs e)
        {
            // Tek seferlik deneme
            this.Loaded -= CargoView_Loaded;
            TrySetBackgroundImage();
        }

        private void TrySetBackgroundImage()
        {
            // Deneme dizisi: birkaç farklı pack URI ve yükleme yöntemi ile kaynak aranıp uygulanacak.
            var attempts = new[]
            {
                // Basit pack uri (relative to application)
                "pack://application:,,,/Assets/Backgrounds/CargoBackground.png",
                // Assembly-qualified pack uri
                $"pack://application:,,,/{System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name};component/Assets/Backgrounds/CargoBackground.png",
                // Type-assembly qualified (this assembly)
                $"pack://application:,,,/{typeof(CargoView).Assembly.GetName().Name};component/Assets/Backgrounds/CargoBackground.png"
            };

            foreach (var uriStr in attempts)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(uriStr))
                        continue;

                    var uri = new System.Uri(uriStr, System.UriKind.Absolute);

                    System.Diagnostics.Debug.WriteLine("Trying cargo background uri: " + uri);

                    // İlk olarak Application.GetResourceStream ile dene
                    try
                    {
                        var info = System.Windows.Application.GetResourceStream(uri);
                        if (info != null)
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.StreamSource = info.Stream;
                            bitmap.EndInit();
                            bitmap.Freeze();

                            var brush = new ImageBrush(bitmap)
                            {
                                Stretch = Stretch.UniformToFill,
                                // Opaklığı %30 artırmak için mevcut 0.45 değerini 1.3 ile çarpıyoruz => 0.585
                                Opacity = 0.585
                            };
                            this.Background = brush;
                            System.Diagnostics.Debug.WriteLine("Cargo background image applied successfully via GetResourceStream: " + uri);
                            return;
                        }
                    }
                    catch (System.Exception inner)
                    {
                        System.Diagnostics.Debug.WriteLine("GetResourceStream failed for " + uri + " -> " + inner.Message);
                    }

                    // Eğer üstte başarısız olduysa doğrudan BitmapImage ile pack uri dene
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = uri;
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        bmp.Freeze();

                        var brush2 = new ImageBrush(bmp)
                        {
                            Stretch = Stretch.UniformToFill,
                            // Opaklığı %30 artırmak için mevcut 0.45 değerini 1.3 ile çarpıyoruz => 0.585
                            Opacity = 0.585
                        };
                        this.Background = brush2;
                        System.Diagnostics.Debug.WriteLine("Cargo background image applied successfully via BitmapImage.UriSource: " + uri);
                        return;
                    }
                    catch (System.Exception inner2)
                    {
                        System.Diagnostics.Debug.WriteLine("BitmapImage(Uri) failed for " + uri + " -> " + inner2.Message);
                    }
                }
                catch (System.Exception exTop)
                {
                    System.Diagnostics.Debug.WriteLine("Invalid uri or other error: " + exTop.Message);
                }
            }

            System.Diagnostics.Debug.WriteLine("All attempts to apply cargo background failed.");
        }

        private void CargoList_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                var personsView = new PersonsGeneralView();
                try { personsView.ConfigureMode(PersonsGeneralView.ViewMode.Genel); } catch { }
                try { personsView.CargoOnlyMode = true; } catch { }
                try { personsView.CargoListCardMode = true; } catch { }

                // Try to use NavigationService if available
                var nav = System.Windows.Navigation.NavigationService.GetNavigationService(this);
                if (nav != null)
                {
                    nav.Navigate(personsView);
                    return;
                }

                // Otherwise open in a new window inside a Frame
                var win = new System.Windows.Window
                {
                    Title = "Kargo Listesi - Filtreler",
                    Content = new System.Windows.Controls.Frame { Content = personsView, NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden },
                    Width = 1100,
                    Height = 700,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                    Owner = System.Windows.Application.Current?.MainWindow
                };
                win.Show();
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message);
            }
        }

        private void CargoDocs_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                var personsView = new PersonsGeneralView();
                try { personsView.CargoOnlyMode = true; } catch { }
                try { personsView.IsCargoDocsMode = true; } catch { }
                try { personsView.ConfigureMode(PersonsGeneralView.ViewMode.Genel); } catch { }

                var nav = System.Windows.Navigation.NavigationService.GetNavigationService(this);
                if (nav != null)
                {
                    nav.Navigate(personsView);
                    return;
                }

                var win = new System.Windows.Window
                {
                    Title = "Kargo Evrak Hazırlama - Filtreler",
                    Content = new System.Windows.Controls.Frame { Content = personsView, NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden },
                    Width = 1100,
                    Height = 700,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                    Owner = System.Windows.Application.Current?.MainWindow
                };
                win.Show();
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message);
            }
        }

        private void CargoTracking_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("=== CargoTracking_Click başladı ===");
            try
            {
                System.Diagnostics.Debug.WriteLine("1. Application.Current.MainWindow aranıyor...");

                // Önce Application.Current.MainWindow deniyoruz
                MainWindow main = System.Windows.Application.Current?.MainWindow as MainWindow;

                if (main == null)
                {
                    System.Diagnostics.Debug.WriteLine("MainWindow.Current.MainWindow null, Application.Current.Windows aranıyor...");

                    // Eğer MainWindow null ise, tüm windows'ları tara
                    foreach (System.Windows.Window window in System.Windows.Application.Current.Windows)
                    {
                        System.Diagnostics.Debug.WriteLine($"Window bulundu: {window.GetType().Name}");
                        if (window is MainWindow mw)
                        {
                            main = mw;
                            System.Diagnostics.Debug.WriteLine("MainWindow bulundu (Windows koleksiyondan)!");
                            break;
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("2. MainWindow bulundu (Application.Current.MainWindow)!");
                }

                if (main != null)
                {
                    System.Diagnostics.Debug.WriteLine("3. CargoTrackingInfoView oluşturuluyor...");
                    var view = new CargoTrackingInfoView();
                    System.Diagnostics.Debug.WriteLine("4. NavigateTo çağrılıyor...");
                    main.NavigateTo(view);
                    System.Diagnostics.Debug.WriteLine("5. NavigateTo çağrısı tamamlandı!");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("HATA: MainWindow hiçbir yerde bulunamadı!");
                    System.Windows.MessageBox.Show("MainWindow bulunamadı! Lütfen uygulamayı yeniden başlatınız.", "Hata", 
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EXCEPTION: {ex.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"Message: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
                System.Windows.MessageBox.Show($"HATA:\n{ex.GetType().Name}\n\n{ex.Message}", "Kargo Takip Hatası", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            System.Diagnostics.Debug.WriteLine("=== CargoTracking_Click bitti ===");
        }
    }
}

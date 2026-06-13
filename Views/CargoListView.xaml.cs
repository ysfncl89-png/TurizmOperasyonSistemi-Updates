using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Windows;
using System.Windows.Controls;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Views
{
    public partial class CargoListView : UserControl
    {
        private List<RowModel> rows = new List<RowModel>();

        public CargoListView(string tourType = null, string period = null, string groupType = null, string duration = null)
        {
            InitializeComponent();
            LoadData(tourType, period);
        }

        private void LoadData(string tourType, string period)
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var query = db.Registrations.Include(r => r.Person).AsQueryable();

                    // filtre: sadece CargoRequest alanı tam olarak "Kargo Gönderilecek" (büyük/küçük harf duyarsız) olanlar
                    query = query.Where(r => r.CargoRequest != null && r.CargoRequest.Trim().ToLower() == "kargo gönderilecek");

                    if (!string.IsNullOrEmpty(tourType)) query = query.Where(r => r.TourType == tourType);
                    if (!string.IsNullOrEmpty(period)) query = query.Where(r => r.Period == period);

                    var list = query.ToList();

                    // Debug: toplam eşleşen kayıt sayısı ve ilk 10 kaydın detayları
                    System.Diagnostics.Debug.WriteLine($"Kargo sorgusu: toplam eşleşen kayıt sayısı = {list.Count}");
                    for (int di = 0; di < list.Count && di < 10; di++)
                    {
                        var rr = list[di];
                        System.Diagnostics.Debug.WriteLine($"Örnek[{di}]: Id={rr.Id}, PersonId={rr.PersonId}, CargoRequest='{rr.CargoRequest}', TourType='{rr.TourType}', Period='{rr.Period}'");
                    }

                    rows.Clear();
                    int i = 1;
                    foreach (var reg in list)
                    {
                        System.Diagnostics.Debug.WriteLine($"Cargo match: Id={reg.Id} PersonId={reg.PersonId} CargoRequest='{reg.CargoRequest}'");
                        var p = reg.Person;
                        rows.Add(new RowModel
                        {
                            RowNumber = i++,
                            FirstName = p?.FirstName ?? string.Empty,
                            LastName = p?.LastName ?? string.Empty,
                            TcKimlikNo = p?.TcKimlikNo ?? string.Empty,
                            Address = p?.Address ?? string.Empty
                        });
                    }

                    CargoDataGrid.ItemsSource = null;
                    CargoDataGrid.ItemsSource = rows;
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Kargo listesi yüklenirken hata: " + ex.Message);
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var nav = System.Windows.Navigation.NavigationService.GetNavigationService(this);
                if (nav != null && nav.CanGoBack)
                {
                    nav.GoBack();
                    return;
                }

                var win = Window.GetWindow(this);
                if (win != null) win.Close();
            }
            catch { }
        }
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string q = (SearchBox.Text ?? string.Empty).Trim().ToLower();
            if (string.IsNullOrEmpty(q))
            {
                CargoDataGrid.ItemsSource = rows;
            }
            else
            {
                var f = rows.Where(r => (r.FirstName ?? string.Empty).ToLower().Contains(q) || (r.LastName ?? string.Empty).ToLower().Contains(q) || (r.TcKimlikNo ?? string.Empty).ToLower().Contains(q) || (r.Address ?? string.Empty).ToLower().Contains(q)).ToList();
                CargoDataGrid.ItemsSource = f;
            }
        }

        private class RowModel
        {
            public int RowNumber { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }
            public string TcKimlikNo { get; set; }
            public string Address { get; set; }
        }
    }
}

using System;
using System.Linq;
using System.Windows;
using TurizmOperasyonSistemi.Data;

namespace TurizmOperasyonSistemi.Views
{
    public partial class CurrencySelectWindow : Window
    {
        public string SelectedCurrency { get; private set; }

        public CurrencySelectWindow()
        {
            InitializeComponent();
            LoadCurrencies();
        }

        private void LoadCurrencies()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var list = db.AppSettings.Where(x => x.Type == "Para Birimi").Select(x => x.Value).ToList();
                    CbCurrency.ItemsSource = list;
                    if (list.Any()) CbCurrency.SelectedIndex = 0;
                }
            }
            catch { }
        }

        private void OkBtn_Click(object sender, RoutedEventArgs e)
        {
            SelectedCurrency = CbCurrency.SelectedItem as string;
            this.DialogResult = true;
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}

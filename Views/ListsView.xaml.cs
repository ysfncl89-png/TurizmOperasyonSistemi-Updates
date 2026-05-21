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

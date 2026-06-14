using System.Windows;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Views
{
    public partial class CargoInspectWindow : Window
    {
        private PersonsGeneralView.GeneralRow _row;

        public bool Confirmed { get; private set; }

        public CargoInspectWindow(PersonsGeneralView.GeneralRow row)
        {
            InitializeComponent();
            try
            {
                this.Height = 520;
                this.Width = 720;
                this.MinHeight = 420;
                this.MinWidth = 520;
                if (this.FindName("TbAddress") is System.Windows.Controls.TextBox tb)
                {
                    tb.Height = 160;
                    tb.VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
                }
                // Tighten spacing and make controls fill available width
                try
                {
                    void StyleTb(string name)
                    {
                        var o = this.FindName(name) as System.Windows.Controls.TextBox;
                        if (o != null)
                        {
                            o.Height = 34;
                            o.Margin = new System.Windows.Thickness(4);
                            o.Padding = new System.Windows.Thickness(6,4,6,4);
                            o.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                        }
                    }

                    StyleTb("TbFirstName");
                    StyleTb("TbLastName");
                    StyleTb("TbTc");
                    StyleTb("TbPhone");
                    var addr = this.FindName("TbAddress") as System.Windows.Controls.TextBox;
                    if (addr != null)
                    {
                        addr.Height = 120;
                        addr.Margin = new System.Windows.Thickness(4);
                        addr.Padding = new System.Windows.Thickness(8);
                        addr.VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
                        addr.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                    }
                }
                catch { }
            }
            catch { }
            _row = row;
            LoadRowToFields();
        }

        private void LoadRowToFields()
        {
            if (_row == null) return;
            TbFirstName.Text = _row.FirstName;
            TbLastName.Text = _row.LastName;
            TbTc.Text = _row.TcKimlikNo;
            TbPhone.Text = _row.PhoneNumber;
            TbAddress.Text = _row.Address;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            this.DialogResult = false;
            this.Close();
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            // persist changes to DB (Person and Registration where applicable)
            try
            {
                using var db = new AppDbContext();
                var person = db.Persons.Find(_row.PersonId);
                if (person != null)
                {
                    person.FirstName = TbFirstName.Text;
                    person.LastName = TbLastName.Text;
                    person.TcKimlikNo = TbTc.Text;
                    person.PhoneNumber = TbPhone.Text;
                    person.Address = TbAddress.Text;
                    db.Entry(person).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
                }

                var reg = db.Registrations.Find(_row.RegistrationId);
                if (reg != null)
                {
                    // ensure CargoRequest preserved
                    reg.CargoRequest = reg.CargoRequest ?? string.Empty;
                    db.Entry(reg).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
                }

                db.SaveChanges();

                // update local row for immediate UI feedback
                _row.FirstName = TbFirstName.Text;
                _row.LastName = TbLastName.Text;
                _row.TcKimlikNo = TbTc.Text;
                _row.PhoneNumber = TbPhone.Text;
                _row.Address = TbAddress.Text;

                // mark as inspected so UI can react
                _row.IsCargoInspected = true;

                Confirmed = true;
                this.DialogResult = true;
                this.Close();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

using System.Windows;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Views
{
    public partial class CargoInspectWindow2 : Window
    {
        private PersonsGeneralView.GeneralRow _row;

        public bool Confirmed { get; private set; }

        public CargoInspectWindow2(PersonsGeneralView.GeneralRow row)
        {
            InitializeComponent();
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
            try
            {
                using var db = new AppDbContext();
                var person = db.Persons.Find(_row.PersonId);
                if (person != null)
                {
                    person.FirstName = TbFirstName.Text;
                    person.LastName = TbLastName.Text;
                    var tcInput = TbTc.Text ?? string.Empty;
                    var digitsOnly = new string(tcInput.Where(char.IsDigit).ToArray());
                    if (digitsOnly.Length != 11)
                    {
                        MessageBox.Show("Geçersiz TC Kimlik numarası. TC Kimlik 11 rakamdan oluşmalıdır.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    person.TcKimlikNo = digitsOnly;
                    person.PhoneNumber = TbPhone.Text;
                    person.Address = TbAddress.Text;
                    db.Entry(person).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
                }

                var reg = db.Registrations.Find(_row.RegistrationId);
                if (reg != null)
                {
                    reg.CargoRequest = reg.CargoRequest ?? string.Empty;
                    db.Entry(reg).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
                }

                db.SaveChanges();

                _row.FirstName = TbFirstName.Text;
                _row.LastName = TbLastName.Text;
                _row.TcKimlikNo = new string((TbTc.Text ?? string.Empty).Where(char.IsDigit).ToArray());
                _row.PhoneNumber = TbPhone.Text;
                _row.Address = TbAddress.Text;

                _row.IsCargoInspected = true;
                try
                {
                    var store = new TurizmOperasyonSistemi.Services.CargoInspectedStore();
                    store.MarkInspected(_row.RegistrationId);
                }
                catch { }

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

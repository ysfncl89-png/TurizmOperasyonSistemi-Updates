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

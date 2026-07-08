using System;
using System.Windows;
using TurizmOperasyonSistemi.Data;

namespace TurizmOperasyonSistemi.Views
{
    public partial class EditAciklamaWindow : Window
    {
        private int _personId;
        public string NewAciklama { get; private set; }

        public EditAciklamaWindow(int personId, string currentAciklama)
        {
            InitializeComponent();
            _personId = personId;
            AciklamaTextBox.Text = currentAciklama;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            NewAciklama = AciklamaTextBox.Text?.Trim();
            try
            {
                using (var db = new AppDbContext())
                {
                    var p = db.Persons.Find(_personId);
                    if (p != null)
                    {
                        p.Aciklama = NewAciklama;
                        db.SaveChanges();
                    }
                }

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
    }
}

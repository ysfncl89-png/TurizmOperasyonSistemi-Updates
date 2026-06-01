using System;
using System.Windows;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Views
{
    public partial class EditPhoneWindow : Window
    {
        private int _personId;
        public string NewPhone { get; private set; }

        public EditPhoneWindow(int personId, string currentPhone)
        {
            InitializeComponent();
            _personId = personId;
            PhoneTextBox.Text = currentPhone;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            NewPhone = PhoneTextBox.Text?.Trim();
            try
            {
                using (var db = new AppDbContext())
                {
                    var p = db.Persons.Find(_personId);
                    if (p != null)
                    {
                        p.PhoneNumber = NewPhone;
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

using System;
using System.Windows;

namespace TurizmOperasyonSistemi.Views
{
    public class SenderInfo
    {
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
    }

    public partial class SenderDialog : Window
    {
        public SenderInfo Result { get; private set; }

        public SenderDialog()
        {
            InitializeComponent();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Result = new SenderInfo
            {
                Name = TbSenderName.Text?.Trim(),
                Phone = TbSenderPhone.Text?.Trim(),
                Address = TbSenderAddress.Text?.Trim()
            };
            this.DialogResult = true;
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}

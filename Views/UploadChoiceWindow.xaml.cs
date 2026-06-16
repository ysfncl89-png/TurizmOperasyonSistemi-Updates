using System.Windows;

namespace TurizmOperasyonSistemi.Views
{
    public partial class UploadChoiceWindow : Window
    {
        public enum UploadChoice { None, File, Qr }
        public UploadChoice Choice { get; private set; } = UploadChoice.None;

        public UploadChoiceWindow()
        {
            InitializeComponent();
        }

        private void BtnFile_Click(object sender, RoutedEventArgs e)
        {
            Choice = UploadChoice.File;
            this.DialogResult = true;
            this.Close();
        }

        private void BtnQr_Click(object sender, RoutedEventArgs e)
        {
            Choice = UploadChoice.Qr;
            this.DialogResult = true;
            this.Close();
        }
    }
}

using System;
using System.Linq;
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

        private Services.SenderTemplateStore _store;

        public SenderDialog()
        {
            InitializeComponent();
            _store = new Services.SenderTemplateStore();
            try { BtnSaveTemplate.Click += BtnSaveTemplate_Click; } catch { }
            try { CbSenderTemplates.SelectionChanged += CbSenderTemplates_SelectionChanged; } catch { }
            LoadTemplates();
        }

        // single constructor defined above

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

        private void LoadTemplates()
        {
            try
            {
                var list = _store.GetAll();
                CbSenderTemplates.ItemsSource = list.Select(x => x.Name).ToList();
            }
            catch { }
        }

        private void CbSenderTemplates_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                var sel = CbSenderTemplates.SelectedItem as string;
                if (string.IsNullOrWhiteSpace(sel)) return;
                var t = _store.GetAll().FirstOrDefault(x => string.Equals(x.Name, sel, StringComparison.OrdinalIgnoreCase));
                if (t != null)
                {
                    TbSenderName.Text = t.Name ?? string.Empty;
                    TbSenderPhone.Text = t.Phone ?? string.Empty;
                    TbSenderAddress.Text = t.Address ?? string.Empty;
                }
            }
            catch { }
        }

        private void BtnSaveTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var name = TbSenderName.Text?.Trim();
                if (string.IsNullOrWhiteSpace(name)) { MessageBox.Show("Şablon için bir ad girin."); return; }
                var tpl = new Services.SenderTemplate { Name = name, Phone = TbSenderPhone.Text?.Trim(), Address = TbSenderAddress.Text?.Trim() };
                _store.Add(tpl);
                LoadTemplates();
                MessageBox.Show("Şablon kaydedildi.");
            }
            catch { }
        }
    }
}

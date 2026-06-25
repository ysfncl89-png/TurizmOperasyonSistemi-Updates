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
            try { BtnDeleteTemplate.Click += BtnDeleteTemplate_Click; } catch { }
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
                CbSenderTemplates.DisplayMemberPath = "Name";
                CbSenderTemplates.SelectedValuePath = "Id";
                CbSenderTemplates.ItemsSource = list;
            }
            catch { }
        }

        private void CbSenderTemplates_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                var t = CbSenderTemplates.SelectedItem as Services.SenderTemplate;
                if (t == null) return;
                TbSenderName.Text = t.Name ?? string.Empty;
                TbSenderPhone.Text = t.Phone ?? string.Empty;
                TbSenderAddress.Text = t.Address ?? string.Empty;
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

                var sel = CbSenderTemplates.SelectedItem as Services.SenderTemplate;
                if (sel != null)
                {
                    // preserve id when updating existing template: remove old and add new with same id
                    tpl.Id = sel.Id;
                    _store.Remove(sel.Id);
                    _store.Add(tpl);
                    MessageBox.Show("Şablon güncellendi.");
                }
                else
                {
                    _store.Add(tpl);
                    MessageBox.Show("Şablon kaydedildi.");
                }

                LoadTemplates();
            }
            catch { }
        }

        private void BtnDeleteTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sel = CbSenderTemplates.SelectedItem as Services.SenderTemplate;
                if (sel == null)
                {
                    MessageBox.Show("Lütfen silmek için bir şablon seçin.");
                    return;
                }

                var res = MessageBox.Show($"\"{sel.Name}\" şablonunu silmek istediğinize emin misiniz?", "Şablonu Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res != MessageBoxResult.Yes) return;

                _store.Remove(sel.Id);
                LoadTemplates();
                // temizle
                TbSenderName.Text = string.Empty;
                TbSenderPhone.Text = string.Empty;
                TbSenderAddress.Text = string.Empty;
                MessageBox.Show("Şablon silindi.");
            }
            catch { }
        }
    }
}

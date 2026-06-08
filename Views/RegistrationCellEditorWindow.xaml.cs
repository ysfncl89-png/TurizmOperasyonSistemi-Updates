using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;
using System.Collections.Generic;

namespace TurizmOperasyonSistemi.Views
{
    public partial class RegistrationCellEditorWindow : Window
    {
        public enum EditorMode { Both, VisaOnly, TicketOnly }
        private EditorMode _mode = EditorMode.Both;
        private int _registrationId;
        private List<AppSetting> _passportOptions = new();
        private List<string> _visaStatusAll = new();
        private List<string> _ticketStatusAll = new();

        public string SelectedVisaRequest => VisaRequestComboBox.SelectedItem?.ToString();
        public AppSetting SelectedPassportArrived => PassportArrivedComboBox.SelectedItem as AppSetting;
        public string SelectedVisaStatus => VisaStatusComboBox.SelectedItem?.ToString();
        public string SelectedTicketRequest => TicketRequestComboBox.SelectedItem?.ToString();
        public string SelectedTicketStatus => TicketStatusComboBox.SelectedItem?.ToString();

        public RegistrationCellEditorWindow(int registrationId, EditorMode mode = EditorMode.Both)
        {
            InitializeComponent();
            _registrationId = registrationId;
            _mode = mode;
            LoadOptions();
            LoadExistingValues();
            AttachHandlers();

            ApplyModeVisibility();
        }

        private void ApplyModeVisibility()
        {
            if (_mode == EditorMode.VisaOnly)
            {
                VisaPanel.Visibility = Visibility.Visible;
                TicketPanel.Visibility = Visibility.Collapsed;
            }
            else if (_mode == EditorMode.TicketOnly)
            {
                VisaPanel.Visibility = Visibility.Collapsed;
                TicketPanel.Visibility = Visibility.Visible;
            }
            else
            {
                VisaPanel.Visibility = Visibility.Visible;
                TicketPanel.Visibility = Visibility.Visible;
            }
        }

        private void AttachHandlers()
        {
            VisaRequestComboBox.SelectionChanged += (s,e) => EvaluateVisaPassportRules();
            PassportArrivedComboBox.SelectionChanged += (s,e) => EvaluateVisaPassportRules();
            TicketRequestComboBox.SelectionChanged += (s,e) => ApplyTicketFiltering();
        }

        private void LoadOptions()
        {
            using var db = new AppDbContext();

            VisaRequestComboBox.ItemsSource = db.AppSettings
                .Where(x => (x.Type == "Vize Talebi" || x.Type == "VisaType") && x.IsActive)
                .OrderBy(x => x.SortOrder)
                .Select(x => x.Value)
                .ToList();

            TicketRequestComboBox.ItemsSource = db.AppSettings
                .Where(x => (x.Type == "Bilet Talebi" || x.Type == "TicketType") && x.IsActive)
                .OrderBy(x => x.SortOrder)
                .Select(x => x.Value)
                .ToList();

            _visaStatusAll = db.AppSettings
                .Where(x => (x.Type == "Vize Durumu" || x.Type == "VisaStatus") && x.IsActive)
                .OrderBy(x => x.SortOrder)
                .Select(x => x.Value)
                .ToList();

            _ticketStatusAll = db.AppSettings
                .Where(x => (x.Type == "Bilet Durumu" || x.Type == "TicketStatus") && x.IsActive)
                .OrderBy(x => x.SortOrder)
                .Select(x => x.Value)
                .ToList();

            _passportOptions = db.AppSettings
                .Where(x => x.Type == "Pasaportu Geldi mi ?" && x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ToList();

            if (!_passportOptions.Any())
            {
                _passportOptions = new List<AppSetting> {
                    new AppSetting { Id = 1, Value = "Evet" },
                    new AppSetting { Id = 0, Value = "Hayır" }
                };
            }

            VisaStatusComboBox.ItemsSource = _visaStatusAll;
            TicketStatusComboBox.ItemsSource = _ticketStatusAll;
            PassportArrivedComboBox.ItemsSource = _passportOptions;
        }

        private void LoadExistingValues()
        {
            using var db = new AppDbContext();
            var reg = db.Registrations.FirstOrDefault(x => x.Id == _registrationId);
            if (reg == null) return;

            VisaRequestComboBox.SelectedItem = reg.VisaRequest;
            TicketRequestComboBox.SelectedItem = reg.TicketRequest;

            // passport arrived stored as int? or null
            if (reg.PassportArrived.HasValue)
            {
                var id = reg.PassportArrived.Value;
                var match = _passportOptions.FirstOrDefault(x => x.Id == id);
                if (match != null) PassportArrivedComboBox.SelectedItem = match;
                else
                {
                    var textMatch = _passportOptions.FirstOrDefault(x => (x.Value ?? string.Empty).IndexOf(id == 1 ? "evet" : "hay", StringComparison.CurrentCultureIgnoreCase) >= 0);
                    if (textMatch != null) PassportArrivedComboBox.SelectedItem = textMatch;
                }
            }

            VisaStatusComboBox.SelectedItem = reg.VisaStatusChoice;
            TicketStatusComboBox.SelectedItem = reg.TicketStatusChoice;

            EvaluateVisaPassportRules();
            ApplyTicketFiltering();
        }

        private string NormalizeForComparison(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            return input.Trim().ToLowerInvariant()
                .Replace('ı','i').Replace('ğ','g').Replace('ü','u').Replace('ş','s').Replace('ö','o').Replace('ç','c');
        }

        private void EvaluateVisaPassportRules()
        {
            try
            {
                var visaReq = VisaRequestComboBox?.SelectedItem?.ToString() ?? string.Empty;
                var visaReqNorm = NormalizeForComparison(visaReq);

                string passportText = PassportArrivedComboBox?.SelectedItem is AppSetting p ? p.Value ?? p.ToString() : PassportArrivedComboBox?.SelectedItem?.ToString() ?? string.Empty;
                var passportNorm = NormalizeForComparison(passportText);

                var passportList = _passportOptions.ToList();
                var visaList = _visaStatusAll.ToList();

                if (!string.IsNullOrWhiteSpace(visaReqNorm) && (visaReqNorm.Contains("kendisi") || visaReqNorm.Contains("kendi")))
                {
                    passportList = passportList.Where(x => NormalizeForComparison(x.Value ?? string.Empty).Contains("verme") || NormalizeForComparison(x.Value ?? string.Empty).Contains("vermeyecek")).ToList();
                    visaList = visaList.Where(v => NormalizeForComparison(v ?? string.Empty).Contains("kendisi") || NormalizeForComparison(v ?? string.Empty).Contains("kendi")).ToList();
                }
                else if (!string.IsNullOrWhiteSpace(visaReqNorm) && visaReqNorm.Contains("alac") && visaReqNorm.Contains("vize"))
                {
                    passportList = _passportOptions.Where(x =>
                        NormalizeForComparison(x.Value ?? string.Empty).Contains("evet") ||
                        NormalizeForComparison(x.Value ?? string.Empty).Contains("hay") ||
                        NormalizeForComparison(x.Value ?? string.Empty).Contains("hayir")).ToList();

                    if (!string.IsNullOrWhiteSpace(passportNorm) && passportNorm.Contains("evet"))
                    {
                        visaList = _visaStatusAll.Where(v =>
                            NormalizeForComparison(v ?? string.Empty).Contains("alin") ||
                            NormalizeForComparison(v ?? string.Empty).Contains("cikti") ||
                            NormalizeForComparison(v ?? string.Empty).Contains("cikm")
                        ).ToList();
                    }
                    else if (!string.IsNullOrWhiteSpace(passportNorm) && passportNorm.Contains("hay"))
                    {
                        visaList = _visaStatusAll.Where(v => NormalizeForComparison(v ?? string.Empty).Contains("pasaport beklen")).ToList();
                    }
                }

                PassportArrivedComboBox.ItemsSource = passportList;
                VisaStatusComboBox.ItemsSource = visaList;

                // Try auto-select sensible defaults
                if (!string.IsNullOrWhiteSpace(visaReqNorm) && (visaReqNorm.Contains("kendisi") || visaReqNorm.Contains("kendi")))
                {
                    var autoVisa = visaList.FirstOrDefault(v => NormalizeForComparison(v ?? string.Empty).Contains("kendisi") || NormalizeForComparison(v ?? string.Empty).Contains("kendi"));
                    if (autoVisa != null) VisaStatusComboBox.SelectedItem = autoVisa;

                    var autoPas = passportList.FirstOrDefault(x => NormalizeForComparison(x.Value ?? string.Empty).Contains("verme") || NormalizeForComparison(x.Value ?? string.Empty).Contains("vermeyecek"));
                    if (autoPas != null) PassportArrivedComboBox.SelectedItem = autoPas;
                }
            }
            catch { }
        }

        private void ApplyTicketFiltering()
        {
            try
            {
                var ticketReq = TicketRequestComboBox?.SelectedItem?.ToString() ?? string.Empty;
                var norm = NormalizeForComparison(ticketReq);
                var list = _ticketStatusAll.ToList();

                if (!string.IsNullOrWhiteSpace(norm) && (norm.Contains("kendisi") || norm.Contains("kendi")))
                {
                    list = list.Where(v => NormalizeForComparison(v ?? string.Empty).Contains("kendisi") || NormalizeForComparison(v ?? string.Empty).Contains("kendi")).ToList();
                }
                else if (!string.IsNullOrWhiteSpace(norm) && norm.Contains("alac"))
                {
                    list = list.Where(v =>
                    {
                        var n = NormalizeForComparison(v ?? string.Empty);
                        return n.Contains("geld") || n.Contains("gelm") || n.Contains("alin") || n.Contains("aliniyor") || n.Contains("cikti");
                    }).ToList();
                }

                TicketStatusComboBox.ItemsSource = list;

                if (!string.IsNullOrWhiteSpace(norm) && (norm.Contains("kendisi") || norm.Contains("kendi")))
                {
                    var auto = list.FirstOrDefault(v => NormalizeForComparison(v ?? string.Empty).Contains("kendisi") || NormalizeForComparison(v ?? string.Empty).Contains("kendi"));
                    if (auto != null) TicketStatusComboBox.SelectedItem = auto;
                }
            }
            catch { }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var db = new AppDbContext();
                var reg = db.Registrations.FirstOrDefault(x => x.Id == _registrationId);
                if (reg == null) { MessageBox.Show("Kayıt bulunamadı."); return; }

                if (_mode == EditorMode.Both || _mode == EditorMode.VisaOnly)
                {
                    reg.VisaRequest = VisaRequestComboBox.SelectedItem?.ToString() ?? reg.VisaRequest;
                    reg.VisaStatusChoice = VisaStatusComboBox.SelectedItem?.ToString() ?? reg.VisaStatusChoice;

                    reg.PassportArrived = -1;
                    if (PassportArrivedComboBox.SelectedItem is AppSetting sel)
                    {
                        reg.PassportArrived = sel.Id;
                    }
                    else
                    {
                        var pText = PassportArrivedComboBox.SelectedItem?.ToString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(pText))
                        {
                            if (pText.IndexOf("evet", StringComparison.CurrentCultureIgnoreCase) >= 0) reg.PassportArrived = 1;
                            else if (pText.IndexOf("hay", StringComparison.CurrentCultureIgnoreCase) >= 0) reg.PassportArrived = 0;
                        }
                    }
                }

                if (_mode == EditorMode.Both || _mode == EditorMode.TicketOnly)
                {
                    reg.TicketRequest = TicketRequestComboBox.SelectedItem?.ToString() ?? reg.TicketRequest;
                    reg.TicketStatusChoice = TicketStatusComboBox.SelectedItem?.ToString() ?? reg.TicketStatusChoice;
                }

                db.SaveChanges();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Kaydedilirken hata: " + ex.Message);
            }

            this.DialogResult = true;
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}

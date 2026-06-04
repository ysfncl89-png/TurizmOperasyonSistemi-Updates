using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using System.IO.Compression;
using System.Reflection;
using System.Security;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;
using TurizmOperasyonSistemi.Services;

namespace TurizmOperasyonSistemi.Views
{
    public partial class PersonsGeneralView : Page
    {
        public enum ViewMode
        {
            Genel,
            Vize,
            Pasaport,
            Bilet,
            Aktarma,
            Havayolu,
            Referans
        }

        // Configure DataGrid columns according to selected view mode
        public void ConfigureMode(ViewMode mode)
        {
            _currentMode = mode;
            // apply theme for the newly selected mode (scope: this page only)
            ApplyTheme(mode);
            // update filter title dynamically
            try
            {
                if (FilterTitleTextBlock != null)
                {
                    FilterTitleTextBlock.Text = $"Görüntülemek istediğiniz {GetModeTitle()} Listesi İçin Önce Kriterleri Sırayla Seçin";
                }
                var mb = this.FindName("ModeBadge") as TextBlock;
                // update inline Run in filter title to highlight current mode
                var mit = this.FindName("ModeInlineText") as TextBlock;
                if (mit != null)
                {
                    mit.Text = GetModeTitle();
                    try {
                        var clr = (Color)FindResource("PrimaryStartColor");
                        mit.Foreground = new SolidColorBrush(clr);
                        mit.FontWeight = FontWeights.ExtraBold;
                        mit.FontSize = CurrentFilterLabel?.FontSize ?? 30;
                    } catch { }
                }
                // also update CurrentModeLabel in TopBar
                var cml = this.FindName("CurrentModeLabel") as TextBlock;
                if (cml != null)
                {
                    cml.Text = GetModeTitle();
                    try { var clr = (Color)FindResource("PrimaryStartColor"); cml.Foreground = new SolidColorBrush(clr); } catch { }
                }
            }
            catch { }
            // Define which headers should be visible for each mode (case-insensitive match)
            var modeVisibleHeaders = new Dictionary<ViewMode, HashSet<string>>()
            {
                [ViewMode.Genel] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Sıra","Ad","Soyad","TC Kimlik","Cinsiyet","Doğum Tarihi","Telefon numarası","Adres","Pasaport tipi","Pasaport numarası","Pasaport Başlangıç tarihi","Pasaport bitiş tarihi","Kan Grubu","Aşı durumu","hastalık durumu","Yaş aralığı","Vize Talebi","Bilet Talebi","Kargo Talebi","Oda tipi","Katılım yapılacak şehir","Aktarma var mı ?","Aktarma şehri","İlçe","Toplam Fiyat","Para Birimi","Ödenen","Kalan","Referans" },
                [ViewMode.Vize] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Sıra","Ad","Soyad","TC Kimlik","Pasaport tipi","Pasaport numarası","Pasaport Başlangıç tarihi","Pasaport bitiş tarihi","Vize Talebi","Telefon numarası","Adres" },
                [ViewMode.Pasaport] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Sıra","Ad","Soyad","TC Kimlik","Pasaport tipi","Pasaport numarası","Pasaport Başlangıç tarihi","Pasaport bitiş tarihi","Telefon numarası","Adres" },
                [ViewMode.Bilet] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Sıra","Ad","Soyad","TC Kimlik","Bilet Talebi","Telefon numarası","Adres","Katılım yapılacak şehir" },
                [ViewMode.Aktarma] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Sıra","Ad","Soyad","TC Kimlik","Aktarma var mı ?","Aktarma şehri","Telefon numarası","Adres","Katılım yapılacak şehir" },
                [ViewMode.Havayolu] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Sıra","Ad","Soyad","TC Kimlik","Katılım yapılacak şehir","Bilet Talebi","Telefon numarası","Adres" },
                [ViewMode.Referans] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Sıra","Ad","Soyad","TC Kimlik","Referans","Telefon numarası","Adres" },
            };

            if (!modeVisibleHeaders.TryGetValue(mode, out var visible)) visible = modeVisibleHeaders[ViewMode.Genel];

            // Show VisaRequestPanel only when in Vize mode
            try
            {
                var visaPanel = this.FindName("VisaRequestPanel") as FrameworkElement;
                if (visaPanel != null)
                {
                    visaPanel.Visibility = (mode == ViewMode.Vize) ? Visibility.Visible : Visibility.Collapsed;
                }
                // clear selection when leaving Vize mode
                if (mode != ViewMode.Vize && CbVisaRequest != null) CbVisaRequest.SelectedIndex = 0;

                var ticketPanel = this.FindName("TicketRequestPanel") as FrameworkElement;
                if (ticketPanel != null)
                {
                    ticketPanel.Visibility = (mode == ViewMode.Bilet) ? Visibility.Visible : Visibility.Collapsed;
                }
                if (mode != ViewMode.Bilet && CbTicketRequest != null) CbTicketRequest.SelectedIndex = 0;
            }
            catch { }
            // If user has a saved template for this mode, prefer that (so custom visibility/order persists)
            try
            {
                var store = new Services.ListTemplateStore();
                var tpl = store.LoadTemplate(mode.ToString());
                if (tpl != null && tpl.Count > 0)
                {
                    // apply saved template order and visibility
                    var ordered = tpl.OrderBy(t => t.Order).ToList();
                    int idx = 0;
                    string Norm(string s)
                    {
                        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
                        var t = s.Trim().ToLowerInvariant();
                        var sb = new System.Text.StringBuilder();
                        foreach (var ch in t)
                        {
                            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                        }
                        return sb.ToString();
                    }

                    foreach (var opt in ordered)
                    {
                        var key = Norm(opt.Title);
                        var col = ResultsDataGrid.Columns.FirstOrDefault(c => Norm(c.Header?.ToString() ?? string.Empty) == key);
                        if (col == null)
                        {
                            col = ResultsDataGrid.Columns.FirstOrDefault(c => Norm(c.Header?.ToString() ?? string.Empty).Contains(key) || key.Contains(Norm(c.Header?.ToString() ?? string.Empty)));
                        }
                        if (col == null) continue;
                        col.Visibility = opt.IsVisible ? Visibility.Visible : Visibility.Collapsed;
                        col.DisplayIndex = idx++;
                    }
                    try { ResultsDataGrid.Items.Refresh(); } catch { }
                    return;
                }
            }
            catch { }

            // Update visibility without recreating columns (fallback to default set)
            int displayIndex = 0;
            foreach (var col in ResultsDataGrid.Columns)
            {
                var header = col.Header?.ToString() ?? string.Empty;
                if (visible.Contains(header))
                {
                    col.Visibility = Visibility.Visible;
                    col.DisplayIndex = displayIndex++;
                }
                else
                {
                    col.Visibility = Visibility.Collapsed;
                }
            }
            // Refresh grid to reflect changes
            try { ResultsDataGrid.Items.Refresh(); } catch { }
        }

        private ViewMode _currentMode = ViewMode.Genel;

        private string GetModeTitle()
        {
            switch (_currentMode)
            {
                case ViewMode.Genel: return "Genel";
                case ViewMode.Vize: return "Vize";
                case ViewMode.Pasaport: return "Pasaport";
                case ViewMode.Havayolu: return "Havayolu";
                case ViewMode.Aktarma: return "Aktarma";
                case ViewMode.Bilet: return "Bilet";
                case ViewMode.Referans: return "Referans";
                default: return "Genel";
            }
        }

        private string CurrentModeName => GetModeTitle();
        private List<GeneralRow> _allRows = new List<GeneralRow>();
        private Button _applyButton;
        private DataGridColumn _editingColumn;

        public PersonsGeneralView()
        {
            InitializeComponent();
            // Apply initial theme for the default mode
            ApplyTheme(_currentMode);
            // ensure default column projection is applied after InitializeComponent
            Loaded += (s, e) => ConfigureMode(_currentMode);
            ResultsDataGrid.MouseDoubleClick += ResultsDataGrid_MouseDoubleClick;
            ResultsDataGrid.CellEditEnding += ResultsDataGrid_CellEditEnding;
            ResultsDataGrid.PreparingCellForEdit += ResultsDataGrid_PreparingCellForEdit;
            Loaded += PersonsGeneralView_Loaded;
            LoadFilterOptions();
            // initial state: show filters, hide topbar
            TopBar.Visibility = Visibility.Collapsed;
            FiltersPanel.Visibility = Visibility.Visible;
        }

        // Theme structure local to this page
        private struct PageTheme
        {
            public Color PrimaryStart;
            public Color PrimaryEnd;
            public Color Glow;
        }

        private void ApplyTheme(ViewMode mode)
        {
            // define per-mode themes
            PageTheme theme;
            switch (mode)
            {
                case ViewMode.Genel:
                    // match ListsView TkGenelBaseColor (#8B5CF6)
                    theme.PrimaryStart = (Color)ColorConverter.ConvertFromString("#FF8B5CF6");
                    theme.PrimaryEnd = (Color)ColorConverter.ConvertFromString("#FF8B5CF6");
                    theme.Glow = (Color)ColorConverter.ConvertFromString("#FF8B5CF6");
                    break;
                case ViewMode.Vize:
                    // match ListsView TkVizeBaseColor (#EC4899)
                    theme.PrimaryStart = (Color)ColorConverter.ConvertFromString("#FFEC4899");
                    theme.PrimaryEnd = (Color)ColorConverter.ConvertFromString("#FFEC4899");
                    theme.Glow = (Color)ColorConverter.ConvertFromString("#FFEC4899");
                    break;
                case ViewMode.Pasaport:
                    // match ListsView TkPasaportBaseColor (#06B6D4)
                    theme.PrimaryStart = (Color)ColorConverter.ConvertFromString("#FF06B6D4");
                    theme.PrimaryEnd = (Color)ColorConverter.ConvertFromString("#FF06B6D4");
                    theme.Glow = (Color)ColorConverter.ConvertFromString("#FF06B6D4");
                    break;
                case ViewMode.Bilet:
                    // match ListsView TkBiletBaseColor (#3B82F6)
                    theme.PrimaryStart = (Color)ColorConverter.ConvertFromString("#FF3B82F6");
                    theme.PrimaryEnd = (Color)ColorConverter.ConvertFromString("#FF3B82F6");
                    theme.Glow = (Color)ColorConverter.ConvertFromString("#FF3B82F6");
                    break;
                case ViewMode.Aktarma:
                    // match ListsView TkAktarmaBaseColor (#10B981)
                    theme.PrimaryStart = (Color)ColorConverter.ConvertFromString("#FF10B981");
                    theme.PrimaryEnd = (Color)ColorConverter.ConvertFromString("#FF10B981");
                    theme.Glow = (Color)ColorConverter.ConvertFromString("#FF10B981");
                    break;
                case ViewMode.Havayolu:
                    // match ListsView TkHavayoluBaseColor (#F59E0B)
                    theme.PrimaryStart = (Color)ColorConverter.ConvertFromString("#FFF59E0B");
                    theme.PrimaryEnd = (Color)ColorConverter.ConvertFromString("#FFF59E0B");
                    theme.Glow = (Color)ColorConverter.ConvertFromString("#FFF59E0B");
                    break;
                case ViewMode.Referans:
                    // match ListsView TkReferansBaseColor (#EF4444)
                    theme.PrimaryStart = (Color)ColorConverter.ConvertFromString("#FFEF4444");
                    theme.PrimaryEnd = (Color)ColorConverter.ConvertFromString("#FFEF4444");
                    theme.Glow = (Color)ColorConverter.ConvertFromString("#FFEF4444");
                    break;
                default:
                    theme.PrimaryStart = (Color)ColorConverter.ConvertFromString("#FF7C3AED");
                    theme.PrimaryEnd = (Color)ColorConverter.ConvertFromString("#FF5B21B6");
                    theme.Glow = (Color)ColorConverter.ConvertFromString("#FF8B5CF6");
                    break;
            }

            // apply to page resources (local only)
            try
            {
                this.Resources["PrimaryStartColor"] = theme.PrimaryStart;
                this.Resources["PrimaryEndColor"] = theme.PrimaryEnd;
                this.Resources["GlowColor"] = theme.Glow;

                // update brushes if present
                if (this.Resources.Contains("PrimaryGradient"))
                {
                    var grad = new LinearGradientBrush(theme.PrimaryStart, theme.PrimaryEnd, 45);
                    this.Resources["PrimaryGradient"] = grad;
                }
                this.Resources["PrimaryStartBrush"] = new SolidColorBrush(theme.PrimaryStart);
                this.Resources["PrimaryEndBrush"] = new SolidColorBrush(theme.PrimaryEnd);
                this.Resources["GlowBrush"] = new SolidColorBrush(theme.Glow);
            }
            catch { }
        }

        private string GetExportDefaultFileName(string extWithoutDot)
        {
            // build a friendly default file name from current filters and selections
            var parts = new List<string> { CurrentModeName + " Liste" };
            try
            {
                var tour = CbTourType?.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(tour) && tour != "Seçiniz") parts.Add(tour);
                var period = CbPeriod?.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(period) && period != "Seçiniz") parts.Add(period);
                var days = CbGroupDays?.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(days) && days != "Seçiniz") parts.Add(days + " Günlük");
                var group = CbGroupType?.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(group) && group != "Seçiniz") parts.Add(group);
                // include smart search summary if present
                try
                {
                    var searchSummary = ComposeSearchSummary(SearchTextBox?.Text ?? string.Empty);
                    if (!string.IsNullOrWhiteSpace(searchSummary)) parts.Add(searchSummary);
                }
                catch { }
            }
            catch { }
            // sanitize and join with ' - '
            var name = string.Join(" - ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
            // keep filename safe
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
            return name + "." + extWithoutDot;
        }

        private string ComposeSearchSummary(string rawSearch)
        {
            if (string.IsNullOrWhiteSpace(rawSearch)) return string.Empty;
            var segments = rawSearch.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            var segPhrases = new List<string>();
            foreach (var seg in segments)
            {
                var toksOrig = seg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                var toksNorm = toksOrig.Select(t => NormalizeForSearch(t)).ToList();
                var used = new bool[toksNorm.Count];
                var phrases = new List<string>();

                Func<int, (int len, string candidate)> TryMatchFieldAtSeg = (i) =>
                {
                    var maxLen = Math.Min(3, toksNorm.Count - i);
                    for (int len = maxLen; len >= 1; len--)
                    {
                        var candidate = string.Join(" ", toksNorm.Skip(i).Take(len));
                        var sel = GetSelectorForHeader(candidate);
                        if (sel != null) return (len, candidate);
                    }
                    return (0, null);
                };

                for (int i = 0; i < toksNorm.Count; i++)
                {
                    if (used[i]) continue;
                    var match = TryMatchFieldAtSeg(i);
                    if (match.len > 0)
                    {
                        for (int t = i; t < i + match.len; t++) if (t < used.Length) used[t] = true;
                        // gather up to 3 value tokens to the right
                        var j = i + match.len;
                        var valueParts = new List<string>();
                        while (j < toksOrig.Count && TryMatchFieldAtSeg(j).len == 0 && valueParts.Count < 3)
                        {
                            valueParts.Add(toksOrig[j]);
                            used[j] = true;
                            j++;
                        }
                        if (valueParts.Count == 0)
                        {
                            var k = i - 1;
                            var left = new List<string>();
                            while (k >= 0 && TryMatchFieldAtSeg(k).len == 0 && !used[k])
                            {
                                left.Insert(0, toksOrig[k]);
                                used[k] = true;
                                k--;
                            }
                            valueParts.AddRange(left);
                        }
                        var valOrig = string.Join(" ", valueParts).Trim();
                        var candidate = match.candidate;
                        if (!string.IsNullOrEmpty(valOrig))
                        {
                            // human friendly phrase mapping
                            if (candidate.Contains("aktarma"))
                            {
                                var vn = NormalizeForSearch(valOrig);
                                if (vn.Contains("var")) phrases.Add("Aktarmalı");
                                else if (vn.Contains("yok")) phrases.Add("Aktarmasız");
                                else phrases.Add($"Aktarma {valOrig}");
                            }
                            else if (candidate.Contains("pasaport"))
                            {
                                phrases.Add($"{valOrig} pasaport");
                            }
                            else if (candidate.Contains("kan"))
                            {
                                phrases.Add($"Kan grubu {valOrig}");
                            }
                            else if (candidate.Contains("kargo"))
                            {
                                phrases.Add($"Kargo {valOrig}");
                            }
                            else
                            {
                                // fallback: value + candidate label
                                phrases.Add($"{valOrig} {candidate}");
                            }
                        }
                    }
                }

                // any unused tokens become generic phrase
                var remaining = new List<string>();
                for (int i = 0; i < toksOrig.Count; i++) if (!used[i]) remaining.Add(toksOrig[i]);
                if (remaining.Any()) phrases.Add(string.Join(" ", remaining));

                if (phrases.Any()) segPhrases.Add(string.Join(" ", phrases));
            }

            return string.Join(" - ", segPhrases);
        }

        // small helper extension-like local function emulation
        // checks nullability in place of returning nullable candidate


        private void PersonsGeneralView_Loaded(object sender, RoutedEventArgs e)
        {
            try { SearchTextBox.Focus(); } catch { }

            // If XAML missing the ApplyFiltersButton (was edited incorrectly), create it dynamically
            try
            {
                // If there is already a named button in XAML, skip dynamic creation
                var named = this.FindName("ApplyFiltersButton") as Button;
                if (named != null)
                {
                    _applyButton = named;
                }
                else
                {
                    if (FilterWarning != null)
                    {
                        if (FilterWarning.Parent is Panel parent)
                        {
                            _applyButton = new Button
                            {
                                Content = "Listeyi Göster",
                                Width = 160,
                                Padding = new Thickness(10, 8, 10, 8),
                                IsEnabled = false
                            };
                            // apply style if available
                            try { _applyButton.Style = (Style)FindResource("PrimaryButtonStyle"); } catch { }
                            _applyButton.Click += ApplyFiltersButton_Click;
                            parent.Children.Add(_applyButton);
                        }
                    }
                }
            }
            catch { }
            // ensure initial filter UI state reflects current selections
            try { Filter_Changed(null, null); } catch { }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // If we're currently showing results, treat Back as "go back to filters" instead of navigating the app back stack
                if (ResultsBorder != null && ResultsBorder.Visibility == Visibility.Visible)
                {
                    // hide results, show filters and hide top search bar
                    ResultsBorder.Visibility = Visibility.Collapsed;
                    TopBar.Visibility = Visibility.Collapsed;
                    FiltersPanel.Visibility = Visibility.Visible;
                    return;
                }

                // Otherwise perform normal navigation (go back in NavService) or close window
                if (this.NavigationService != null && this.NavigationService.CanGoBack)
                {
                    this.NavigationService.GoBack();
                    return;
                }

                var win = Window.GetWindow(this);
                if (win != null) win.Close();
            }
            catch { }
        }

        private void LoadFilterOptions()
        {
            try
            {
                using var db = new AppDbContext();
                var all = db.AppSettings.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToList();

                var tour = all.Where(x => x.Type == "TourType").Select(x => x.Value).ToList();
                tour.Insert(0, "Seçiniz");
                if (CbTourType != null) CbTourType.ItemsSource = tour;

                var period = all.Where(x => x.Type == "Dönem").Select(x => x.Value).ToList();
                period.Insert(0, "Seçiniz");
                if (CbPeriod != null) CbPeriod.ItemsSource = period;

                var group = all.Where(x => x.Type == "GroupType").Select(x => x.Value).ToList();
                group.Insert(0, "Seçiniz");
                if (CbGroupType != null) CbGroupType.ItemsSource = group;

                var days = all.Where(x => x.Type == "Kaç Günlük Grup").Select(x => x.Value).ToList();
                days.Insert(0, "Seçiniz");
                if (CbGroupDays != null) CbGroupDays.ItemsSource = days;

                var visa = all.Where(x => x.Type == "VizeTalebi" || x.Type == "VisaRequest" || x.Type == "Vize Talebi").Select(x => x.Value).ToList();
                if (visa == null) visa = new System.Collections.Generic.List<string>();
                if (!visa.Any())
                {
                    visa.AddRange(new[] { "Seçiniz", "Var", "Yok" });
                }
                else
                {
                    visa.Insert(0, "Seçiniz");
                }
                if (CbVisaRequest != null) CbVisaRequest.ItemsSource = visa;

                var ticket = all.Where(x => x.Type == "BiletTalebi" || x.Type == "TicketRequest" || x.Type == "Bilet Talebi").Select(x => x.Value).ToList();
                if (ticket == null) ticket = new System.Collections.Generic.List<string>();
                if (!ticket.Any())
                {
                    ticket.AddRange(new[] { "Seçiniz", "Var", "Yok" });
                }
                else
                {
                    ticket.Insert(0, "Seçiniz");
                }
                if (CbTicketRequest != null) CbTicketRequest.ItemsSource = ticket;
            }
            catch { }
        }

        private void Filter_Changed(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var tourReady = CbTourType?.SelectedItem as string;
                var periodReady = CbPeriod?.SelectedItem as string;
                var ready = !string.IsNullOrWhiteSpace(tourReady) && tourReady != "Seçiniz" &&
                            !string.IsNullOrWhiteSpace(periodReady) && periodReady != "Seçiniz";

                if (_applyButton != null)
                    _applyButton.IsEnabled = ready;

                if (FilterWarning != null)
                    FilterWarning.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;

                // If we're already showing results and user changes Visa selection while in Vize mode,
                // re-run the results query so the DataGrid is filtered immediately.
                try
                {
                    if (( _currentMode == ViewMode.Vize || _currentMode == ViewMode.Bilet) && ResultsBorder != null && ResultsBorder.Visibility == Visibility.Visible)
                    {
                        // Only reload when a selection change happened (guard null sender cases)
                        LoadResults();
                    }
                }
                catch { }
            }
            catch { }
        }

        private void ApplyFiltersButton_Click(object sender, RoutedEventArgs e)
        {
            // load results and swap UI: hide filters, show top search bar
            LoadResults();
            FiltersPanel.Visibility = Visibility.Collapsed;
            TopBar.Visibility = Visibility.Visible;
            ResultsBorder.Visibility = Visibility.Visible;
            try
            {
                // show a concise summary of selected filters next to the design button
                var parts = new List<string>();
                var tour = CbTourType?.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(tour) && tour != "Seçiniz") parts.Add(tour);
                var period = CbPeriod?.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(period) && period != "Seçiniz") parts.Add(period);
                var group = CbGroupType?.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(group) && group != "Seçiniz") parts.Add(group);
                var days = CbGroupDays?.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(days) && days != "Seçiniz") parts.Add(days);
                var summary = string.Join(" ", parts);
                try { CurrentFilterLabel.Text = string.IsNullOrWhiteSpace(summary) ? string.Empty : summary; } catch { }
            }
            catch { }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            // Prefer showing the custom popup if available
            try
            {
                var popup = this.FindName("ExportPopup") as System.Windows.Controls.Primitives.Popup;
                if (popup != null)
                {
                    popup.IsOpen = true;
                    return;
                }
            }
            catch { }

            // fallback to old context menu
            if (ExportButton?.ContextMenu != null)
            {
                ExportButton.ContextMenu.PlacementTarget = ExportButton;
                ExportButton.ContextMenu.IsOpen = true;
            }
        }

        private void ExportWord_Click(object sender, RoutedEventArgs e)
        {
            var defaultName = GetExportDefaultFileName("docx");
            var path = AskForPath("Word belgesi", "Word dosyası (*.docx)|*.docx", defaultName);
            if (path == null) return;
            ExportWord(NormalizeExtension(path, ".docx"));
        }

        private void ExportExcel_Click(object sender, RoutedEventArgs e)
        {
            var defaultName = GetExportDefaultFileName("xlsx");
            var path = AskForPath("Excel çalışma kitabı", "Excel dosyası (*.xlsx)|*.xlsx", defaultName);
            if (path == null) return;
            ExportExcel(NormalizeExtension(path, ".xlsx"));
        }

        private void ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            var defaultName = GetExportDefaultFileName("pdf");
            var path = AskForPath("PDF belgesi", "PDF dosyası (*.pdf)|*.pdf", defaultName);
            if (path == null) return;
            ExportPdf(NormalizeExtension(path, ".pdf"));
        }

        private string AskForPath(string title, string filter, string defaultFileName)
        {
            var dialog = new SaveFileDialog
            {
                Title = title,
                Filter = filter,
                FileName = defaultFileName,
                AddExtension = true,
                OverwritePrompt = true
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private string NormalizeExtension(string filePath, string extension)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return filePath;
            return Path.ChangeExtension(filePath, extension);
        }

        private void ExportWord(string filePath)
        {
            var cols = GetExportHeaders();
            var rows = GetExportRows(cols);
            Services.ExportHelper.ExportWord(filePath, rows, cols, CurrentModeName + " Liste");
        }

        private void ExportExcel(string filePath)
        {
            var cols = GetExportHeaders();
            var rows = GetExportRows(cols);
            Services.ExportHelper.ExportExcel(filePath, rows, cols, CurrentModeName + " Liste");
        }

        private void ExportPdf(string filePath)
        {
            var cols = GetExportHeaders();
            var rows = GetExportRows(cols);
            Services.ExportHelper.ExportPdf(filePath, rows, cols, CurrentModeName + " Liste");
        }

        private void DeleteIfExists(string filePath)
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }

        private List<string> GetExportHeaders()
        {
            // Respect visual order (DisplayIndex) when building export headers
            var headers = ResultsDataGrid.Columns
                .Where(c => c.Visibility == Visibility.Visible)
                .OrderBy(c => c.DisplayIndex)
                .Select(c => c.Header?.ToString() ?? string.Empty)
                .ToList();
            if (!headers.Any())
            {
                // fallback to all known headers from MapHeaderToProperty
                headers = new List<string> { "Ad", "Soyad", "TC Kimlik", "Telefon numarası", "Adres", "Kan Grubu", "Hastalık durumu" };
            }
            // For Havayolu mode, export headers should be uppercase and Turkish characters removed
            try
            {
                if (_currentMode == ViewMode.Havayolu)
                {
                    return headers.Select(h => TransformForHavayolu(h)).ToList();
                }
            }
            catch { }
            return headers;
        }

        private List<Dictionary<string, string>> GetExportRows(List<string> headers)
        {
            var source = ResultsDataGrid.ItemsSource?.Cast<object>().ToList() ?? new List<object>();
            // Map header names from the visual columns (raw) to properties so we don't rely on possibly-transformed header text
            var visibleCols = ResultsDataGrid.Columns
                .Where(c => c.Visibility == Visibility.Visible)
                .OrderBy(c => c.DisplayIndex)
                .ToList();
            var rawHeaders = visibleCols.Select(c => c.Header?.ToString() ?? string.Empty).ToList();

            var rows = new List<Dictionary<string, string>>();
            for (int i = 0; i < source.Count; i++)
            {
                var item = source[i];
                var dict = new Dictionary<string, string>();
                for (int colIndex = 0; colIndex < rawHeaders.Count; colIndex++)
                {
                    var raw = rawHeaders[colIndex];
                    var outHeader = (colIndex < headers.Count) ? headers[colIndex] : TransformForHavayolu(raw);
                    var val = GetPropertyStringValue(item, MapHeaderToProperty(raw)) ?? string.Empty;
                    if (_currentMode == ViewMode.Havayolu) val = TransformForHavayolu(val);
                    dict[outHeader] = val;
                }
                rows.Add(dict);
            }
            return rows;
        }

        private string TransformForHavayolu(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            // remove/replace Turkish-specific letters then uppercase invariant
            var t = value.Replace("İ", "I").Replace("ı", "i").Replace("Ş", "S").Replace("ş", "s").Replace("Ğ", "G").Replace("ğ", "g").Replace("Ü", "U").Replace("ü", "u").Replace("Ö", "O").Replace("ö", "o").Replace("Ç", "C").Replace("ç", "c");
            try { return t.ToUpperInvariant(); } catch { return t.ToUpper(); }
        }

        // --- Below: utility methods for building docx/xlsx/pdf adapted from PersonsHealthView ---
        private void AddZipEntry(ZipArchive archive, string entryName, string content)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
            writer.Write(content);
        }

        private void AddDocxContentTypes(ZipArchive archive)
        {
            var content = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\n  <Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>\n  <Default Extension=\"xml\" ContentType=\"application/xml\"/>\n  <Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>\n</Types>";
            AddZipEntry(archive, "[Content_Types].xml", content);
        }

        private void AddDocxRels(ZipArchive archive)
        {
            var rels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n  <Relationship Id=\"R1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>\n</Relationships>";
            AddZipEntry(archive, "_rels/.rels", rels);
        }

        private void AddDocxDocumentRels(ZipArchive archive)
        {
            AddZipEntry(archive, "word/_rels/document.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"/>");
        }

        private void AddDocxDocumentXml(ZipArchive archive, List<Dictionary<string, string>> rows, List<string> headers)
        {
            var tableRows = new List<string> { BuildWordTableRow(headers, true) };
            tableRows.AddRange(rows.Select(r => BuildWordTableRow(headers.Select(h => r.ContainsKey(h) ? r[h] : string.Empty).ToList(), false)));
            var document = $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">\n  <w:body>\n    <w:p><w:r><w:t>Genel Liste</w:t></w:r></w:p>\n    <w:tbl>\n      {string.Join(Environment.NewLine, tableRows)}\n    </w:tbl>\n  </w:body>\n</w:document>";
            AddZipEntry(archive, "word/document.xml", document);
        }

        private string BuildWordTableRow(IReadOnlyList<string> values, bool header)
        {
            var cells = values.Select(value => $"<w:tc><w:tcPr>{(header ? "<w:shd w:fill=\"22324A\"/>" : string.Empty)}</w:tcPr><w:p><w:r><w:t>{EscapeXml(value ?? string.Empty)}</w:t></w:r></w:p></w:tc>");
            return "<w:tr>" + string.Join(string.Empty, cells) + "</w:tr>";
        }

        private void AddXlsxContentTypes(ZipArchive archive)
        {
            var content = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\n  <Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>\n  <Default Extension=\"xml\" ContentType=\"application/xml\"/>\n  <Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>\n  <Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>\n  <Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>\n  <Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>\n  <Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>\n</Types>";
            AddZipEntry(archive, "[Content_Types].xml", content);
        }

        private void AddXlsxRels(ZipArchive archive)
        {
            var rels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>\n</Relationships>";
            AddZipEntry(archive, "_rels/.rels", rels);
            AddZipEntry(archive, "docProps/core.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:dcmitype=\"http://purl.org/dc/dcmitype/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">  <dc:title>Genel Liste</dc:title></cp:coreProperties>");
            AddZipEntry(archive, "docProps/app.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\" xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\"><Application>TurizmOperasyonSistemi</Application><TitlesOfParts><vt:vector size=\"1\" baseType=\"lpstr\"><vt:lpstr>Genel Liste</vt:lpstr></vt:vector></TitlesOfParts></Properties>");
        }

        private void AddXlsxWorkbook(ZipArchive archive)
        {
            var workbook = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">\n  <bookViews>\n    <workbookView activeTab=\"0\"/>\n  </bookViews>\n  <sheets>\n    <sheet name=\"Genel Liste\" sheetId=\"1\" r:id=\"rId1\"/>\n  </sheets>\n</workbook>";
            AddZipEntry(archive, "xl/workbook.xml", workbook);
        }

        private void AddXlsxWorkbookRels(ZipArchive archive)
        {
            var rels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>\n  <Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>\n</Relationships>";
            AddZipEntry(archive, "xl/_rels/workbook.xml.rels", rels);
        }

        private void AddXlsxStyles(ZipArchive archive)
        {
            var styles = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts></styleSheet>";
            AddZipEntry(archive, "xl/styles.xml", styles);
        }

        private void AddXlsxSheet(ZipArchive archive, List<Dictionary<string, string>> rows, List<string> headers)
        {
            var rowXml = new List<string>();
            rowXml.Add(BuildXlsxRow(1, headers, true));
            for (var i = 0; i < rows.Count; i++)
            {
                rowXml.Add(BuildXlsxRow(i + 2, headers.Select(h => rows[i].ContainsKey(h) ? rows[i][h] : string.Empty).ToList(), false));
            }
            var lastColumn = ColumnName(headers.Count);
            var lastRow = Math.Max(1, rows.Count + 1);
            var sheet = $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">\n  <dimension ref=\"A1:{lastColumn}{lastRow}\"/>\n  <sheetData>\n    {string.Join(Environment.NewLine, rowXml)}\n  </sheetData>\n</worksheet>";
            AddZipEntry(archive, "xl/worksheets/sheet1.xml", sheet);
        }

        private string BuildXlsxRow(int rowNumber, IReadOnlyList<string> values, bool header)
        {
            var cells = new List<string>();
            for (var i = 0; i < values.Count; i++)
            {
                var cellRef = $"{ColumnName(i + 1)}{rowNumber}";
                cells.Add($"<c r=\"{cellRef}\" t=\"inlineStr\"><is><t>{EscapeXml(values[i] ?? string.Empty)}</t></is></c>");
            }
            return $"<row r=\"{rowNumber}\">{string.Join(string.Empty, cells)}</row>";
        }

        private byte[] BuildSimplePdf(List<Dictionary<string, string>> rows, List<string> headers)
        {
            var pageWidth = 842; var pageHeight = 595; var margin = 28; var rowHeight = 22; var usableWidth = pageWidth - margin * 2; var columnWidth = usableWidth / Math.Max(1, headers.Count);
            var content = new List<string> { $"BT /F1 16 Tf 40 560 Td ({EscapePdfText("Genel Liste")}) Tj ET" };
            var y = 520; content.AddRange(BuildPdfRow(headers, margin, y, columnWidth, rowHeight, true)); y -= rowHeight;
            foreach (var row in rows)
            {
                if (y < 50) break;
                content.AddRange(BuildPdfRow(headers.Select(h => row.ContainsKey(h) ? row[h] : string.Empty).ToList(), margin, y, columnWidth, rowHeight, false));
                y -= rowHeight;
            }
            var contentText = string.Join("\n", content);
            return BuildMinimalPdfBytes(pageWidth, pageHeight, contentText);
        }

        private IEnumerable<string> BuildPdfRow(IReadOnlyList<string> values, int x, int y, double columnWidth, int rowHeight, bool header)
        {
            for (var i = 0; i < values.Count; i++)
            {
                var left = x + (int)(i * columnWidth);
                var right = x + (int)((i + 1) * columnWidth);
                var text = EscapePdf(values[i] ?? string.Empty);
                var textX = left + 4; var textY = y + 7;
                if (header)
                {
                    yield return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F3} {1:F3} {2:F3} rg", 34.0/255.0, 50.0/255.0, 74.0/255.0);
                    yield return "0 0 0 RG";
                    yield return $"{left} {y} {right - left} {rowHeight} re B";
                    yield return "1 1 1 rg";
                    yield return $"BT /F2 9 Tf {textX} {textY} Td ({EscapePdfText(values[i] ?? string.Empty)}) Tj ET";
                    yield return "0 0 0 rg";
                }
                else
                {
                    yield return "0 0 0 RG";
                    yield return $"{left} {y} {right - left} {rowHeight} re S";
                    yield return "0 0 0 rg";
                    yield return $"BT /F1 8 Tf {textX} {textY} Td ({EscapePdfText(values[i] ?? string.Empty)}) Tj ET";
                }
            }
        }

        private byte[] BuildMinimalPdfBytes(int pageWidth, int pageHeight, string content)
        {
            var objects = new List<string> { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageWidth} {pageHeight}] /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> /Contents 6 0 R >>", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>" };
            var streamBytes = System.Text.Encoding.ASCII.GetBytes(content);
            var contentObject = $"<< /Length {streamBytes.Length} >>\nstream\n{content}\nendstream";
            objects.Add(contentObject);
            using var ms = new MemoryStream(); using var writer = new StreamWriter(ms, System.Text.Encoding.ASCII, 1024, leaveOpen: true);
            writer.WriteLine("%PDF-1.4");
            var offsets = new List<long>();
            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(ms.Position);
                writer.WriteLine($"{i + 1} 0 obj"); writer.WriteLine(objects[i]); writer.WriteLine("endobj");
            }
            writer.Flush(); var xrefPosition = ms.Position; writer.WriteLine("xref"); writer.WriteLine($"0 {objects.Count + 1}"); writer.WriteLine("0000000000 65535 f ");
            foreach (var offset in offsets) writer.WriteLine($"{offset:0000000000} 00000 n ");
            writer.WriteLine("trailer"); writer.WriteLine($"<< /Size {objects.Count + 1} /Root 1 0 R >>"); writer.WriteLine("startxref"); writer.WriteLine(xrefPosition); writer.WriteLine("%%EOF"); writer.Flush(); return ms.ToArray();
        }

        private string EscapeXml(string value) { return SecurityElement.Escape(value) ?? string.Empty; }
        private string EscapePdf(string value) { return (value ?? string.Empty).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)"); }
        private string EscapePdfText(string value) { return EscapePdf(TransliterateTurkish(value)); }
        private string TransliterateTurkish(string value)
        {
            return (value ?? string.Empty).Replace("İ", "I").Replace("ı", "i").Replace("Ş", "S").Replace("ş", "s").Replace("Ğ", "G").Replace("ğ", "g").Replace("Ü", "U").Replace("ü", "u").Replace("Ö", "O").Replace("ö", "o").Replace("Ç", "C").Replace("ç", "c");
        }

        private string ColumnName(int index)
        {
            var dividend = index; var columnName = string.Empty;
            while (dividend > 0) { var modulo = (dividend - 1) % 26; columnName = Convert.ToChar(65 + modulo) + columnName; dividend = (dividend - modulo) / 26; }
            return columnName;
        }

        private void LoadResults()
        {
            try
            {
                using var db = new AppDbContext();

                var query = db.Registrations.Include(r => r.Person).AsQueryable();

                var tour = CbTourType.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(tour) && tour != "Seçiniz") query = query.Where(r => r.TourType == tour);

                var period = CbPeriod.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(period) && period != "Seçiniz") query = query.Where(r => r.Period == period);

                var group = CbGroupType.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(group) && group != "Seçiniz") query = query.Where(r => r.GroupType == group);

                var days = CbGroupDays.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(days) && days != "Seçiniz") query = query.Where(r => r.DaysCount == days);

                // If in Vize mode and a specific VisaRequest is chosen, filter by it
                try
                {
                    if (_currentMode == ViewMode.Vize && CbVisaRequest != null)
                    {
                        var visaSel = CbVisaRequest.SelectedItem as string;
                        if (!string.IsNullOrWhiteSpace(visaSel) && visaSel != "Seçiniz")
                        {
                            query = query.Where(r => r.VisaRequest == visaSel);
                        }
                    }
                    // If in Bilet mode and a specific TicketRequest is chosen, filter by it
                    if (_currentMode == ViewMode.Bilet && CbTicketRequest != null)
                    {
                        var ticketSel = CbTicketRequest.SelectedItem as string;
                        if (!string.IsNullOrWhiteSpace(ticketSel) && ticketSel != "Seçiniz")
                        {
                            query = query.Where(r => r.TicketRequest == ticketSel);
                        }
                    }
                }
                catch { }

                var list = query.OrderBy(r => r.Id).ToList();

                var rows = new List<GeneralRow>();
                var seq = 1;
                foreach (var reg in list)
                {
                    var p = reg.Person;
                    var r = new GeneralRow
                    {
                        RegistrationId = reg.Id,
                        PersonId = p?.Id ?? 0,
                        Sequence = seq++,
                        FirstName = p?.FirstName ?? string.Empty,
                        LastName = p?.LastName ?? string.Empty,
                        TcKimlikNo = p?.TcKimlikNo ?? string.Empty,
                        Gender = p?.Gender ?? string.Empty,
                        BirthDate = p?.BirthDate.ToString("dd.MM.yyyy") ?? string.Empty,
                        PhoneNumber = p?.PhoneNumber ?? string.Empty,
                        Address = p?.Address ?? string.Empty,
                        PassportType = p?.PassportType ?? string.Empty,
                        PassportNumber = p?.PassportNumber ?? string.Empty,
                        PassportStartDate = p?.PassportStartDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                        PassportEndDate = p?.PassportEndDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                        BloodType = p?.BloodType ?? string.Empty,
                        VaccineStatus = p?.VaccineStatus ?? string.Empty,
                        HealthStatus = p?.HealthStatus ?? string.Empty,
                        AgeRange = p?.AgeRange ?? string.Empty,

                        VisaRequest = reg.VisaRequest ?? string.Empty,
                        TicketRequest = reg.TicketRequest ?? string.Empty,
                        CargoRequest = reg.CargoRequest ?? string.Empty,
                        RoomType = reg.RoomType ?? string.Empty,
                        DepartureCity = reg.DepartureCity ?? string.Empty,
                        HasTransfer = reg.HasTransfer ?? string.Empty,
                        TransferCity = reg.TransferCity ?? string.Empty,
                        District = reg.District ?? string.Empty,
                        TotalPrice = reg.TotalPrice.ToString("0.##"),
                        Currency = reg.Currency ?? string.Empty,
                        PaidAmount = reg.PaidAmount.ToString("0.##"),
                        RemainingDebt = reg.RemainingDebt.ToString("0.##"),
                        Reference = reg.Reference ?? string.Empty
                    };
                    // If current mode is Havayolu, transform all textual fields to uppercase and remove Turkish characters
                    if (_currentMode == ViewMode.Havayolu)
                    {
                        r.FirstName = TransformForHavayolu(r.FirstName);
                        r.LastName = TransformForHavayolu(r.LastName);
                        r.TcKimlikNo = TransformForHavayolu(r.TcKimlikNo);
                        r.Gender = TransformForHavayolu(r.Gender);
                        r.BirthDate = TransformForHavayolu(r.BirthDate);
                        r.PhoneNumber = TransformForHavayolu(r.PhoneNumber);
                        r.Address = TransformForHavayolu(r.Address);
                        r.PassportType = TransformForHavayolu(r.PassportType);
                        r.PassportNumber = TransformForHavayolu(r.PassportNumber);
                        r.PassportStartDate = TransformForHavayolu(r.PassportStartDate);
                        r.PassportEndDate = TransformForHavayolu(r.PassportEndDate);
                        r.BloodType = TransformForHavayolu(r.BloodType);
                        r.VaccineStatus = TransformForHavayolu(r.VaccineStatus);
                        r.HealthStatus = TransformForHavayolu(r.HealthStatus);
                        r.AgeRange = TransformForHavayolu(r.AgeRange);
                        r.VisaRequest = TransformForHavayolu(r.VisaRequest);
                        r.TicketRequest = TransformForHavayolu(r.TicketRequest);
                        r.CargoRequest = TransformForHavayolu(r.CargoRequest);
                        r.RoomType = TransformForHavayolu(r.RoomType);
                        r.DepartureCity = TransformForHavayolu(r.DepartureCity);
                        r.HasTransfer = TransformForHavayolu(r.HasTransfer);
                        r.TransferCity = TransformForHavayolu(r.TransferCity);
                        r.District = TransformForHavayolu(r.District);
                        r.TotalPrice = TransformForHavayolu(r.TotalPrice);
                        r.Currency = TransformForHavayolu(r.Currency);
                        r.PaidAmount = TransformForHavayolu(r.PaidAmount);
                        r.RemainingDebt = TransformForHavayolu(r.RemainingDebt);
                        r.Reference = TransformForHavayolu(r.Reference);
                    }
                    rows.Add(r);
                }

                _allRows = rows;
                ResultsDataGrid.ItemsSource = _allRows;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void ResultsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var dep = (DependencyObject)e.OriginalSource;
                var cell = FindParent<DataGridCell>(dep);
                if (cell == null) return;
                var column = cell.Column;
                var header = (column.Header?.ToString() ?? string.Empty).Trim();
                // allowed editable headers
                var editable = new[] { "telefon numarası", "adres", "pasaport numarası", "aşı durumu", "hastalık durumu", "ilçe", "toplam fiyat", "ödenen", "kalan", "referans",
                                        "pasaport tipi", "kan grubu", "vize talebi", "bilet talebi", "kargo talebi", "katılım yapılacak şehir", "aktarma var mı ?", "aktarma şehri", "para birimi" };
                if (!editable.Contains(header.ToLowerInvariant())) return;

                // make only this column editable and begin edit
                ResultsDataGrid.IsReadOnly = false;
                foreach (var col in ResultsDataGrid.Columns) col.IsReadOnly = true;
                column.IsReadOnly = false;
                _editingColumn = column;

                // select row and begin edit
                if (cell.DataContext != null)
                {
                    ResultsDataGrid.SelectedItem = cell.DataContext;
                    ResultsDataGrid.CurrentCell = new DataGridCellInfo(cell.DataContext, column);
                    ResultsDataGrid.BeginEdit();
                }
            }
            catch { }
        }

        private void ResultsDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            try
            {
                if (_editingColumn == null) return;
                // commit already in progress; save after UI update
                if (e.EditAction == DataGridEditAction.Commit)
                {
                    var rowItem = e.Row.Item as GeneralRow;
                    var header = e.Column.Header?.ToString() ?? string.Empty;
                    string newText = null;
                    try
                    {
                        // If editing element is TextBox (text columns)
                        if (e.EditingElement is TextBox tb) newText = tb.Text;
                        else
                        {
                            // Try to find ComboBox inside editing element or cell content
                            ComboBox editCb = null;
                            if (e.EditingElement is ComboBox directCb) editCb = directCb;
                            else editCb = FindVisualChild<ComboBox>(e.EditingElement);
                            if (editCb == null)
                            {
                                try { editCb = FindVisualChild<ComboBox>(e.Column.GetCellContent(e.Row)); } catch { }
                            }
                            if (editCb != null)
                            {
                                // Prefer SelectedValue or SelectedItem.ToString
                                if (editCb.SelectedValue != null) newText = editCb.SelectedValue.ToString();
                                else if (editCb.SelectedItem != null) newText = editCb.SelectedItem.ToString();
                            }
                        }
                    }
                    catch { }
                    // use dispatcher to ensure binding updated
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { SaveEditedValue(rowItem, header, newText); } catch { }
                        // restore grid to read-only default
                        ResultsDataGrid.CommitEdit();
                        ResultsDataGrid.IsReadOnly = true;
                        foreach (var col in ResultsDataGrid.Columns) col.IsReadOnly = true;
                        _editingColumn = null;
                    }));
                }
                else
                {
                    // canceled - restore
                    ResultsDataGrid.IsReadOnly = true;
                    foreach (var col in ResultsDataGrid.Columns) col.IsReadOnly = true;
                    _editingColumn = null;
                }
            }
            catch { }
        }

        private void ResultsDataGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            try
            {
                var header = e.Column.Header?.ToString() ?? string.Empty;
                // when a combo box cell is prepared, populate its items from AppSettings
                ComboBox cb = null;
                if (e.EditingElement is ComboBox direct) cb = direct;
                else cb = FindVisualChild<ComboBox>(e.EditingElement);
                // fallback: try to get cell content and search there (template hosting may cause EditingElement not to contain combo yet)
                if (cb == null)
                {
                    try
                    {
                        var content = e.Column.GetCellContent(e.Row);
                        if (content != null) cb = FindVisualChild<ComboBox>(content);
                    }
                    catch { }
                }
                if (cb == null) return;
                using var db = new AppDbContext();
                var all = db.AppSettings.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToList();
                List<string> items = null;
                var headerKey = header.Trim().ToLowerInvariant();
                // Robust matching: normalize Type strings (remove punctuation/spaces, lowercase) to avoid mismatches like 'Aktarma Var mı?' vs 'Aktarma Var mı ?'
                string NormalizeForCompare(string s)
                {
                    if (string.IsNullOrWhiteSpace(s)) return string.Empty;
                    var sb = new StringBuilder();
                    foreach (var ch in s.ToLowerInvariant())
                    {
                        if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                        // treat Turkish dotted letters equivalently by ToLowerInvariant above
                    }
                    return sb.ToString();
                }

                // Transliterate Turkish characters before normalizing so checks like "katilim" or "sehir"
                // match headers that contain Turkish letters (ş, ı, ö, ü, etc.).
                string headerNorm = NormalizeForCompare(TransliterateTurkish(headerKey));

                // mapping: header -> expected setting Type values
                List<string> expectedTypes = null;
                switch (headerNorm)
                {
                    case var _ when headerNorm.Contains("pasaport"):
                        expectedTypes = new List<string> { "Pasaport Türü", "Pasaport Tipi", "PassportType" };
                        break;
                    case var _ when headerNorm.Contains("kangrub") || headerNorm.Contains("kangrubu") || headerNorm.Contains("kangrubu"):
                        expectedTypes = new List<string> { "Kan Grubu" };
                        break;
                    case var _ when headerNorm.Contains("vize"):
                        expectedTypes = new List<string> { "Vize Talebi" };
                        break;
                    case var _ when headerNorm.Contains("bilet"):
                        expectedTypes = new List<string> { "Bilet Talebi" };
                        break;
                    case var _ when headerNorm.Contains("kargo"):
                        expectedTypes = new List<string> { "Kargo Talebi" };
                        break;
                    // Match only katılım-related headers for DepartureCity to avoid accidentally
                    // matching any header that contains the substring "sehir" (which would
                    // also match TransferCity). This prevents wrong AppSettings being used.
                    case var _ when headerNorm.Contains("katilimmenu") || headerNorm.Contains("katilim"):
                        expectedTypes = new List<string> { "Katılım Yapılacak Şehir" };
                        break;
                    // Specific match for transfer city must come before generic transfer-flag match
                    // Accept multiple normalized variants (sehir / sehri) to avoid missing Turkish suffix/inflection
                    case var _ when headerNorm.Contains("aktarmaseh") || headerNorm.Contains("aktarmasehir") || headerNorm.Contains("aktarmasehri"):
                        expectedTypes = new List<string> { "Aktarma Şehri" };
                        break;
                    case var _ when headerNorm.Contains("aktarmavar") || headerNorm.Contains("aktarmavarmi"):
                        expectedTypes = new List<string> { "Aktarma Var mı ?", "Aktarma Var mı?", "Aktarma Var mı", "Aktarma" };
                        break;
                    case var _ when headerNorm.Contains("parabirimi") || headerNorm.Contains("parabirim"):
                        expectedTypes = new List<string> { "Para Birimi" };
                        break;
                }

                if (expectedTypes != null)
                {
                    var expectedNorms = expectedTypes.Select(t => NormalizeForCompare(t)).ToList();
                    // Preserve AppSettings SortOrder when producing ComboBox items
                    items = all.Where(x => expectedNorms.Contains(NormalizeForCompare(x.Type)))
                               .OrderBy(x => x.SortOrder)
                               .Select(x => x.Value)
                               .ToList();
                    // Remove placeholder values that might have been stored in AppSettings
                    items = items.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
                    var cleaned = new List<string>();
                    foreach (var v in items)
                    {
                        var t = v.Trim();
                        // normalize using transliteration so Turkish chars map to ASCII for robust comparison
                        var norm = NormalizeForCompare(TransliterateTurkish(t));
                        if (string.Equals(norm, "seciniz", StringComparison.OrdinalIgnoreCase)) continue;
                        if (string.Equals(norm, "secilmedi", StringComparison.OrdinalIgnoreCase)) continue;
                        cleaned.Add(t);
                    }
                    items = cleaned;
                }
                // ensure items list is not null
                if (items == null) items = new List<string>();
                // IMPORTANT: only use values coming from AppSettings for the mapped Types.
                // If no values exist for the expected Type(s), do not populate unrelated options.
                if (items.Count == 0)
                {
                    // leave items empty so the ComboBox will show only the default "Seçiniz" entry below
                    items = new List<string>();
                }

                // normalize items: trim and remove null/empty and duplicates while preserving order
                var normalized = new List<string>();
                foreach (var it in items)
                {
                    if (string.IsNullOrWhiteSpace(it)) continue;
                    var t = it.Trim();
                    if (normalized.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase))) continue;
                    normalized.Add(t);
                }
                if (!normalized.Any()) normalized.Add("Seçiniz");
                if (!normalized.Contains("Seçiniz", StringComparer.OrdinalIgnoreCase)) normalized.Insert(0, "Seçiniz");
                cb.ItemsSource = normalized;
            }
            catch { }
        }

        private static TChild FindVisualChild<TChild>(DependencyObject obj) where TChild : DependencyObject
        {
            if (obj == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                if (child is TChild correctlyTyped) return correctlyTyped;
                var result = FindVisualChild<TChild>(child);
                if (result != null) return result;
            }
            return null;
        }

        private void SaveEditedValue(GeneralRow row, string header, string newText = null)
        {
            if (row == null) return;
            var propName = MapHeaderToProperty(header);
            if (string.IsNullOrWhiteSpace(propName)) return;

            // find property on row
            var prop = row.GetType().GetProperty(propName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (prop == null) return;

            // determine new value: prefer provided newText (from editing control), otherwise current row value
            object newValObj = null;
            if (!string.IsNullOrEmpty(newText))
            {
                newValObj = newText;
                // update row property so UI reflects change
                try { prop.SetValue(row, newText); } catch { }
            }
            else
            {
                newValObj = prop.GetValue(row);
            }

            var newVal = newValObj?.ToString();

            try
            {
                using var db = new AppDbContext();
                // person properties
                var personProps = new[] { "PhoneNumber", "Address", "PassportNumber", "VaccineStatus", "HealthStatus", "PassportType", "BloodType" };
                // registration properties (including new combobox-backed fields)
                var registrationProps = new[] { "District", "TotalPrice", "PaidAmount", "RemainingDebt", "Reference", "VisaRequest", "TicketRequest", "CargoRequest", "DepartureCity", "HasTransfer", "TransferCity", "Currency" };

                if (personProps.Contains(propName, StringComparer.OrdinalIgnoreCase))
                {
                    var p = db.Persons.FirstOrDefault(x => x.Id == row.PersonId);
                    if (p == null) return;
                    // assign string properties
                    if (propName.Equals("PhoneNumber", StringComparison.OrdinalIgnoreCase)) p.PhoneNumber = newVal?.ToString() ?? string.Empty;
                    else if (propName.Equals("Address", StringComparison.OrdinalIgnoreCase)) p.Address = newVal?.ToString() ?? string.Empty;
                    else if (propName.Equals("PassportNumber", StringComparison.OrdinalIgnoreCase)) p.PassportNumber = newVal?.ToString() ?? string.Empty;
                    else if (propName.Equals("VaccineStatus", StringComparison.OrdinalIgnoreCase)) p.VaccineStatus = newVal?.ToString() ?? string.Empty;
                    else if (propName.Equals("HealthStatus", StringComparison.OrdinalIgnoreCase)) p.HealthStatus = newVal?.ToString() ?? string.Empty;
                    else if (propName.Equals("PassportType", StringComparison.OrdinalIgnoreCase)) p.PassportType = newVal?.ToString() ?? string.Empty;
                    else if (propName.Equals("BloodType", StringComparison.OrdinalIgnoreCase)) p.BloodType = newVal?.ToString() ?? string.Empty;
                    db.Entry(p).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
                    db.SaveChanges();
                    // refresh grid so change is visible
                    try { ResultsDataGrid.Items.Refresh(); } catch { }
                }
                else if (registrationProps.Contains(propName, StringComparer.OrdinalIgnoreCase))
                {
                    var reg = db.Registrations.FirstOrDefault(x => x.Id == row.RegistrationId);
                    if (reg == null) return;
                    if (propName.Equals("District", StringComparison.OrdinalIgnoreCase)) { reg.District = newVal?.ToString() ?? string.Empty; }
                    else if (propName.Equals("Reference", StringComparison.OrdinalIgnoreCase)) { reg.Reference = newVal?.ToString() ?? string.Empty; }
                    else if (propName.Equals("VisaRequest", StringComparison.OrdinalIgnoreCase)) { reg.VisaRequest = newVal?.ToString() ?? string.Empty; row.VisaRequest = reg.VisaRequest ?? string.Empty; }
                    else if (propName.Equals("TicketRequest", StringComparison.OrdinalIgnoreCase)) { reg.TicketRequest = newVal?.ToString() ?? string.Empty; row.TicketRequest = reg.TicketRequest ?? string.Empty; }
                    else if (propName.Equals("CargoRequest", StringComparison.OrdinalIgnoreCase)) { reg.CargoRequest = newVal?.ToString() ?? string.Empty; row.CargoRequest = reg.CargoRequest ?? string.Empty; }
                    else if (propName.Equals("DepartureCity", StringComparison.OrdinalIgnoreCase)) { reg.DepartureCity = newVal?.ToString() ?? string.Empty; row.DepartureCity = reg.DepartureCity ?? string.Empty; }
                    else if (propName.Equals("HasTransfer", StringComparison.OrdinalIgnoreCase)) { reg.HasTransfer = newVal?.ToString() ?? string.Empty; row.HasTransfer = reg.HasTransfer ?? string.Empty; }
                    else if (propName.Equals("TransferCity", StringComparison.OrdinalIgnoreCase)) { reg.TransferCity = newVal?.ToString() ?? string.Empty; row.TransferCity = reg.TransferCity ?? string.Empty; }
                    else if (propName.Equals("Currency", StringComparison.OrdinalIgnoreCase)) { reg.Currency = newVal?.ToString() ?? string.Empty; row.Currency = reg.Currency ?? string.Empty; }
                    else if (propName.Equals("TotalPrice", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryParseDecimalFlexible(newVal, out var d))
                        {
                            reg.TotalPrice = d;
                            // recalc remaining debt
                            reg.RemainingDebt = reg.TotalPrice - reg.PaidAmount;
                            row.TotalPrice = reg.TotalPrice.ToString("0.##");
                            row.RemainingDebt = reg.RemainingDebt.ToString("0.##");
                        }
                    }
                    else if (propName.Equals("PaidAmount", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryParseDecimalFlexible(newVal, out var d))
                        {
                            reg.PaidAmount = d;
                            // recalc remaining debt
                            reg.RemainingDebt = reg.TotalPrice - reg.PaidAmount;
                            row.PaidAmount = reg.PaidAmount.ToString("0.##");
                            row.RemainingDebt = reg.RemainingDebt.ToString("0.##");
                        }
                    }
                    else if (propName.Equals("RemainingDebt", StringComparison.OrdinalIgnoreCase))
                    {
                        // If user edits remaining debt, adjust PaidAmount = TotalPrice - RemainingDebt
                        if (TryParseDecimalFlexible(newVal, out var d))
                        {
                            reg.RemainingDebt = d;
                            reg.PaidAmount = reg.TotalPrice - reg.RemainingDebt;
                            row.RemainingDebt = reg.RemainingDebt.ToString("0.##");
                            row.PaidAmount = reg.PaidAmount.ToString("0.##");
                        }
                    }
                    db.Entry(reg).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
                    db.SaveChanges();
                    try { ResultsDataGrid.Items.Refresh(); } catch { }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Kaydetme sırasında hata: " + ex.Message);
            }
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = child;
            while (parent != null)
            {
                if (parent is T correctlyTyped) return correctlyTyped;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        private bool TryParseDecimalFlexible(string s, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrWhiteSpace(s)) return false;
            // try invariant
            if (decimal.TryParse(s, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out value)) return true;
            // try current culture
            if (decimal.TryParse(s, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out value)) return true;
            // replace comma/point heuristics
            var alt = s.Replace(',', '.');
            if (decimal.TryParse(alt, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out value)) return true;
            alt = s.Replace('.', ',');
            if (decimal.TryParse(alt, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out value)) return true;
            return false;
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = (SearchTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                ResultsDataGrid.ItemsSource = _allRows;
                SearchHint.Visibility = Visibility.Visible;
                return;
            }

            SearchHint.Visibility = Visibility.Collapsed;
            // Önce Türkçe karakter transliterasyonu, sonra küçültme: böylece 'İ' gibi büyük harfler doğru çevrilir
            var q = NormalizeForSearch(text);

            // support column search like "Ad:ali" or "Ad: ali"
            if (q.Contains(":"))
            {
                var parts = q.Split(new[] { ':' }, 2);
                var key = TransliterateTurkish(parts[0].Trim()).ToLowerInvariant();
                var val = NormalizeForSearch(parts[1]);
                if (!string.IsNullOrEmpty(key))
                {
                    Func<GeneralRow, string> selector = GetSelectorForHeader(key);
                    if (selector != null)
                    {
                        // Eğer anahtar 'ad' ise ve değer boşluk içeriyorsa (ör. "Ad:Ali Oncel"),
                        // önce sadece ad ile, sonra "ad soyad" kombinasyonu ile de kontrol et.
                        if (key == "ad" && val.Contains(' '))
                        {
                            ResultsDataGrid.ItemsSource = _allRows.Where(r =>
                                NormalizeForSearch(r.FirstName ?? string.Empty).Contains(val) ||
                                NormalizeForSearch((r.FirstName ?? string.Empty) + " " + (r.LastName ?? string.Empty)).Contains(val)
                            ).ToList();
                            return;
                        }

                        ResultsDataGrid.ItemsSource = _allRows.Where(r => NormalizeForSearch(selector(r) ?? string.Empty).Contains(val)).ToList();
                        return;
                    }
                }
            }

            // Smart search with comma-separated segments: each segment must match (AND across segments)
            var segments = (q ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s)).ToList();

            var segFieldFilters = new List<List<(Func<GeneralRow, string> selector, string value)>>();
            var segGenericTokens = new List<List<string>>();

            foreach (var seg in segments)
            {
                var toks = seg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                var usedSeg = new bool[toks.Count];
                var fieldFiltersSeg = new List<(Func<GeneralRow, string>, string)>();

                Func<int, (Func<GeneralRow, string> sel, int len)> TryMatchFieldAtSeg = (i) =>
                {
                    var maxLen = Math.Min(3, toks.Count - i);
                    for (int len = maxLen; len >= 1; len--)
                    {
                        var candidate = string.Join(" ", toks.Skip(i).Take(len));
                        var sel = GetSelectorForHeader(candidate);
                        if (sel != null) return (sel, len);
                    }
                    return (null, 0);
                };

                Func<int, bool> IsFieldAtSeg = (i) => TryMatchFieldAtSeg(i).sel != null;

                for (int i = 0; i < toks.Count; i++)
                {
                    if (usedSeg[i]) continue;
                    var match = TryMatchFieldAtSeg(i);
                    if (match.sel != null)
                    {
                        for (int t = i; t < i + match.len; t++) if (t < usedSeg.Length) usedSeg[t] = true;
                        var maxValueTokens = 3;
                        var j = i + match.len;
                        var valueParts = new List<string>();
                        while (j < toks.Count && !IsFieldAtSeg(j) && valueParts.Count < maxValueTokens)
                        {
                            valueParts.Add(toks[j]);
                            usedSeg[j] = true;
                            j++;
                        }
                        if (valueParts.Count == 0)
                        {
                            var k = i - 1;
                            var leftParts = new List<string>();
                            while (k >= 0 && !IsFieldAtSeg(k) && !usedSeg[k])
                            {
                                leftParts.Insert(0, toks[k]);
                                usedSeg[k] = true;
                                k--;
                            }
                            valueParts.AddRange(leftParts);
                        }
                        var val = NormalizeForSearch(string.Join(" ", valueParts));
                        if (!string.IsNullOrEmpty(val)) fieldFiltersSeg.Add((match.sel, val));
                    }
                }

                var genericSeg = new List<string>();
                for (int i = 0; i < toks.Count; i++) if (!usedSeg[i] && !IsFieldAtSeg(i)) genericSeg.Add(NormalizeForSearch(toks[i]));

                segFieldFilters.Add(fieldFiltersSeg);
                segGenericTokens.Add(genericSeg);
            }

            ResultsDataGrid.ItemsSource = _allRows.Where(row =>
            {
                for (int s = 0; s < segFieldFilters.Count; s++)
                {
                    var fieldFiltersSeg = segFieldFilters[s];
                    var genericSeg = segGenericTokens[s];
                    // all field filters in this segment must match
                    foreach (var f in fieldFiltersSeg)
                    {
                        var cellVal = NormalizeForSearch(f.selector(row) ?? string.Empty);
                        if (!cellVal.Contains(f.value)) return false;
                    }
                    // all generic tokens in this segment must be found somewhere
                    foreach (var tok in genericSeg)
                    {
                        var found = false;
                        if (NormalizeForSearch(row.FirstName ?? string.Empty).Contains(tok)) found = true;
                        else if (NormalizeForSearch(row.LastName ?? string.Empty).Contains(tok)) found = true;
                        else if (NormalizeForSearch((row.FirstName ?? string.Empty) + " " + (row.LastName ?? string.Empty)).Contains(tok)) found = true;
                        else if (NormalizeForSearch(row.TcKimlikNo ?? string.Empty).Contains(tok)) found = true;
                        else if (NormalizeForSearch(row.PassportNumber ?? string.Empty).Contains(tok)) found = true;
                        else if (NormalizeForSearch(row.PhoneNumber ?? string.Empty).Contains(tok)) found = true;
                        else if (NormalizeForSearch(row.Address ?? string.Empty).Contains(tok)) found = true;
                        else if (NormalizeForSearch(row.Gender ?? string.Empty).Contains(tok)) found = true;
                        else if (NormalizeForSearch(row.PassportType ?? string.Empty).Contains(tok)) found = true;
                        else if (NormalizeForSearch(row.BloodType ?? string.Empty).Contains(tok)) found = true;
                        else if (NormalizeForSearch(row.HealthStatus ?? string.Empty).Contains(tok)) found = true;
                        else if (NormalizeForSearch(row.VisaRequest ?? string.Empty).Contains(tok)) found = true;
                        else if (NormalizeForSearch(row.TicketRequest ?? string.Empty).Contains(tok)) found = true;
                        else if (NormalizeForSearch(row.CargoRequest ?? string.Empty).Contains(tok)) found = true;
                        else if (NormalizeForSearch(row.RoomType ?? string.Empty).Contains(tok)) found = true;
                        else if (NormalizeForSearch(row.DepartureCity ?? string.Empty).Contains(tok)) found = true;
                        else if (NormalizeForSearch(row.HasTransfer ?? string.Empty).Contains(tok)) found = true;
                        else if (NormalizeForSearch(row.TransferCity ?? string.Empty).Contains(tok)) found = true;
                        else if (NormalizeForSearch(row.Reference ?? string.Empty).Contains(tok)) found = true;
                        if (!found) return false;
                    }
                }
                return true;
            }).ToList();
        }

        private string NormalizeForSearch(string input)
        {
            var s = TransliterateTurkish(input ?? string.Empty).ToLowerInvariant().Trim();
            // collapse multiple whitespace into single space
            var parts = s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts);
        }

        private Func<GeneralRow, string> GetSelectorForHeader(string headerKey)
        {
            // headerKey burada TransliterateTurkish(...).ToLowerInvariant() olacak şekilde çağrılıyor
            var k = TransliterateTurkish(headerKey).ToLowerInvariant();
            switch (k)
            {
                case "ad": return r => r.FirstName;
                case "soyad": return r => r.LastName;
                case "adsoyad":
                case "ad soyad":
                case "ad-soyad": return r => ((r.FirstName ?? string.Empty) + " " + (r.LastName ?? string.Empty)).Trim();
                case "tc":
                case "tc kimlik":
                case "tckimlik": return r => r.TcKimlikNo;
                case "cinsiyet": return r => r.Gender;
                case "telefon":
                case "telefonno":
                case "telefonnumarasi": return r => r.PhoneNumber;
                case "adres": return r => r.Address;
                // Treat plain "pasaport" as passport type (e.g. "bordo pasaport")
                case "pasaport":
                case "pasaporttipi":
                case "pasaporttur":
                case "passporttype": return r => r.PassportType;
                case "pasaportno":
                case "pasaportnumarasi":
                case "passport": return r => r.PassportNumber;
                case "kan grubu": return r => r.BloodType;
                case "hastalık":
                case "hastalık durumu": return r => r.HealthStatus;
                case "aşı":
                case "aşı durumu": return r => r.VaccineStatus;
                case "vize":
                case "vize talebi": return r => r.VisaRequest;
                case "bilet":
                case "bilet talebi": return r => r.TicketRequest;
                case "kargo":
                case "kargo talebi": return r => r.CargoRequest;
                case "oda tipi":
                case "odatipi": return r => r.RoomType;
                case "katilim yapilacak sehir":
                case "katilimyapilacaksehir":
                case "katilimsehir":
                case "katilim": return r => r.DepartureCity;
                case "aktarma":
                case "aktarmavar":
                case "aktarmavarmi": return r => r.HasTransfer;
                case "aktarmasehri":
                case "aktarmasehir": return r => r.TransferCity;
                case "referans": return r => r.Reference;
                default: return null;
            }
        }

        private void DesignButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new ListDesignDialog(ResultsDataGrid.Columns, _currentMode.ToString(), ResultsDataGrid);
                dialog.Owner = Application.Current?.MainWindow;
                var res = dialog.ShowDialog();
                if (res != true) return;

                var options = dialog.ColumnOptions;
                if (options == null) return;

                // Apply ordering (DisplayIndex) and visibility based on returned options
                var ordered = options.OrderBy(o => o.Order).ToList();
                for (int targetIndex = 0; targetIndex < ordered.Count; targetIndex++)
                {
                    var opt = ordered[targetIndex];
                    var col = ResultsDataGrid.Columns.FirstOrDefault(c => string.Equals(c.Header?.ToString(), opt.Title, StringComparison.OrdinalIgnoreCase));
                    if (col == null) continue;
                    col.DisplayIndex = targetIndex;
                    col.Visibility = opt.IsVisible ? Visibility.Visible : Visibility.Collapsed;
                }
                // save template for this mode automatically
                try
                {
                    var store = new Services.ListTemplateStore();
                    var list = ordered.Select(o => new Services.ListTemplateStore.ColumnDto { Title = o.Title, Order = o.Order, IsVisible = o.IsVisible }).ToList();
                    store.SaveTemplate(_currentMode.ToString(), list);
                }
                catch { }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var defaultName = GetExportDefaultFileName("csv");
            var dlg = new SaveFileDialog { Title = "CSV olarak kaydet", Filter = "CSV dosyası (*.csv)|*.csv", FileName = defaultName };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var visibleCols = ResultsDataGrid.Columns.Where(c => c.Visibility == Visibility.Visible).ToList();
                var headers = visibleCols.Select(c => c.Header?.ToString() ?? string.Empty).ToList();
                using var sw = new StreamWriter(dlg.FileName, false, Encoding.UTF8);
                sw.WriteLine(string.Join(',', headers.Select(h => EscapeCsv(h))));

                var rows = ResultsDataGrid.ItemsSource?.Cast<object>().ToList() ?? new List<object>();
                foreach (var item in rows)
                {
                    var vals = new List<string>();
                    foreach (var col in visibleCols)
                    {
                        var header = col.Header?.ToString() ?? string.Empty;
                        var val = GetPropertyStringValue(item, header);
                        vals.Add(EscapeCsv(val));
                    }
                    sw.WriteLine(string.Join(',', vals));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private string GetPropertyStringValue(object item, string header)
        {
            if (item == null) return string.Empty;
            var prop = item.GetType().GetProperties().FirstOrDefault(p => string.Equals(p.Name, MapHeaderToProperty(header), StringComparison.OrdinalIgnoreCase));
            if (prop == null)
            {
                // fallback: try by header name
                prop = item.GetType().GetProperty(header.Replace(" ", ""), System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            }
            if (prop == null) return string.Empty;
            var val = prop.GetValue(item);
            return val?.ToString() ?? string.Empty;
        }

        private string MapHeaderToProperty(string header)
        {
            return header?.Trim().ToLowerInvariant() switch
            {
                "sıra" => "Sequence",
                "ad" => "FirstName",
                "soyad" => "LastName",
                "tc kimlik" => "TcKimlikNo",
                "cinsiyet" => "Gender",
                "doğum tarihi" => "BirthDate",
                "telefon numarası" => "PhoneNumber",
                "adres" => "Address",
                "pasaport tipi" => "PassportType",
                "pasaport numarası" => "PassportNumber",
                "pasaport başlangıç tarihi" => "PassportStartDate",
                "pasaport bitiş tarihi" => "PassportEndDate",
                "kan grubu" => "BloodType",
                "aşı durumu" => "VaccineStatus",
                "hastalık durumu" => "HealthStatus",
                "yaş aralığı" => "AgeRange",
                "vize talebi" => "VisaRequest",
                "bilet talebi" => "TicketRequest",
                "kargo talebi" => "CargoRequest",
                "oda tipi" => "RoomType",
                "katılım yapılacak şehir" => "DepartureCity",
                "aktarma var mı ?" => "HasTransfer",
                "aktarma şehri" => "TransferCity",
                "ilçe" => "District",
                "toplam fiyat" => "TotalPrice",
                "para birimi" => "Currency",
                "ödenen" => "PaidAmount",
                "kalan" => "RemainingDebt",
                "referans" => "Reference",
                _ => header?.Replace(" ", "")
            };
        }

        private string EscapeCsv(string v)
        {
            if (v == null) return string.Empty;
            if (v.Contains(',') || v.Contains('"') || v.Contains('\n'))
                return '"' + v.Replace("\"", "\"\"") + '"';
            return v;
        }

        private class GeneralRow : System.ComponentModel.INotifyPropertyChanged
        {
            public int RegistrationId { get; set; }
            public int PersonId { get; set; }
            public int Sequence { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }
            public string TcKimlikNo { get; set; }
            public string Gender { get; set; }
            public string BirthDate { get; set; }
            public string PhoneNumber { get; set; }
            public string Address { get; set; }
            public string PassportType { get; set; }
            public string PassportNumber { get; set; }
            public string PassportStartDate { get; set; }
            public string PassportEndDate { get; set; }
            public string BloodType { get; set; }
            public string VaccineStatus { get; set; }
            public string HealthStatus { get; set; }
            public string AgeRange { get; set; }
            public string VisaRequest { get; set; }
            public string TicketRequest { get; set; }
            public string CargoRequest { get; set; }
            public string RoomType { get; set; }
            public string DepartureCity { get; set; }
            public string HasTransfer { get; set; }
            public string TransferCity { get; set; }
            public string District { get; set; }
            private string _totalPrice;
            public string TotalPrice
            {
                get => _totalPrice;
                set
                {
                    if (_totalPrice != value)
                    {
                        _totalPrice = value;
                        OnPropertyChanged(nameof(TotalPrice));
                        RecalculateRemainingFromStrings();
                    }
                }
            }
            public string Currency { get; set; }
            private string _paidAmount;
            public string PaidAmount
            {
                get => _paidAmount;
                set
                {
                    if (_paidAmount != value)
                    {
                        _paidAmount = value;
                        OnPropertyChanged(nameof(PaidAmount));
                        RecalculateRemainingFromStrings();
                    }
                }
            }
            private string _remainingDebt;
            public string RemainingDebt
            {
                get => _remainingDebt;
                set { if (_remainingDebt != value) { _remainingDebt = value; OnPropertyChanged(nameof(RemainingDebt)); } }
            }
            public string Reference { get; set; }

            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
            private void RecalculateRemainingFromStrings()
            {
                var t = ParseDecimalFlexible(_totalPrice);
                var p = ParseDecimalFlexible(_paidAmount);
                if (t.HasValue && p.HasValue)
                {
                    var rem = t.Value - p.Value;
                    var remStr = rem.ToString("0.##");
                    if (_remainingDebt != remStr)
                    {
                        _remainingDebt = remStr;
                        OnPropertyChanged(nameof(RemainingDebt));
                    }
                }
            }

            private decimal? ParseDecimalFlexible(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return null;
                if (decimal.TryParse(s, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v)) return v;
                if (decimal.TryParse(s, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out v)) return v;
                var alt = s.Replace(',', '.');
                if (decimal.TryParse(alt, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out v)) return v;
                alt = s.Replace('.', ',');
                if (decimal.TryParse(alt, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out v)) return v;
                return null;
            }
        }
    }
}

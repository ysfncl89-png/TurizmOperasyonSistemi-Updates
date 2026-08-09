using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TurizmOperasyonSistemi.Views
{
    public partial class KisiBazliTurFiyatiView : UserControl
    {
        private int _currentStep = 1;
        // Exchange rate fields
        private static readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();
        private decimal _usdRate = 0m; // TRY per USD
        private decimal _sarRate = 0m; // TRY per SAR
        private System.Windows.Threading.DispatcherTimer _ratesTimer;

        public KisiBazliTurFiyatiView()
        {
            InitializeComponent();
            SetStep(1);

            // Attach handlers so ComboBox opens when clicking the box area
            try { AttachComboBoxOpenOnBoxClick(); } catch { }

            // Load durations from settings (Kaç Günlük Grup)
            try { LoadDurationComboItems(); } catch { }

            // start fetching exchange rates
            try
            {
                _ratesTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
                _ratesTimer.Tick += async (s, e) => await FetchExchangeRatesAsync();
                _ratesTimer.Start();
                _ = FetchExchangeRatesAsync();
            }
            catch { }
        }

        private void LoadDurationComboItems()
        {
            try
            {
                using var db = new TurizmOperasyonSistemi.Data.AppDbContext();
                var allSettings = db.AppSettings.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToList();
                var durations = allSettings.Where(x => x.Type != null && (x.Type.Trim().Equals("Kaç Günlük Grup", StringComparison.OrdinalIgnoreCase) || x.Type.Trim().Equals("CountOfDays", StringComparison.OrdinalIgnoreCase))).Select(x => x.Value).Distinct().ToList();

                if (durations.Any())
                {
                    MekkeDaysCombo.ItemsSource = durations;
                    MedineDaysCombo.ItemsSource = durations;
                    MekkeDaysCombo_Walk.ItemsSource = durations;
                    MedineDaysCombo_Walk.ItemsSource = durations;

                    // Do not select any item by default — user will choose
                    MekkeDaysCombo.SelectedItem = null;
                    MedineDaysCombo.SelectedItem = null;
                    MekkeDaysCombo_Walk.SelectedItem = null;
                    MedineDaysCombo_Walk.SelectedItem = null;
                }
            }
            catch { }
        }

        private void AttachComboBoxOpenOnBoxClick()
        {
            try
            {
                AttachToChildren(this);
            }
            catch { }
        }

        private void AttachToChildren(System.Windows.DependencyObject parent)
        {
            if (parent == null) return;

            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);

                if (child is ComboBox cb)
                {
                    cb.PreviewMouseLeftButtonDown -= ComboBox_PreviewMouseLeftButtonDown;
                    cb.PreviewMouseLeftButtonDown += ComboBox_PreviewMouseLeftButtonDown;
                }

                AttachToChildren(child);
            }
        }

        private void ComboBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is ComboBox cb)
            {
                if (!cb.IsDropDownOpen)
                {
                    cb.IsDropDownOpen = true;
                    e.Handled = true;
                }
            }
        }

        private decimal TryParseRate(string txt)
        {
            if (string.IsNullOrWhiteSpace(txt)) return 0m;
            if (decimal.TryParse(txt, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return v;
            if (decimal.TryParse(txt, NumberStyles.Any, new CultureInfo("tr-TR"), out v)) return v;
            var cleaned = new string(txt.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray());
            if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
            if (decimal.TryParse(cleaned, NumberStyles.Any, new CultureInfo("tr-TR"), out v)) return v;
            return 0m;
        }

        private async System.Threading.Tasks.Task FetchExchangeRatesAsync()
        {
            try
            {
                const string url = "https://www.tcmb.gov.tr/kurlar/today.xml";
                string xml = await _httpClient.GetStringAsync(url).ConfigureAwait(false);
                var doc = System.Xml.Linq.XDocument.Parse(xml);

                string GetRate(string code, string elementName)
                {
                    try
                    {
                        var node = doc.Descendants("Currency").FirstOrDefault(x => (string)x.Attribute("CurrencyCode") == code);
                        if (node == null) return "-";
                        var el = node.Element(elementName);
                        if (el == null) return "-";
                        var val = el.Value?.Trim();
                        if (string.IsNullOrWhiteSpace(val)) return "-";
                        return val;
                    }
                    catch { return "-"; }
                }

                var usdBuy = GetRate("USD", "ForexBuying");
                var sarBuy = GetRate("SAR", "ForexBuying");

                _usdRate = TryParseRate(usdBuy);
                _sarRate = TryParseRate(sarBuy);
            }
            catch { }
        }

        private void SetStep(int step)
        {
            _currentStep = Math.Max(1, Math.Min(2, step));

            Step1Panel.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
            Step2Panel.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
            ResultsPanel.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;

            // Update header button visuals
            var activeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B5CF6"));
            var inactiveBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#072235"));
            var inactiveFg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));

            Step1Btn.Background = inactiveBg; Step1Btn.Foreground = inactiveFg;
            Step2Btn.Background = inactiveBg; Step2Btn.Foreground = inactiveFg;

            switch (_currentStep)
            {
                case 1:
                    Step1Btn.Background = activeBrush; Step1Btn.Foreground = Brushes.White;
                    break;
                case 2:
                    Step2Btn.Background = activeBrush; Step2Btn.Foreground = Brushes.White;
                    break;
            }

            // Prev/Next enable
            PrevBtn.IsEnabled = _currentStep > 1;
            NextBtn.IsEnabled = _currentStep < 3;
        }

        private void Step1Btn_Click(object sender, RoutedEventArgs e) => SetStep(1);
        private void Step2Btn_Click(object sender, RoutedEventArgs e) => SetStep(2);
        // Step3 removed — no handler needed

        private void PrevBtn_Click(object sender, RoutedEventArgs e)
        {
            SetStep(_currentStep - 1);
        }

        private void NextBtn_Click(object sender, RoutedEventArgs e)
        {
            SetStep(_currentStep + 1);
        }

        // Staff expense is entered directly by user; no per-person calculation needed now.

        private void PriceInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            RecalculateTotals();
        }

        private void ExtraVisitsTxt_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Only update SAR->USD display for extras. Do not include these in hotel totals here.
            UpdateExtraSarToUsdDisplay(ExtraVisitsTxt, ExtraVisitsUsd);
            // update step2 totals after converting extra visits to USD
            UpdateStep2GiderUsdAndTotals();
        }

        private void MealExpenseTxt_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Meal expense is entered in USD by the user
            UpdateStep2GiderUsdAndTotals();
        }

        private void Gider_TextChanged(object sender, TextChangedEventArgs e)
        {
            // recalc Step2 USD total when any USD denominator input changes
            UpdateStep2GiderUsdAndTotals();
        }

        private void TurSalesPriceTxt_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateProfitDisplay();
        }

        private void UpdateStep2GiderUsdAndTotals()
        {
            try
            {
                decimal ticket = ParseDecimalFromText(TicketTxt?.Text);
                decimal visa = ParseDecimalFromText(VisaTxt?.Text);
                decimal bag = ParseDecimalFromText(BagTxt?.Text);
                decimal staff = ParseDecimalFromText(StaffExpenseTxt?.Text);

                decimal extrasUsd = 0m;
                // extras entered in SAR are converted to USD (ExtraVisits)
                if (!string.IsNullOrWhiteSpace(ExtraVisitsUsd?.Text) && ExtraVisitsUsd.Text != "-")
                {
                    extrasUsd += ParseDecimalFromText(ExtraVisitsUsd.Text.Replace("$", ""));
                }

                // Meal expense is entered directly in USD by user
                var mealBox = FindName("MealExpenseTxt") as TextBox;
                decimal mealUsd = ParseDecimalFromText(mealBox?.Text);

                decimal giderUsd = ticket + visa + bag + staff + extrasUsd + mealUsd;
                Step2GiderUsd.Text = giderUsd > 0 ? giderUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";

                // Also update Step1 overall USD into Step1OverallUsd
                // OverallUsd Text already computed in RecalculateTotals; read and copy
                Step1OverallUsd.Text = OverallUsd.Text;
                // NO-OP placeholder: keep file up-to-date before adding custom rate logic

                // Tur Maliyeti = Otel (Step1OverallUsd) + Giderler (giderUsd)
                decimal otelUsd = 0m;
                if (!string.IsNullOrWhiteSpace(OverallUsd?.Text) && OverallUsd.Text != "-")
                {
                    otelUsd = ParseDecimalFromText(OverallUsd.Text.Replace("$", ""));
                }
                decimal turCost = otelUsd + giderUsd;
                TurCostUsd.Text = turCost > 0 ? turCost.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";

                // update profit if sales price present
                UpdateProfitDisplay();
            }
            catch { }
        }

        private bool _useCustomRate = false;
        private decimal _customSarToUsd = 0m;

        private void UseCustomRateChk_Checked(object sender, RoutedEventArgs e)
        {
            _useCustomRate = true;
            CustomSarToUsdTxt.Visibility = Visibility.Visible;
            CustomRateNote.Visibility = Visibility.Visible;
        }

        private void UseCustomRateChk_Unchecked(object sender, RoutedEventArgs e)
        {
            _useCustomRate = false;
            CustomSarToUsdTxt.Visibility = Visibility.Collapsed;
            CustomRateNote.Visibility = Visibility.Collapsed;
            // reset to fetched rates
            _customSarToUsd = 0m;
            // re-run recalculations to use TCMB rates
            RecalculateTotals();
            UpdateStep2GiderUsdAndTotals();
        }

        private void CustomSarToUsdTxt_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                var val = ParseDecimalFromText(CustomSarToUsdTxt?.Text);
                if (val > 0)
                {
                    _customSarToUsd = val;
                }
                else
                {
                    _customSarToUsd = 0m;
                }

                // When custom rate is set, ensure conversions use it
                RecalculateTotals();
                UpdateStep2GiderUsdAndTotals();
            }
            catch { }
        }

        private void UpdateProfitDisplay()
        {
            try
            {
                decimal turCost = 0m;
                if (!string.IsNullOrWhiteSpace(TurCostUsd?.Text) && TurCostUsd.Text != "-")
                    turCost = ParseDecimalFromText(TurCostUsd.Text.Replace("$", ""));

                decimal sales = ParseDecimalFromText(TurSalesPriceTxt?.Text);
                if (sales > 0 && turCost > 0)
                {
                    var profit = sales - turCost;
                    TurProfitTxt.Text = profit.ToString("N2", CultureInfo.CurrentCulture) + " $";
                }
                else if (sales > 0)
                {
                    TurProfitTxt.Text = sales.ToString("N2", CultureInfo.CurrentCulture) + " $";
                }
                else
                {
                    TurProfitTxt.Text = "-";
                }
            }
            catch { TurProfitTxt.Text = "-"; }
        }

        private void UpdateExtraSarToUsdDisplay(TextBox sarBox, TextBlock usdBlock)
        {
            try
            {
                var sar = ParseDecimalFromText(sarBox?.Text);
                if (sar <= 0m)
                {
                    usdBlock.Text = "-";
                    return;
                }

                // If user provided custom rate (entered as 1 USD = X SAR), use it; otherwise use TCMB rates
                if (_useCustomRate && _customSarToUsd > 0m)
                {
                    // _customSarToUsd now represents SAR per 1 USD. To convert SAR -> USD divide.
                    var usd = sar / _customSarToUsd;
                    usdBlock.Text = usd.ToString("N2", CultureInfo.CurrentCulture) + " $";
                }
                else if (_usdRate > 0m && _sarRate > 0m)
                {
                    var usd = sar * (_sarRate / _usdRate);
                    usdBlock.Text = usd.ToString("N2", CultureInfo.CurrentCulture) + " $";
                }
                else
                {
                    usdBlock.Text = "-";
                }
            }
            catch { usdBlock.Text = "-"; }
        }

        private void RecalculateTotals()
        {
            try
            {
                // For each of four groups compute SAR totals
                decimal econMekke = SumPricesForGroup(new[] { MekkeSingleTxt, MekkeDblTxt, MekkeTrplTxt, MekkeQuadTxt, MekkeQuintTxt }, MekkeDaysCombo);
                decimal econMedine = SumPricesForGroup(new[] { MedineSingleTxt, MedineDblTxt, MedineTrplTxt, MedineQuadTxt, MedineQuintTxt }, MedineDaysCombo);
                decimal walkMekke = SumPricesForGroup(new[] { MekkeSingleTxt_Walk, MekkeDblTxt_Walk, MekkeTrplTxt_Walk, MekkeQuadTxt_Walk, MekkeQuintTxt_Walk }, MekkeDaysCombo_Walk);
                decimal walkMedine = SumPricesForGroup(new[] { MedineSingleTxt_Walk, MedineDblTxt_Walk, MedineTrplTxt_Walk, MedineQuadTxt_Walk, MedineQuintTxt_Walk }, MedineDaysCombo_Walk);

                EconMekkeSar.Text = econMekke > 0 ? econMekke.ToString("N2", CultureInfo.CurrentCulture) + " SAR" : "-";
                EconMedineSar.Text = econMedine > 0 ? econMedine.ToString("N2", CultureInfo.CurrentCulture) + " SAR" : "-";
                WalkMekkeSar.Text = walkMekke > 0 ? walkMekke.ToString("N2", CultureInfo.CurrentCulture) + " SAR" : "-";
                WalkMedineSar.Text = walkMedine > 0 ? walkMedine.ToString("N2", CultureInfo.CurrentCulture) + " SAR" : "-";

                // Convert SAR to USD using TCMB rates (USD = SAR * (SAR_try / USD_try))
                string econMekkeUsd = "-", econMedineUsd = "-", walkMekkeUsd = "-", walkMedineUsd = "-";
                // Note: extras (ExtraVisits) are SAR inputs shown in Masraflar step
                // They are not included here in hotel price totals – user requested these not affect Step1 calculations.
                decimal overallSar = econMekke + econMedine + walkMekke + walkMedine;
                string overallUsd = "-";

                if (_usdRate > 0 && _sarRate > 0)
                {
                    Func<decimal, string> toUsd = sar =>
                    {
                        if (_useCustomRate && _customSarToUsd > 0m)
                        {
                            // _customSarToUsd is SAR per 1 USD (user inputs 1 USD = X SAR)
                            var usd = sar / _customSarToUsd;
                            return usd.ToString("N2", CultureInfo.CurrentCulture) + " $";
                        }
                        return (sar * (_sarRate / _usdRate)).ToString("N2", CultureInfo.CurrentCulture) + " $";
                    };
                    if (econMekke > 0) econMekkeUsd = toUsd(econMekke);
                    if (econMedine > 0) econMedineUsd = toUsd(econMedine);
                    if (walkMekke > 0) walkMekkeUsd = toUsd(walkMekke);
                    if (walkMedine > 0) walkMedineUsd = toUsd(walkMedine);
                    overallUsd = toUsd(overallSar);
                }

                EconMekkeUsd.Text = econMekkeUsd;
                EconMedineUsd.Text = econMedineUsd;
                WalkMekkeUsd.Text = walkMekkeUsd;
                WalkMedineUsd.Text = walkMedineUsd;

                OverallSar.Text = overallSar > 0 ? overallSar.ToString("N2", CultureInfo.CurrentCulture) + " SAR" : "-";
                OverallUsd.Text = overallUsd;

                // Whenever Step1 totals recalculated, update Step2 displays too
                UpdateStep2GiderUsdAndTotals();
            }
            catch { }
        }

        private decimal SumPricesForGroup(TextBox[] boxes, ComboBox daysCombo)
        {
            int days = ParseDaysCount(daysCombo?.SelectedItem?.ToString());
            decimal sum = 0m;
            foreach (var tb in boxes)
            {
                if (tb == null) continue;
                var v = ParseDecimalFromText(tb.Text);
                if (v > 0 && days > 0) sum += v * days;
            }
            return sum;
        }

        private int ParseDaysCount(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 0;
            // extract digits
            var digits = new string(raw.Where(c => char.IsDigit(c)).ToArray());
            if (int.TryParse(digits, out var d)) return d;
            return 0;
        }

        private decimal ParseDecimalFromText(string txt)
        {
            if (string.IsNullOrWhiteSpace(txt)) return 0m;
            if (decimal.TryParse(txt, NumberStyles.Any, CultureInfo.CurrentCulture, out var v)) return v;
            if (decimal.TryParse(txt, NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
            if (decimal.TryParse(txt, NumberStyles.Any, new CultureInfo("tr-TR"), out v)) return v;
            // remove non-numeric
            var cleaned = new string(txt.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray());
            if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
            if (decimal.TryParse(cleaned, NumberStyles.Any, new CultureInfo("tr-TR"), out v)) return v;
            return 0m;
        }

        // wire combobox selection changed to recalc
        private void MekkeDaysCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RecalculateTotals();
        private void MedineDaysCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RecalculateTotals();
        private void MekkeDaysCombo_Walk_SelectionChanged(object sender, SelectionChangedEventArgs e) => RecalculateTotals();
        private void MedineDaysCombo_Walk_SelectionChanged(object sender, SelectionChangedEventArgs e) => RecalculateTotals();
    }
}

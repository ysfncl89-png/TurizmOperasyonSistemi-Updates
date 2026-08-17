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
        // last computed tour cost (used for profit calculation after Tur Maliyeti UI removed)
        private decimal _lastTurCost = 0m;
        // last gider share added to each category (we add full giderUsd to each per user's request)
        private decimal _lastGiderShare = 0m;
        // total OTEL USD (stored for Step2 calculations)
        private decimal _otelUsdTotal = 0m;
        // per-person base room USD values (without shared gider split)
        private decimal _perPersonSingleBaseUsd = 0m;
        private decimal _perPersonDblBaseUsd = 0m;
        private decimal _perPersonTrplBaseUsd = 0m;
        private decimal _perPersonQuadBaseUsd = 0m;
        private decimal _perPersonQuintBaseUsd = 0m;
        // store per-category per-person USD values from RecalculateTotals so UpdateStep2 can use them without parsing UI
        private decimal _econSingleUsd = 0m;
        private decimal _econDblUsd = 0m;
        private decimal _econTrplUsd = 0m;
        private decimal _econQuadUsd = 0m;
        private decimal _econQuintUsd = 0m;
        private decimal _walkSingleUsd = 0m;
        private decimal _walkDblUsd = 0m;
        private decimal _walkTrplUsd = 0m;
        private decimal _walkQuadUsd = 0m;
        private decimal _walkQuintUsd = 0m;

        public KisiBazliTurFiyatiView()
        {
            InitializeComponent();
            PopulateResultQuantityComboBoxes();
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
            // allow 3 steps now
            _currentStep = Math.Max(1, Math.Min(3, step));

            Step1Panel.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
            Step2Panel.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
            ResultsPanel.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
            // Step3 content panel (Sonuç) - use FindName to avoid referencing a field that may not be generated
            try
            {
                var s3 = FindName("Step3Panel") as Border;
                if (s3 != null) s3.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { }

            // Update header button visuals
            var activeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B5CF6"));
            var inactiveBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#072235"));
            var inactiveFg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));

            Step1Btn.Background = inactiveBg; Step1Btn.Foreground = inactiveFg;
            Step2Btn.Background = inactiveBg; Step2Btn.Foreground = inactiveFg;
            // ensure Step3 button exists before modifying
            try { Step3Btn.Background = inactiveBg; Step3Btn.Foreground = inactiveFg; } catch { }

            switch (_currentStep)
            {
                case 1:
                    Step1Btn.Background = activeBrush; Step1Btn.Foreground = Brushes.White;
                    break;
                case 2:
                    Step2Btn.Background = activeBrush; Step2Btn.Foreground = Brushes.White;
                    break;
                case 3:
                    try { Step3Btn.Background = activeBrush; Step3Btn.Foreground = Brushes.White; } catch { }
                    break;
            }

            // Prev/Next enable
            PrevBtn.IsEnabled = _currentStep > 1;
            NextBtn.IsEnabled = _currentStep < 3;

            if (_currentStep == 3)
                UpdateResultSummary();
        }

        private void Step1Btn_Click(object sender, RoutedEventArgs e) => SetStep(1);
        private void Step2Btn_Click(object sender, RoutedEventArgs e) => SetStep(2);
        private void Step3Btn_Click(object sender, RoutedEventArgs e) => SetStep(3);

        private void PrevBtn_Click(object sender, RoutedEventArgs e)
        {
            SetStep(_currentStep - 1);
        }

        private void NextBtn_Click(object sender, RoutedEventArgs e)
        {
            SetStep(_currentStep + 1);
        }

        private void CalcBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var w = new CalculatorWindow();
                // set owner if window is hosted in a Window
                var wnd = Window.GetWindow(this);
                if (wnd != null) w.Owner = wnd;
                w.Show();
            }
            catch { }
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
            // also update meal expense SAR->USD display if present
            try { UpdateExtraSarToUsdDisplay(MealExpenseTxt, MealExpenseUsd); } catch { }
            // update step2 totals after converting extra visits to USD
            UpdateStep2GiderUsdAndTotals();
        }

        private void MealExpenseTxt_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Meal expense is entered in SAR by the user; update SAR->USD display and totals
            try { UpdateExtraSarToUsdDisplay(MealExpenseTxt, MealExpenseUsd); } catch { }
            UpdateStep2GiderUsdAndTotals();
        }

        private void Gider_TextChanged(object sender, TextChangedEventArgs e)
        {
            // recalc Step2 USD total when any USD denominator input changes
            UpdateStep2GiderUsdAndTotals();
        }

        private void TurSalesPriceTxt_TextChanged(object sender, TextChangedEventArgs e)
        {
            // no-op: global sales box removed; per-occupancy sales inputs drive profit updates
        }

        private void PerOccupancySales_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                // When any per-occupancy sales value changes, recalc profits using the corresponding sales value
                UpdateProfitDisplayPerInputs();
                UpdateResultSummary();
            }
            catch { }
        }

        private void PopulateResultQuantityComboBoxes()
        {
            var comboNames = new[]
            {
                "ResultEconQtySingle", "ResultEconQtyDbl", "ResultEconQtyTrpl", "ResultEconQtyQuad", "ResultEconQtyQuint",
                "ResultWalkQtySingle", "ResultWalkQtyDbl", "ResultWalkQtyTrpl", "ResultWalkQtyQuad", "ResultWalkQtyQuint"
            };

            foreach (var name in comboNames)
            {
                if (FindName(name) is not ComboBox combo) continue;
                combo.Items.Clear();
                for (var quantity = 1; quantity <= 100; quantity++)
                    combo.Items.Add(quantity);
                combo.SelectedIndex = 0;
            }
        }

        private void ResultQuantity_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateResultSummary();
        }

        private int GetResultQuantity(string comboName)
        {
            if (FindName(comboName) is ComboBox { SelectedItem: not null } combo &&
                int.TryParse(combo.SelectedItem.ToString(), out var quantity))
                return quantity;
            return 0;
        }

        private decimal GetSalesPrice(string salesTextBoxName)
        {
            return FindName(salesTextBoxName) is TextBox textBox ? ParseDecimalFromText(textBox.Text) : 0m;
        }

        private void UpdateResultSummary()
        {
            try
            {
                UpdateResultSection(
                    "ResultEcon", new[] { "Single", "Dbl", "Trpl", "Quad", "Quint" },
                    new[] { _econSingleUsd, _econDblUsd, _econTrplUsd, _econQuadUsd, _econQuintUsd },
                    new[] { "SalesEconSingle", "SalesEconDbl", "SalesEconTrpl", "SalesEconQuad", "SalesEconQuint" });

                UpdateResultSection(
                    "ResultWalk", new[] { "Single", "Dbl", "Trpl", "Quad", "Quint" },
                    new[] { _walkSingleUsd, _walkDblUsd, _walkTrplUsd, _walkQuadUsd, _walkQuintUsd },
                    new[] { "SalesWalkSingle", "SalesWalkDbl", "SalesWalkTrpl", "SalesWalkQuad", "SalesWalkQuint" });
            }
            catch { }
        }

        private void UpdateResultSection(string resultPrefix, string[] occupancies, decimal[] hotelCosts, string[] salesTextBoxNames)
        {
            decimal totalCost = 0m;
            decimal totalSales = 0m;

            for (var i = 0; i < occupancies.Length; i++)
            {
                var occupancy = occupancies[i];
                var unitCost = hotelCosts[i] > 0m ? hotelCosts[i] + _lastGiderShare : 0m;
                var unitSales = GetSalesPrice(salesTextBoxNames[i]);
                var quantity = GetResultQuantity($"{resultPrefix}Qty{occupancy}");

                SetResultText($"{resultPrefix}Cost{occupancy}", unitCost);
                SetResultText($"{resultPrefix}Sales{occupancy}", unitSales);

                totalCost += unitCost * quantity;
                totalSales += unitSales * quantity;
            }

            if (FindName($"{resultPrefix}TotalCost") is TextBlock costTotal)
                costTotal.Text = $"Toplam Tur Maliyeti: {totalCost.ToString("N2", CultureInfo.CurrentCulture)} $";
            if (FindName($"{resultPrefix}TotalSales") is TextBlock salesTotal)
                salesTotal.Text = $"Toplam Satış Fiyatı: {totalSales.ToString("N2", CultureInfo.CurrentCulture)} $";
            if (FindName($"{resultPrefix}TotalProfit") is TextBlock profitTotal)
                profitTotal.Text = $"Elde Edilebilecek Kâr: {(totalSales - totalCost).ToString("N2", CultureInfo.CurrentCulture)} $";
        }

        private void SetResultText(string name, decimal value)
        {
            if (FindName(name) is TextBlock textBlock)
                textBlock.Text = value > 0m ? value.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";
        }

        private void UpdateProfitDisplayPerInputs()
        {
            try
            {
                // For each occupancy, read its Sales textbox; if present and >0, compute profit = sales - (perPersonCost + giderShare)
                decimal v;

                if (TryParseTextBoxValue("SalesEconSingle", out v) && _econSingleUsd > 0m) SetProfitText("ProfitEconSingle", v - (_econSingleUsd + _lastGiderShare)); else ClearProfitText("ProfitEconSingle");
                if (TryParseTextBoxValue("SalesEconDbl", out v) && _econDblUsd > 0m) SetProfitText("ProfitEconDbl", v - (_econDblUsd + _lastGiderShare)); else ClearProfitText("ProfitEconDbl");
                if (TryParseTextBoxValue("SalesEconTrpl", out v) && _econTrplUsd > 0m) SetProfitText("ProfitEconTrpl", v - (_econTrplUsd + _lastGiderShare)); else ClearProfitText("ProfitEconTrpl");
                if (TryParseTextBoxValue("SalesEconQuad", out v) && _econQuadUsd > 0m) SetProfitText("ProfitEconQuad", v - (_econQuadUsd + _lastGiderShare)); else ClearProfitText("ProfitEconQuad");
                if (TryParseTextBoxValue("SalesEconQuint", out v) && _econQuintUsd > 0m) SetProfitText("ProfitEconQuint", v - (_econQuintUsd + _lastGiderShare)); else ClearProfitText("ProfitEconQuint");

                if (TryParseTextBoxValue("SalesWalkSingle", out v) && _walkSingleUsd > 0m) SetProfitText("ProfitWalkSingle", v - (_walkSingleUsd + _lastGiderShare)); else ClearProfitText("ProfitWalkSingle");
                if (TryParseTextBoxValue("SalesWalkDbl", out v) && _walkDblUsd > 0m) SetProfitText("ProfitWalkDbl", v - (_walkDblUsd + _lastGiderShare)); else ClearProfitText("ProfitWalkDbl");
                if (TryParseTextBoxValue("SalesWalkTrpl", out v) && _walkTrplUsd > 0m) SetProfitText("ProfitWalkTrpl", v - (_walkTrplUsd + _lastGiderShare)); else ClearProfitText("ProfitWalkTrpl");
                if (TryParseTextBoxValue("SalesWalkQuad", out v) && _walkQuadUsd > 0m) SetProfitText("ProfitWalkQuad", v - (_walkQuadUsd + _lastGiderShare)); else ClearProfitText("ProfitWalkQuad");
                if (TryParseTextBoxValue("SalesWalkQuint", out v) && _walkQuintUsd > 0m) SetProfitText("ProfitWalkQuint", v - (_walkQuintUsd + _lastGiderShare)); else ClearProfitText("ProfitWalkQuint");
            }
            catch { }
        }

        private bool TryParseTextBoxValue(string name, out decimal value)
        {
            value = 0m;
            try
            {
                var tb = FindName(name) as TextBox;
                if (tb == null) return false;
                var v = ParseDecimalFromText(tb.Text);
                if (v <= 0m) return false;
                value = v;
                return true;
            }
            catch { return false; }
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

                // Meal expense is entered in SAR; converted display stored in MealExpenseUsd
                decimal mealUsd = 0m;
                if (!string.IsNullOrWhiteSpace(MealExpenseUsd?.Text) && MealExpenseUsd.Text != "-")
                {
                    mealUsd = ParseDecimalFromText(MealExpenseUsd.Text.Replace("$", ""));
                }

                decimal giderUsd = ticket + visa + bag + staff + extrasUsd + mealUsd;
                Step2GiderUsd.Text = giderUsd > 0 ? giderUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";

                // Also update Step1 overall USD into Step1OverallUsd (show per-person overall USD as displayed in Step1)
                Step1OverallUsd.Text = OverallUsd.Text;

                // Tur Maliyeti = Otel (total OTEL USD) + Giderler (giderUsd)
                decimal otelUsd = _otelUsdTotal;
                decimal turCost = otelUsd + giderUsd;
                // store last calculated tour cost for profit calculations (UI "Tur Maliyeti" removed)
                _lastTurCost = turCost;

                // Determine which combined room categories have base values and distribute giderUsd among them
                var nonEmptyCategories = new System.Collections.Generic.List<string>();
                if (_perPersonSingleBaseUsd > 0m) nonEmptyCategories.Add("single");
                if (_perPersonDblBaseUsd > 0m) nonEmptyCategories.Add("dbl");
                if (_perPersonTrplBaseUsd > 0m) nonEmptyCategories.Add("trpl");
                if (_perPersonQuadBaseUsd > 0m) nonEmptyCategories.Add("quad");
                if (_perPersonQuintBaseUsd > 0m) nonEmptyCategories.Add("quint");

                // perShare: per-category addition. Per user's request, add full giderUsd to each category (do not divide).
                decimal perShare = giderUsd;
                _lastGiderShare = perShare;

                try
                {
                    var rcSingle = FindName("RoomCostSingle") as TextBlock;
                    if (rcSingle != null)
                    {
                        if (_perPersonSingleBaseUsd > 0m)
                            rcSingle.Text = (_perPersonSingleBaseUsd + perShare).ToString("N2", CultureInfo.CurrentCulture) + " $";
                        else
                            rcSingle.Text = "-";
                    }

                    var rcDbl = FindName("RoomCostDbl") as TextBlock;
                    if (rcDbl != null)
                    {
                        if (_perPersonDblBaseUsd > 0m)
                            rcDbl.Text = (_perPersonDblBaseUsd + perShare).ToString("N2", CultureInfo.CurrentCulture) + " $";
                        else
                            rcDbl.Text = "-";
                    }

                    var rcTrpl = FindName("RoomCostTrpl") as TextBlock;
                    if (rcTrpl != null)
                    {
                        if (_perPersonTrplBaseUsd > 0m)
                            rcTrpl.Text = (_perPersonTrplBaseUsd + perShare).ToString("N2", CultureInfo.CurrentCulture) + " $";
                        else
                            rcTrpl.Text = "-";
                    }

                    var rcQuad = FindName("RoomCostQuad") as TextBlock;
                    if (rcQuad != null)
                    {
                        if (_perPersonQuadBaseUsd > 0m)
                            rcQuad.Text = (_perPersonQuadBaseUsd + perShare).ToString("N2", CultureInfo.CurrentCulture) + " $";
                        else
                            rcQuad.Text = "-";
                    }

                    var rcQuint = FindName("RoomCostQuint") as TextBlock;
                    if (rcQuint != null)
                    {
                        if (_perPersonQuintBaseUsd > 0m)
                            rcQuint.Text = (_perPersonQuintBaseUsd + perShare).ToString("N2", CultureInfo.CurrentCulture) + " $";
                        else
                            rcQuint.Text = "-";
                    }
                }
                catch { }

                // After updating room costs, refresh profit breakdown from per-occupancy sales inputs
                UpdateProfitDisplayPerInputs();

                // Additionally add full giderUsd to each category that has a computed base per-person USD
                try
                {
                    var eSingle = FindName("EconRoomSingle") as TextBlock;
                    var eDbl = FindName("EconRoomDbl") as TextBlock;
                    var eTrpl = FindName("EconRoomTrpl") as TextBlock;
                    var eQuad = FindName("EconRoomQuad") as TextBlock;
                    var eQuint = FindName("EconRoomQuint") as TextBlock;

                    var wSingle = FindName("WalkRoomSingle") as TextBlock;
                    var wDbl = FindName("WalkRoomDbl") as TextBlock;
                    var wTrpl = FindName("WalkRoomTrpl") as TextBlock;
                    var wQuad = FindName("WalkRoomQuad") as TextBlock;
                    var wQuint = FindName("WalkRoomQuint") as TextBlock;

                    if (eSingle != null && _econSingleUsd > 0m) eSingle.Text = (_econSingleUsd + perShare).ToString("N2", CultureInfo.CurrentCulture) + " $";
                    if (eDbl != null && _econDblUsd > 0m) eDbl.Text = (_econDblUsd + perShare).ToString("N2", CultureInfo.CurrentCulture) + " $";
                    if (eTrpl != null && _econTrplUsd > 0m) eTrpl.Text = (_econTrplUsd + perShare).ToString("N2", CultureInfo.CurrentCulture) + " $";
                    if (eQuad != null && _econQuadUsd > 0m) eQuad.Text = (_econQuadUsd + perShare).ToString("N2", CultureInfo.CurrentCulture) + " $";
                    if (eQuint != null && _econQuintUsd > 0m) eQuint.Text = (_econQuintUsd + perShare).ToString("N2", CultureInfo.CurrentCulture) + " $";

                    if (wSingle != null && _walkSingleUsd > 0m) wSingle.Text = (_walkSingleUsd + perShare).ToString("N2", CultureInfo.CurrentCulture) + " $";
                    if (wDbl != null && _walkDblUsd > 0m) wDbl.Text = (_walkDblUsd + perShare).ToString("N2", CultureInfo.CurrentCulture) + " $";
                    if (wTrpl != null && _walkTrplUsd > 0m) wTrpl.Text = (_walkTrplUsd + perShare).ToString("N2", CultureInfo.CurrentCulture) + " $";
                    if (wQuad != null && _walkQuadUsd > 0m) wQuad.Text = (_walkQuadUsd + perShare).ToString("N2", CultureInfo.CurrentCulture) + " $";
                    if (wQuint != null && _walkQuintUsd > 0m) wQuint.Text = (_walkQuintUsd + perShare).ToString("N2", CultureInfo.CurrentCulture) + " $";
                }
                catch { }

                // update profit if sales price present (per-occupancy inputs)
                UpdateProfitDisplayPerInputs();
                UpdateResultSummary();
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
            // refresh any SAR->USD displays when enabling custom rate
            try { UpdateExtraSarToUsdDisplay(ExtraVisitsTxt, ExtraVisitsUsd); } catch { }
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
            // refresh SAR->USD displays and step2 totals
            try { UpdateExtraSarToUsdDisplay(ExtraVisitsTxt, ExtraVisitsUsd); } catch { }
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
                // update SAR->USD displays that depend on the custom rate
                try { UpdateExtraSarToUsdDisplay(ExtraVisitsTxt, ExtraVisitsUsd); } catch { }
                UpdateStep2GiderUsdAndTotals();
            }
            catch { }
        }

        private void UpdateProfitDisplay()
        {
            // deprecated: global sales field removed. Use UpdateProfitDisplayPerInputs instead.
            UpdateProfitDisplayPerInputs();
        }

        private void SetProfitText(string name, decimal value)
        {
            try
            {
                var tb = FindName(name) as TextBlock;
                if (tb == null) return;
                if (value == 0m)
                {
                    tb.Text = "-";
                    return;
                }
                tb.Text = value.ToString("N2", CultureInfo.CurrentCulture) + " $";
            }
            catch { }
        }

        private void ClearProfitText(string name)
        {
            try
            {
                var tb = FindName(name) as TextBlock;
                if (tb != null) tb.Text = "-";
            }
            catch { }
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
                // For each of four groups compute SAR totals (room totals)
                decimal econMekke = SumPricesForGroup(new[] { MekkeSingleTxt, MekkeDblTxt, MekkeTrplTxt, MekkeQuadTxt, MekkeQuintTxt }, MekkeDaysCombo);
                decimal econMedine = SumPricesForGroup(new[] { MedineSingleTxt, MedineDblTxt, MedineTrplTxt, MedineQuadTxt, MedineQuintTxt }, MedineDaysCombo);
                decimal walkMekke = SumPricesForGroup(new[] { MekkeSingleTxt_Walk, MekkeDblTxt_Walk, MekkeTrplTxt_Walk, MekkeQuadTxt_Walk, MekkeQuintTxt_Walk }, MekkeDaysCombo_Walk);
                decimal walkMedine = SumPricesForGroup(new[] { MedineSingleTxt_Walk, MedineDblTxt_Walk, MedineTrplTxt_Walk, MedineQuadTxt_Walk, MedineQuintTxt_Walk }, MedineDaysCombo_Walk);

                // Total SAR across all groups (room totals)
                decimal overallSarTotal = econMekke + econMedine + walkMekke + walkMedine;

                // helper decimal converter SAR -> USD
                decimal SarToUsdDecimal(decimal sar)
                {
                    if (sar <= 0m) return 0m;
                    if (_useCustomRate && _customSarToUsd > 0m) return sar / _customSarToUsd;
                    if (_usdRate > 0m && _sarRate > 0m) return sar * (_sarRate / _usdRate);
                    return 0m;
                }

                // store total OTEL USD for Step2 calculations (unchanged total)
                _otelUsdTotal = SarToUsdDecimal(overallSarTotal);

                // helper to compute per-person SAR for a single textbox
                decimal PerPersonSarFor(TextBox tb, ComboBox daysCombo, int occupancy)
                {
                    if (tb == null) return 0m;
                    var price = ParseDecimalFromText(tb.Text);
                    var days = ParseDaysCount(daysCombo?.SelectedItem?.ToString());
                    if (price <= 0m || days <= 0 || occupancy <= 0) return 0m;
                    var totalSarForRoom = price * days;
                    return totalSarForRoom / occupancy;
                }

                // compute per-person SAR totals for each group
                decimal econMekkePer = PerPersonSarFor(MekkeSingleTxt, MekkeDaysCombo, 1)
                                      + PerPersonSarFor(MekkeDblTxt, MekkeDaysCombo, 2)
                                      + PerPersonSarFor(MekkeTrplTxt, MekkeDaysCombo, 3)
                                      + PerPersonSarFor(MekkeQuadTxt, MekkeDaysCombo, 4)
                                      + PerPersonSarFor(MekkeQuintTxt, MekkeDaysCombo, 5);

                decimal econMedinePer = PerPersonSarFor(MedineSingleTxt, MedineDaysCombo, 1)
                                      + PerPersonSarFor(MedineDblTxt, MedineDaysCombo, 2)
                                      + PerPersonSarFor(MedineTrplTxt, MedineDaysCombo, 3)
                                      + PerPersonSarFor(MedineQuadTxt, MedineDaysCombo, 4)
                                      + PerPersonSarFor(MedineQuintTxt, MedineDaysCombo, 5);

                decimal walkMekkePer = PerPersonSarFor(MekkeSingleTxt_Walk, MekkeDaysCombo_Walk, 1)
                                      + PerPersonSarFor(MekkeDblTxt_Walk, MekkeDaysCombo_Walk, 2)
                                      + PerPersonSarFor(MekkeTrplTxt_Walk, MekkeDaysCombo_Walk, 3)
                                      + PerPersonSarFor(MekkeQuadTxt_Walk, MekkeDaysCombo_Walk, 4)
                                      + PerPersonSarFor(MekkeQuintTxt_Walk, MekkeDaysCombo_Walk, 5);

                decimal walkMedinePer = PerPersonSarFor(MedineSingleTxt_Walk, MedineDaysCombo_Walk, 1)
                                      + PerPersonSarFor(MedineDblTxt_Walk, MedineDaysCombo_Walk, 2)
                                      + PerPersonSarFor(MedineTrplTxt_Walk, MedineDaysCombo_Walk, 3)
                                      + PerPersonSarFor(MedineQuadTxt_Walk, MedineDaysCombo_Walk, 4)
                                      + PerPersonSarFor(MedineQuintTxt_Walk, MedineDaysCombo_Walk, 5);

                // convert per-person SAR to USD for display
                var econMekkePerUsd = SarToUsdDecimal(econMekkePer);
                var econMedinePerUsd = SarToUsdDecimal(econMedinePer);
                var walkMekkePerUsd = SarToUsdDecimal(walkMekkePer);
                var walkMedinePerUsd = SarToUsdDecimal(walkMedinePer);

                // display per-person values in Step1
                EconMekkeSar.Text = econMekkePer > 0 ? econMekkePer.ToString("N2", CultureInfo.CurrentCulture) + " SAR" : "-";
                EconMekkeUsd.Text = econMekkePerUsd > 0m ? econMekkePerUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";

                EconMedineSar.Text = econMedinePer > 0 ? econMedinePer.ToString("N2", CultureInfo.CurrentCulture) + " SAR" : "-";
                EconMedineUsd.Text = econMedinePerUsd > 0m ? econMedinePerUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";

                WalkMekkeSar.Text = walkMekkePer > 0 ? walkMekkePer.ToString("N2", CultureInfo.CurrentCulture) + " SAR" : "-";
                WalkMekkeUsd.Text = walkMekkePerUsd > 0m ? walkMekkePerUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";

                WalkMedineSar.Text = walkMedinePer > 0 ? walkMedinePer.ToString("N2", CultureInfo.CurrentCulture) + " SAR" : "-";
                WalkMedineUsd.Text = walkMedinePerUsd > 0m ? walkMedinePerUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";

                var overallPerPersonSar = econMekkePer + econMedinePer + walkMekkePer + walkMedinePer;
                var overallPerPersonUsd = SarToUsdDecimal(overallPerPersonSar);
                OverallSar.Text = overallPerPersonSar > 0 ? overallPerPersonSar.ToString("N2", CultureInfo.CurrentCulture) + " SAR" : "-";
                OverallUsd.Text = overallPerPersonUsd > 0m ? overallPerPersonUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";

                // Also compute per-room base per-person USD values for RoomCost* fields
                var singlePerPersonSar = PerPersonSarFor(MekkeSingleTxt, MekkeDaysCombo, 1)
                                      + PerPersonSarFor(MedineSingleTxt, MedineDaysCombo, 1)
                                      + PerPersonSarFor(MekkeSingleTxt_Walk, MekkeDaysCombo_Walk, 1)
                                      + PerPersonSarFor(MedineSingleTxt_Walk, MedineDaysCombo_Walk, 1);

                var dblPerPersonSar = PerPersonSarFor(MekkeDblTxt, MekkeDaysCombo, 2)
                                      + PerPersonSarFor(MedineDblTxt, MedineDaysCombo, 2)
                                      + PerPersonSarFor(MekkeDblTxt_Walk, MekkeDaysCombo_Walk, 2)
                                      + PerPersonSarFor(MedineDblTxt_Walk, MedineDaysCombo_Walk, 2);

                var trplPerPersonSar = PerPersonSarFor(MekkeTrplTxt, MekkeDaysCombo, 3)
                                       + PerPersonSarFor(MedineTrplTxt, MedineDaysCombo, 3)
                                       + PerPersonSarFor(MekkeTrplTxt_Walk, MekkeDaysCombo_Walk, 3)
                                       + PerPersonSarFor(MedineTrplTxt_Walk, MedineDaysCombo_Walk, 3);

                var quadPerPersonSar = PerPersonSarFor(MekkeQuadTxt, MekkeDaysCombo, 4)
                                       + PerPersonSarFor(MedineQuadTxt, MedineDaysCombo, 4)
                                       + PerPersonSarFor(MekkeQuadTxt_Walk, MekkeDaysCombo_Walk, 4)
                                       + PerPersonSarFor(MedineQuadTxt_Walk, MedineDaysCombo_Walk, 4);

                var quintPerPersonSar = PerPersonSarFor(MekkeQuintTxt, MekkeDaysCombo, 5)
                                        + PerPersonSarFor(MedineQuintTxt, MedineDaysCombo, 5)
                                        + PerPersonSarFor(MekkeQuintTxt_Walk, MekkeDaysCombo_Walk, 5)
                                        + PerPersonSarFor(MedineQuintTxt_Walk, MedineDaysCombo_Walk, 5);

                // combined base (econ + walk) used internally for Step2 calculations
                _perPersonSingleBaseUsd = SarToUsdDecimal(singlePerPersonSar);
                _perPersonDblBaseUsd = SarToUsdDecimal(dblPerPersonSar);
                _perPersonTrplBaseUsd = SarToUsdDecimal(trplPerPersonSar);
                _perPersonQuadBaseUsd = SarToUsdDecimal(quadPerPersonSar);
                _perPersonQuintBaseUsd = SarToUsdDecimal(quintPerPersonSar);

                // also compute per-category (Ekonomik vs Walk) per-room values for UI
                var econSinglePerPersonSar = PerPersonSarFor(MekkeSingleTxt, MekkeDaysCombo, 1) + PerPersonSarFor(MedineSingleTxt, MedineDaysCombo, 1);
                var econDblPerPersonSar = PerPersonSarFor(MekkeDblTxt, MekkeDaysCombo, 2) + PerPersonSarFor(MedineDblTxt, MedineDaysCombo, 2);
                var econTrplPerPersonSar = PerPersonSarFor(MekkeTrplTxt, MekkeDaysCombo, 3) + PerPersonSarFor(MedineTrplTxt, MedineDaysCombo, 3);
                var econQuadPerPersonSar = PerPersonSarFor(MekkeQuadTxt, MekkeDaysCombo, 4) + PerPersonSarFor(MedineQuadTxt, MedineDaysCombo, 4);
                var econQuintPerPersonSar = PerPersonSarFor(MekkeQuintTxt, MekkeDaysCombo, 5) + PerPersonSarFor(MedineQuintTxt, MedineDaysCombo, 5);

                var walkSinglePerPersonSar = PerPersonSarFor(MekkeSingleTxt_Walk, MekkeDaysCombo_Walk, 1) + PerPersonSarFor(MedineSingleTxt_Walk, MedineDaysCombo_Walk, 1);
                var walkDblPerPersonSar = PerPersonSarFor(MekkeDblTxt_Walk, MekkeDaysCombo_Walk, 2) + PerPersonSarFor(MedineDblTxt_Walk, MedineDaysCombo_Walk, 2);
                var walkTrplPerPersonSar = PerPersonSarFor(MekkeTrplTxt_Walk, MekkeDaysCombo_Walk, 3) + PerPersonSarFor(MedineTrplTxt_Walk, MedineDaysCombo_Walk, 3);
                var walkQuadPerPersonSar = PerPersonSarFor(MekkeQuadTxt_Walk, MekkeDaysCombo_Walk, 4) + PerPersonSarFor(MedineQuadTxt_Walk, MedineDaysCombo_Walk, 4);
                var walkQuintPerPersonSar = PerPersonSarFor(MekkeQuintTxt_Walk, MekkeDaysCombo_Walk, 5) + PerPersonSarFor(MedineQuintTxt_Walk, MedineDaysCombo_Walk, 5);

                // convert to USD
                var econSingleUsd = SarToUsdDecimal(econSinglePerPersonSar);
                var econDblUsd = SarToUsdDecimal(econDblPerPersonSar);
                var econTrplUsd = SarToUsdDecimal(econTrplPerPersonSar);
                var econQuadUsd = SarToUsdDecimal(econQuadPerPersonSar);
                var econQuintUsd = SarToUsdDecimal(econQuintPerPersonSar);

                var walkSingleUsd = SarToUsdDecimal(walkSinglePerPersonSar);
                var walkDblUsd = SarToUsdDecimal(walkDblPerPersonSar);
                var walkTrplUsd = SarToUsdDecimal(walkTrplPerPersonSar);
                var walkQuadUsd = SarToUsdDecimal(walkQuadPerPersonSar);
                var walkQuintUsd = SarToUsdDecimal(walkQuintPerPersonSar);

                // persist these computed per-category USD values so UpdateStep2 can use them reliably
                _econSingleUsd = econSingleUsd;
                _econDblUsd = econDblUsd;
                _econTrplUsd = econTrplUsd;
                _econQuadUsd = econQuadUsd;
                _econQuintUsd = econQuintUsd;
                _walkSingleUsd = walkSingleUsd;
                _walkDblUsd = walkDblUsd;
                _walkTrplUsd = walkTrplUsd;
                _walkQuadUsd = walkQuadUsd;
                _walkQuintUsd = walkQuintUsd;

                // update combined base UI (kept for backward compat)
                var rcSingle2 = FindName("RoomCostSingle") as TextBlock;
                if (rcSingle2 != null) rcSingle2.Text = _perPersonSingleBaseUsd > 0m ? _perPersonSingleBaseUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";
                var rcDbl2 = FindName("RoomCostDbl") as TextBlock;
                if (rcDbl2 != null) rcDbl2.Text = _perPersonDblBaseUsd > 0m ? _perPersonDblBaseUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";
                var rcTrpl2 = FindName("RoomCostTrpl") as TextBlock;
                if (rcTrpl2 != null) rcTrpl2.Text = _perPersonTrplBaseUsd > 0m ? _perPersonTrplBaseUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";
                var rcQuad2 = FindName("RoomCostQuad") as TextBlock;
                if (rcQuad2 != null) rcQuad2.Text = _perPersonQuadBaseUsd > 0m ? _perPersonQuadBaseUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";
                var rcQuint2 = FindName("RoomCostQuint") as TextBlock;
                if (rcQuint2 != null) rcQuint2.Text = _perPersonQuintBaseUsd > 0m ? _perPersonQuintBaseUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";

                // update EKONOMİK / LÜKS displays (use FindName to avoid generated field dependency)
                try
                {
                    var eSingle = FindName("EconRoomSingle") as TextBlock;
                    var eDbl = FindName("EconRoomDbl") as TextBlock;
                    var eTrpl = FindName("EconRoomTrpl") as TextBlock;
                    var eQuad = FindName("EconRoomQuad") as TextBlock;
                    var eQuint = FindName("EconRoomQuint") as TextBlock;

                    var wSingle = FindName("WalkRoomSingle") as TextBlock;
                    var wDbl = FindName("WalkRoomDbl") as TextBlock;
                    var wTrpl = FindName("WalkRoomTrpl") as TextBlock;
                    var wQuad = FindName("WalkRoomQuad") as TextBlock;
                    var wQuint = FindName("WalkRoomQuint") as TextBlock;

                    if (eSingle != null)
                    {
                        eSingle.Text = econSingleUsd > 0m ? econSingleUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";
                    }
                    if (eDbl != null)
                    {
                        eDbl.Text = econDblUsd > 0m ? econDblUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";
                    }
                    if (eTrpl != null)
                    {
                        eTrpl.Text = econTrplUsd > 0m ? econTrplUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";
                    }
                    if (eQuad != null)
                    {
                        eQuad.Text = econQuadUsd > 0m ? econQuadUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";
                    }
                    if (eQuint != null)
                    {
                        eQuint.Text = econQuintUsd > 0m ? econQuintUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";
                    }

                    if (wSingle != null)
                    {
                        wSingle.Text = walkSingleUsd > 0m ? walkSingleUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";
                    }
                    if (wDbl != null)
                    {
                        wDbl.Text = walkDblUsd > 0m ? walkDblUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";
                    }
                    if (wTrpl != null)
                    {
                        wTrpl.Text = walkTrplUsd > 0m ? walkTrplUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";
                    }
                    if (wQuad != null)
                    {
                        wQuad.Text = walkQuadUsd > 0m ? walkQuadUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";
                    }
                    if (wQuint != null)
                    {
                        wQuint.Text = walkQuintUsd > 0m ? walkQuintUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";
                    }
                }
                catch { }

                // per-room base USD values already computed above and assigned to _perPerson*BaseUsd
                // ensure RoomCost fields reflect base per-person USD (if present in XAML)
                var rcSingle3 = FindName("RoomCostSingle") as TextBlock;
                if (rcSingle3 != null) rcSingle3.Text = _perPersonSingleBaseUsd > 0m ? _perPersonSingleBaseUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";
                var rcDbl3 = FindName("RoomCostDbl") as TextBlock;
                if (rcDbl3 != null) rcDbl3.Text = _perPersonDblBaseUsd > 0m ? _perPersonDblBaseUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";
                var rcTrpl3 = FindName("RoomCostTrpl") as TextBlock;
                if (rcTrpl3 != null) rcTrpl3.Text = _perPersonTrplBaseUsd > 0m ? _perPersonTrplBaseUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";
                var rcQuad3 = FindName("RoomCostQuad") as TextBlock;
                if (rcQuad3 != null) rcQuad3.Text = _perPersonQuadBaseUsd > 0m ? _perPersonQuadBaseUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";
                var rcQuint3 = FindName("RoomCostQuint") as TextBlock;
                if (rcQuint3 != null) rcQuint3.Text = _perPersonQuintBaseUsd > 0m ? _perPersonQuintBaseUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";

                // Show per-person SAR and USD in Step1
                EconMekkeSar.Text = econMekkePer > 0 ? econMekkePer.ToString("N2", CultureInfo.CurrentCulture) + " SAR" : "-";
                EconMekkeUsd.Text = econMekkePerUsd > 0m ? econMekkePerUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";

                EconMedineSar.Text = econMedinePer > 0 ? econMedinePer.ToString("N2", CultureInfo.CurrentCulture) + " SAR" : "-";
                EconMedineUsd.Text = econMedinePerUsd > 0m ? econMedinePerUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";

                WalkMekkeSar.Text = walkMekkePer > 0 ? walkMekkePer.ToString("N2", CultureInfo.CurrentCulture) + " SAR" : "-";
                WalkMekkeUsd.Text = walkMekkePerUsd > 0m ? walkMekkePerUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";

                WalkMedineSar.Text = walkMedinePer > 0 ? walkMedinePer.ToString("N2", CultureInfo.CurrentCulture) + " SAR" : "-";
                WalkMedineUsd.Text = walkMedinePerUsd > 0m ? walkMedinePerUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";

                OverallSar.Text = overallPerPersonSar > 0 ? overallPerPersonSar.ToString("N2", CultureInfo.CurrentCulture) + " SAR" : "-";
                OverallUsd.Text = overallPerPersonUsd > 0m ? overallPerPersonUsd.ToString("N2", CultureInfo.CurrentCulture) + " $" : "-";

                // Whenever Step1 totals recalculated, update Step2 displays too
                try { UpdateProfitDisplayPerInputs(); } catch { }
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

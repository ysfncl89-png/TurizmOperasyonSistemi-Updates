using System;
using System.Windows.Controls;
using System.Windows;
using System.Linq;
using System.Collections.Generic;
using TurizmOperasyonSistemi.Views;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Net.Http;
using System.Xml.Linq;
using System.Globalization;
// TCMB exchange rate widget moved here

namespace TurizmOperasyonSistemi.Views
{
    public partial class PaymentsView : UserControl
    {
        // Exchange rate timer and client
        private readonly DispatcherTimer _ratesTimer;
        private static readonly HttpClient _httpClient = new HttpClient();
        private decimal _usdRate = 0m;
        private decimal _eurRate = 0m;
        private decimal _gbpRate = 0m;
        private decimal _sarRate = 0m;

        public PaymentsView()
        {
            InitializeComponent();
            Loaded += PaymentsView_Loaded;

            // Start periodic rate fetch
            _ratesTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(30)
            };
            _ratesTimer.Tick += async (s, e) => await FetchExchangeRatesAsync();
            _ratesTimer.Start();

            // Initial fetch (fire-and-forget)
            _ = FetchExchangeRatesAsync();
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

        private async Task FetchExchangeRatesAsync()
        {
            try
            {
                const string url = "https://www.tcmb.gov.tr/kurlar/today.xml";
                string xml = await _httpClient.GetStringAsync(url).ConfigureAwait(false);

                var doc = XDocument.Parse(xml);

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
                var usdSell = GetRate("USD", "ForexSelling");
                var eurBuy = GetRate("EUR", "ForexBuying");
                var eurSell = GetRate("EUR", "ForexSelling");
                var gbpBuy = GetRate("GBP", "ForexBuying");
                var gbpSell = GetRate("GBP", "ForexSelling");
                var sarBuy = GetRate("SAR", "ForexBuying");
                var sarSell = GetRate("SAR", "ForexSelling");

                _usdRate = TryParseRate(usdBuy);
                _eurRate = TryParseRate(eurBuy);
                _gbpRate = TryParseRate(gbpBuy);
                _sarRate = TryParseRate(sarBuy);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var usdBuyTb = this.FindName("UsdBuyText") as TextBlock;
                        var usdSellTb = this.FindName("UsdSellText") as TextBlock;
                        var eurBuyTb = this.FindName("EurBuyText") as TextBlock;
                        var eurSellTb = this.FindName("EurSellText") as TextBlock;
                        var gbpBuyTb = this.FindName("GbpBuyText") as TextBlock;
                        var gbpSellTb = this.FindName("GbpSellText") as TextBlock;
                        var sarBuyTb = this.FindName("SarBuyText") as TextBlock;
                        var sarSellTb = this.FindName("SarSellText") as TextBlock;

                        if (usdBuyTb != null) usdBuyTb.Text = usdBuy;
                        if (usdSellTb != null) usdSellTb.Text = usdSell;
                        if (eurBuyTb != null) eurBuyTb.Text = eurBuy;
                        if (eurSellTb != null) eurSellTb.Text = eurSell;
                        if (gbpBuyTb != null) gbpBuyTb.Text = gbpBuy;
                        if (gbpSellTb != null) gbpSellTb.Text = gbpSell;
                        if (sarBuyTb != null) sarBuyTb.Text = sarBuy;
                        if (sarSellTb != null) sarSellTb.Text = sarSell;

                        // footer widget set (if present)
                        var usdBuyF = this.FindName("UsdBuyFooter") as TextBlock;
                        var usdSellF = this.FindName("UsdSellFooter") as TextBlock;
                        var eurBuyF = this.FindName("EurBuyFooter") as TextBlock;
                        var eurSellF = this.FindName("EurSellFooter") as TextBlock;
                        var gbpBuyF = this.FindName("GbpBuyFooter") as TextBlock;
                        var gbpSellF = this.FindName("GbpSellFooter") as TextBlock;
                        var sarBuyF = this.FindName("SarBuyFooter") as TextBlock;
                        var sarSellF = this.FindName("SarSellFooter") as TextBlock;

                        if (usdBuyF != null) usdBuyF.Text = usdBuy;
                        if (usdSellF != null) usdSellF.Text = usdSell;
                        if (eurBuyF != null) eurBuyF.Text = eurBuy;
                        if (eurSellF != null) eurSellF.Text = eurSell;
                        if (gbpBuyF != null) gbpBuyF.Text = gbpBuy;
                        if (gbpSellF != null) gbpSellF.Text = gbpSell;
                        if (sarBuyF != null) sarBuyF.Text = sarBuy;
                        if (sarSellF != null) sarSellF.Text = sarSell;

                        // footer widget set (if present)
                        var usdBuyFooter = this.FindName("UsdBuyFooter") as TextBlock;
                        var usdSellFooter = this.FindName("UsdSellFooter") as TextBlock;
                        var eurBuyFooter = this.FindName("EurBuyFooter") as TextBlock;
                        var eurSellFooter = this.FindName("EurSellFooter") as TextBlock;
                        var gbpBuyFooter = this.FindName("GbpBuyFooter") as TextBlock;
                        var gbpSellFooter = this.FindName("GbpSellFooter") as TextBlock;
                        var sarBuyFooter = this.FindName("SarBuyFooter") as TextBlock;
                        var sarSellFooter = this.FindName("SarSellFooter") as TextBlock;

                        if (usdBuyFooter != null) usdBuyFooter.Text = usdBuy;
                        if (usdSellFooter != null) usdSellFooter.Text = usdSell;
                        if (eurBuyFooter != null) eurBuyFooter.Text = eurBuy;
                        if (eurSellFooter != null) eurSellFooter.Text = eurSell;
                        if (gbpBuyFooter != null) gbpBuyFooter.Text = gbpBuy;
                        if (gbpSellFooter != null) gbpSellFooter.Text = gbpSell;
                        if (sarBuyFooter != null) sarBuyFooter.Text = sarBuy;
                        if (sarSellFooter != null) sarSellFooter.Text = sarSell;
                    }
                    catch { }
                });
            }
            catch
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        foreach (var name in new[] { "UsdBuyText", "UsdSellText", "EurBuyText", "EurSellText", "GbpBuyText", "GbpSellText", "SarBuyText", "SarSellText" })
                        {
                            var tb = this.FindName(name) as TextBlock;
                            if (tb != null) tb.Text = "-";
                        }
                    }
                    catch { }
                });
            }
        }

        private void PopulateFilters()
        {
            try
            {
                using var db = new TurizmOperasyonSistemi.Data.AppDbContext();
                var regs = db.Registrations.Where(r => r.IsActive).ToList();

                var tourTypes = regs.Select(r => string.IsNullOrWhiteSpace(r.TourType) ? "(Belirtilmemiş)" : r.TourType).Distinct().OrderBy(x => x).ToList();
                var periods = regs.Select(r => string.IsNullOrWhiteSpace(r.Period) ? "(Belirtilmemiş)" : r.Period).Distinct().OrderBy(x => x).ToList();
                var groupTypes = regs.Select(r => string.IsNullOrWhiteSpace(r.GroupType) ? "(Belirtilmemiş)" : r.GroupType).Distinct().OrderBy(x => x).ToList();
                var dayCounts = regs.Select(r => string.IsNullOrWhiteSpace(r.DaysCount) ? "-" : r.DaysCount).Distinct().OrderBy(x => x).ToList();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        CbTourType.Items.Clear();
                        CbTourType.Items.Add("(Tümü)");
                        foreach (var t in tourTypes) CbTourType.Items.Add(t);
                        CbTourType.SelectedIndex = 0;

                        CbPeriod.Items.Clear();
                        CbPeriod.Items.Add("(Tümü)");
                        foreach (var p in periods) CbPeriod.Items.Add(p);
                        CbPeriod.SelectedIndex = 0;

                        CbGroupType.Items.Clear();
                        CbGroupType.Items.Add("(Tümü)");
                        foreach (var g in groupTypes) CbGroupType.Items.Add(g);
                        CbGroupType.SelectedIndex = 0;

                        CbDayCount.Items.Clear();
                        CbDayCount.Items.Add("(Tümü)");
                        foreach (var d in dayCounts) CbDayCount.Items.Add(d);
                        CbDayCount.SelectedIndex = 0;
                    }
                    catch { }
                });
            }
            catch { }
        }

        private void Filter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try { Task.Run(() => RefreshFinanceSummary()); } catch { }
        }

        private void RefreshFinanceSummary()
        {
            try
            {
                int totalAccounts = 0;
                decimal totalCollected = 0m;
                decimal totalRemaining = 0m;
                string collectedDisplay = null;
                string remainingDisplay = null;
                int incompleteCount = 0;
                int overpaidCount = 0;

                try
                {
                    using var db = new TurizmOperasyonSistemi.Data.AppDbContext();
                    var query = db.Registrations.AsQueryable().Where(r => r.IsActive);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            var t = CbTourType.SelectedItem as string;
                            if (!string.IsNullOrEmpty(t) && t != "(Tümü)") query = query.Where(r => (r.TourType ?? "") == t);
                            var p = CbPeriod.SelectedItem as string;
                            if (!string.IsNullOrEmpty(p) && p != "(Tümü)") query = query.Where(r => (r.Period ?? "") == p);
                            var g = CbGroupType.SelectedItem as string;
                            if (!string.IsNullOrEmpty(g) && g != "(Tümü)") query = query.Where(r => (r.GroupType ?? "") == g);
                            var d = CbDayCount.SelectedItem as string;
                            if (!string.IsNullOrEmpty(d) && d != "(Tümü)")
                            {
                                query = query.Where(r => (r.DaysCount ?? "") == d);
                            }
                        }
                        catch { }
                    });

                    var regs = query.ToList();

                    totalAccounts = regs.Select(r => r.PersonId).Distinct().Count();

                    var ledgerPerPerson = regs.GroupBy(r => r.PersonId)
                                              .Select(g => new
                                              {
                                                  PersonId = g.Key,
                                                  Total = g.Sum(x => (decimal?)x.TotalPrice) ?? 0m,
                                                  Paid = g.Sum(x => (decimal?)x.PaidAmount) ?? 0m,
                                                  Remaining = g.Sum(x => (decimal?)x.RemainingDebt) ?? 0m,
                                                  Currency = g.FirstOrDefault()?.Currency ?? string.Empty
                                              }).ToList();

                    var totalsByCurrency = ledgerPerPerson.GroupBy(p => string.IsNullOrWhiteSpace(p.Currency) ? "" : p.Currency)
                                                          .Select(g => new
                                                          {
                                                              Currency = g.Key,
                                                              Collected = g.Sum(x => x.Paid),
                                                              Remaining = g.Sum(x => x.Remaining)
                                                          }).ToList();

                    totalCollected = totalsByCurrency.Sum(x => x.Collected);
                    totalRemaining = totalsByCurrency.Sum(x => x.Remaining);

                    string FormatAmount(decimal amount, string currency)
                    {
                        if (string.IsNullOrWhiteSpace(currency))
                            return amount.ToString("N0");
                        var c = currency.Trim().ToUpperInvariant();
                        if (c == "TRY" || c == "TL" || c == "₺")
                            return $"₺{amount:N0}";
                        if (c == "USD" || c == "$")
                            return $"${amount:N0}";
                        if (c == "EUR" || c == "€")
                            return $"€{amount:N0}";
                        return $"{amount:N0} {currency}";
                    }

                    var collectedParts = totalsByCurrency.Select(t => FormatAmount(t.Collected, t.Currency)).ToList();
                    var remainingParts = totalsByCurrency.Select(t => FormatAmount(t.Remaining, t.Currency)).ToList();

                    collectedDisplay = collectedParts.Any() ? string.Join(" — ", collectedParts) : FormatAmount(0, string.Empty);
                    remainingDisplay = remainingParts.Any() ? string.Join(" — ", remainingParts) : FormatAmount(0, string.Empty);

                    // Kişi bazında kalan borcu topladıktan sonra, kalan > 0 olan kişi sayısını al
                    incompleteCount = ledgerPerPerson.Count(p => p.Remaining > 0m);
                    // Fazla ödeme yapmış (remaining < 0) kişi sayısı
                    overpaidCount = ledgerPerPerson.Count(p => p.Remaining < 0m);
                }
                catch { }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        TotalAccountsTextBlock.Text = $"Cari Hesap: {totalAccounts}";
                        TotalCollectedTextBlock.Text = $"Toplam Tahsilat: {(string.IsNullOrEmpty(collectedDisplay) ? totalCollected.ToString("C0") : collectedDisplay)}";
                        TotalRemainingTextBlock.Text = $"Toplam Kalan: {(string.IsNullOrEmpty(remainingDisplay) ? totalRemaining.ToString("C0") : remainingDisplay)}";
                        IncompleteCountTextBlock.Text = incompleteCount.ToString();
                        try { OverpaidCountTextBlock.Text = overpaidCount.ToString(); } catch { }

                        // Güncelle: progress bar'ları ve yüzde metinlerini ayarla
                        try
                        {
                            var collectedPb = this.FindName("CollectedProgressBar_Payments") as ProgressBar;
                            var remainingPb = this.FindName("RemainingProgressBar_Payments") as ProgressBar;
                            var collectedPctTb = this.FindName("CollectedPercentageText_Payments") as TextBlock;
                            var remainingPctTb = this.FindName("RemainingPercentageText_Payments") as TextBlock;

                            if (collectedPb != null && remainingPb != null)
                            {
                                double collectedPercentValue = 0, remainingPercentValue = 0;
                                decimal grandTotal = totalCollected + totalRemaining;
                                if (grandTotal > 0)
                                {
                                    collectedPercentValue = (double)((totalCollected / grandTotal) * 100m);
                                    remainingPercentValue = (double)((totalRemaining / grandTotal) * 100m);
                                }

                                collectedPb.Value = collectedPercentValue;
                                remainingPb.Value = remainingPercentValue;

                                if (collectedPctTb != null) collectedPctTb.Text = $"{Math.Round(collectedPercentValue, 1)}%";
                                if (remainingPctTb != null) remainingPctTb.Text = $"{Math.Round(remainingPercentValue, 1)}%";
                            }
                        }
                        catch { }

                        try
                        {
                            using var db = new TurizmOperasyonSistemi.Data.AppDbContext();
                            var totalTx = db.Registrations.Count();
                            var activeCurrency = "TRY";
                            try
                            {
                                var m = db.Registrations.GroupBy(r => r.Currency ?? "")
                                                          .Select(g => new { Currency = g.Key, Count = g.Count() })
                                                          .OrderByDescending(x => x.Count)
                                                          .FirstOrDefault();
                                if (m != null && !string.IsNullOrWhiteSpace(m.Currency)) activeCurrency = m.Currency;
                            }
                            catch { }

                            SystemInfoTextBlock.Text = $"Aktif para birimi: {activeCurrency}    Toplam işlem: {totalTx}";
                        }
                        catch { }
                    }
                    catch { }
                });
            }
            catch { }
        }



        private void Ledger_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cu = TurizmOperasyonSistemi.Services.CurrentUser.Instance;
                // Allow if admin or has payments permission
                if (!(cu.IsAdmin || cu.HasPermission(TurizmOperasyonSistemi.Services.PermissionKeys.Payments)))
                {
                    MessageBox.Show("Bu modüle erişim izniniz yok.");
                    return;
                }

                // Get the MainWindow and navigate to LedgerView
                var mw = System.Windows.Application.Current?.MainWindow as MainWindow;
                if (mw != null)
                {
                    mw.NavigateTo(new LedgerView());
                    return;
                }

                // Fallback: Try to find the window hosting this control
                var host = Window.GetWindow(this);
                if (host is MainWindow mwHost)
                {
                    mwHost.NavigateTo(new LedgerView());
                    return;
                }

                MessageBox.Show("Ana pencere bulunamadı. Lütfen uygulamayı yeniden başlatın.");
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Cari işlemlere geçiş sırasında hata: {ex.Message}");
            }
        }

        private void IncompleteCount_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                // Compute personIds for current filters to ensure exact match
                using var db = new TurizmOperasyonSistemi.Data.AppDbContext();
                var query = db.Registrations.AsQueryable().Where(r => r.IsActive);
                var tour = CbTourType.SelectedItem == null ? null : CbTourType.SelectedItem.ToString();
                var period = CbPeriod.SelectedItem == null ? null : CbPeriod.SelectedItem.ToString();
                var group = CbGroupType.SelectedItem == null ? null : CbGroupType.SelectedItem.ToString();
                var days = CbDayCount.SelectedItem == null ? null : CbDayCount.SelectedItem.ToString();
                try
                {
                    if (!string.IsNullOrEmpty(tour) && tour != "(Tümü)")
                    {
                        if (tour == "(Belirtilmemiş)") query = query.Where(r => string.IsNullOrWhiteSpace(r.TourType));
                        else query = query.Where(r => (r.TourType ?? "") == tour);
                    }
                    if (!string.IsNullOrEmpty(period) && period != "(Tümü)")
                    {
                        if (period == "(Belirtilmemiş)") query = query.Where(r => string.IsNullOrWhiteSpace(r.Period));
                        else query = query.Where(r => (r.Period ?? "") == period);
                    }
                    if (!string.IsNullOrEmpty(group) && group != "(Tümü)")
                    {
                        if (group == "(Belirtilmemiş)") query = query.Where(r => string.IsNullOrWhiteSpace(r.GroupType));
                        else query = query.Where(r => (r.GroupType ?? "") == group);
                    }
                    if (!string.IsNullOrEmpty(days) && days != "(Tümü)")
                    {
                        if (days == "-") query = query.Where(r => string.IsNullOrWhiteSpace(r.DaysCount));
                        else query = query.Where(r => (r.DaysCount ?? "") == days);
                    }
                }
                catch { }

                var registrationIds = query.Select(r => r.Id).Distinct().ToList();

                var ledgerView = new LedgerView();
                ledgerView.ApplyFilterByRegistrationIds(LedgerView.LedgerFilter.Incomplete, registrationIds);

                // Try to navigate via MainWindow, fallback to host window, and show error if not found
                var mw = System.Windows.Application.Current?.MainWindow as MainWindow;
                if (mw != null)
                {
                    mw.NavigateTo(ledgerView);
                    return;
                }

                var host = Window.GetWindow(this);
                if (host is MainWindow hostMw)
                {
                    hostMw.NavigateTo(ledgerView);
                    return;
                }

                MessageBox.Show("Ana pencere bulunamadı. Lütfen uygulamayı yeniden başlatın.", "Bildirim", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OverpaidCount_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                // Compute registrationIds for current filters so ledger groups only include filtered registrations
                using var db = new TurizmOperasyonSistemi.Data.AppDbContext();
                var query = db.Registrations.AsQueryable().Where(r => r.IsActive);
                var tour = CbTourType.SelectedItem == null ? null : CbTourType.SelectedItem.ToString();
                var period = CbPeriod.SelectedItem == null ? null : CbPeriod.SelectedItem.ToString();
                var group = CbGroupType.SelectedItem == null ? null : CbGroupType.SelectedItem.ToString();
                var days = CbDayCount.SelectedItem == null ? null : CbDayCount.SelectedItem.ToString();
                try
                {
                    if (!string.IsNullOrEmpty(tour) && tour != "(Tümü)")
                    {
                        if (tour == "(Belirtilmemiş)") query = query.Where(r => string.IsNullOrWhiteSpace(r.TourType));
                        else query = query.Where(r => (r.TourType ?? "") == tour);
                    }
                    if (!string.IsNullOrEmpty(period) && period != "(Tümü)")
                    {
                        if (period == "(Belirtilmemiş)") query = query.Where(r => string.IsNullOrWhiteSpace(r.Period));
                        else query = query.Where(r => (r.Period ?? "") == period);
                    }
                    if (!string.IsNullOrEmpty(group) && group != "(Tümü)")
                    {
                        if (group == "(Belirtilmemiş)") query = query.Where(r => string.IsNullOrWhiteSpace(r.GroupType));
                        else query = query.Where(r => (r.GroupType ?? "") == group);
                    }
                    if (!string.IsNullOrEmpty(days) && days != "(Tümü)")
                    {
                        if (days == "-") query = query.Where(r => string.IsNullOrWhiteSpace(r.DaysCount));
                        else query = query.Where(r => (r.DaysCount ?? "") == days);
                    }
                }
                catch { }

                var registrationIds = query.Select(r => r.Id).Distinct().ToList();

                var ledgerView = new LedgerView();
                ledgerView.ApplyFilterByRegistrationIds(LedgerView.LedgerFilter.Overpaid, registrationIds);

                // Try to navigate via MainWindow, fallback to host window, and show error if not found
                var mw = System.Windows.Application.Current?.MainWindow as MainWindow;
                if (mw != null)
                {
                    mw.NavigateTo(ledgerView);
                    return;
                }

                var host = Window.GetWindow(this);
                if (host is MainWindow hostMw)
                {
                    hostMw.NavigateTo(ledgerView);
                    return;
                }

                MessageBox.Show("Ana pencere bulunamadı. Lütfen uygulamayı yeniden başlatın.", "Bildirim", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void PaymentsView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await Task.Run(() => PopulateFilters());
                await Task.Run(() => RefreshFinanceSummary());
            }
            catch { }
        }

        private void LoadFinanceSummary()
        {
            try
            {
                var summaries = new List<string>();
                int totalAccounts = 0;
                decimal totalCollected = 0m;
                decimal totalRemaining = 0m;
                string collectedDisplay = null;
                string remainingDisplay = null;
                try
                {
                    using var db = new TurizmOperasyonSistemi.Data.AppDbContext();
                    // Load active registrations
                    var regs = db.Registrations.Where(r => r.IsActive).ToList();

                    // Count distinct person-ledger accounts (ledger-like aggregation)
                    totalAccounts = regs.Select(r => r.PersonId).Distinct().Count();

                    // Aggregate per-person (ledger) totals so we don't double-count across multiple registrations
                    var ledgerPerPerson = regs.GroupBy(r => r.PersonId)
                                              .Select(g => new
                                              {
                                                  PersonId = g.Key,
                                                  Total = g.Sum(x => (decimal?)x.TotalPrice) ?? 0m,
                                                  Paid = g.Sum(x => (decimal?)x.PaidAmount) ?? 0m,
                                                  Remaining = g.Sum(x => (decimal?)x.RemainingDebt) ?? 0m,
                                                  Currency = g.FirstOrDefault()?.Currency ?? string.Empty
                                              }).ToList();

                    // Sum by currency so we don't mix USD with TRY when formatting
                    var totalsByCurrency = ledgerPerPerson.GroupBy(p => string.IsNullOrWhiteSpace(p.Currency) ? "" : p.Currency)
                                                          .Select(g => new
                                                          {
                                                              Currency = g.Key,
                                                              Collected = g.Sum(x => x.Paid),
                                                              Remaining = g.Sum(x => x.Remaining)
                                                          }).ToList();

                    totalCollected = totalsByCurrency.Sum(x => x.Collected);
                    totalRemaining = totalsByCurrency.Sum(x => x.Remaining);

                    string FormatAmount(decimal amount, string currency)
                    {
                        if (string.IsNullOrWhiteSpace(currency))
                            return amount.ToString("N0");
                        var c = currency.Trim().ToUpperInvariant();
                        if (c == "TRY" || c == "TL" || c == "₺")
                            return $"₺{amount:N0}";
                        if (c == "USD" || c == "$")
                            return $"${amount:N0}";
                        if (c == "EUR" || c == "€")
                            return $"€{amount:N0}";
                        // Fallback: append currency code
                        return $"{amount:N0} {currency}";
                    }

                    // Prepare display strings per currency
                    var collectedParts = totalsByCurrency.Select(t => FormatAmount(t.Collected, t.Currency)).ToList();
                    var remainingParts = totalsByCurrency.Select(t => FormatAmount(t.Remaining, t.Currency)).ToList();

                    collectedDisplay = collectedParts.Any() ? string.Join(" — ", collectedParts) : FormatAmount(0, string.Empty);
                    remainingDisplay = remainingParts.Any() ? string.Join(" — ", remainingParts) : FormatAmount(0, string.Empty);

                    // Per-tour summary: show currency-aware sums per tour (grouped by tour then currency)
                    var perTour = regs.GroupBy(r => string.IsNullOrWhiteSpace(r.TourType) ? "(Belirtilmemiş)" : r.TourType)
                                      .Select(g => new
                                      {
                                          Tour = g.Key,
                                          ByCurrency = g.GroupBy(x => string.IsNullOrWhiteSpace(x.Currency) ? "" : x.Currency)
                                                        .Select(cg => new { Currency = cg.Key, Collected = cg.Sum(x => (decimal?)x.PaidAmount) ?? 0m, Remaining = cg.Sum(x => (decimal?)x.RemainingDebt) ?? 0m })
                                                        .ToList(),
                                          TotalCount = g.Count(),
                                          FullyPaid = g.Count(x => x.RemainingDebt <= 0),
                                          Pending = g.Count(x => x.RemainingDebt > 0)
                                      }).OrderByDescending(x => x.TotalCount).ToList();

                    foreach (var p in perTour)
                    {
                        var currencyParts = p.ByCurrency.Select(b => $"{FormatAmount(b.Collected, b.Currency)} tahsilat / {FormatAmount(b.Remaining, b.Currency)} kalan{(string.IsNullOrWhiteSpace(b.Currency) ? "" : $" ({b.Currency})")}");
                        var currencySummary = string.Join("; ", currencyParts);
                        var line = $"{p.Tour}: {currencySummary} — Tamam {p.FullyPaid}/{p.TotalCount} — Bekleyen {p.Pending}";
                        summaries.Add(line);
                    }
                }
                catch { }

                // marshal back to UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        TotalAccountsTextBlock.Text = $"Cari Hesap: {totalAccounts}";
                        TotalCollectedTextBlock.Text = $"Toplam Tahsilat: {(string.IsNullOrEmpty(collectedDisplay) ? totalCollected.ToString("C0") : collectedDisplay)}";
                        TotalRemainingTextBlock.Text = $"Toplam Kalan: {(string.IsNullOrEmpty(remainingDisplay) ? totalRemaining.ToString("C0") : remainingDisplay)}";
                        // Tour summary removed per user request

                        // Update basic system info at bottom: active currency and total transactions
                        try
                        {
                            using var db = new TurizmOperasyonSistemi.Data.AppDbContext();
                            var totalTx = db.Registrations.Count();
                            var activeCurrency = "TRY";
                            try
                            {
                                var m = db.Registrations.GroupBy(r => r.Currency ?? "")
                                                          .Select(g => new { Currency = g.Key, Count = g.Count() })
                                                          .OrderByDescending(x => x.Count)
                                                          .FirstOrDefault();
                                if (m != null && !string.IsNullOrWhiteSpace(m.Currency)) activeCurrency = m.Currency;
                            }
                            catch { }

                            SystemInfoTextBlock.Text = $"Aktif para birimi: {activeCurrency}    Toplam işlem: {totalTx}";
                        }
                        catch { }
                    }
                    catch { }
                });
            }
            catch { }
        }

        // LoadExchangeRatesAsync removed along with TCMB widget

        private void PaymentList_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var personsView = new PersonsGeneralView();
                // Ensure PaymentsListCardMode is set before configuring mode so UI overrides (combobox color, labels)
                try { personsView.PaymentsListCardMode = true; } catch { }
                try { personsView.ConfigureMode(PersonsGeneralView.ViewMode.Genel); } catch { }

                // If hosted in a NavigationService-capable host, navigate there
                var nav = System.Windows.Navigation.NavigationService.GetNavigationService(this);
                if (nav != null)
                {
                    nav.Navigate(personsView);
                    return;
                }

                // Otherwise open a new window that hosts the page inside a Frame
                var win = new Window
                {
                    Title = "Tur Kayıtları - Genel (Ödeme Listesi)",
                    Content = new Frame { Content = personsView, NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden },
                    Width = 1100,
                    Height = 700,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Application.Current?.MainWindow
                };
                win.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TourProfitability_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var view = new TourProfitabilityAnalysisWindow();

                var mw = System.Windows.Application.Current?.MainWindow as MainWindow;
                if (mw != null)
                {
                    mw.NavigateTo(view);
                    return;
                }

                var host = Window.GetWindow(this);
                if (host is MainWindow hostMw)
                {
                    hostMw.NavigateTo(view);
                    return;
                }

                MessageBox.Show("Ana pencere bulunamadı. Lütfen uygulamayı yeniden başlatın.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

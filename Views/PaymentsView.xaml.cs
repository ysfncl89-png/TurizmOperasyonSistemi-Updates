using System.Windows.Controls;
using System.Windows;
using System.Linq;
using System.Collections.Generic;
using TurizmOperasyonSistemi.Views;
using System.Threading.Tasks;
using TurizmOperasyonSistemi.Services;

namespace TurizmOperasyonSistemi.Views
{
    public partial class PaymentsView : UserControl
    {
        private readonly ExchangeRateService _exchangeRateService = new ExchangeRateService();
        public PaymentsView()
        {
            InitializeComponent();
            Loaded += PaymentsView_Loaded;
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
            var mw = System.Windows.Application.Current.MainWindow as MainWindow;
            if (mw != null)
            {
                mw.NavigateTo(new LedgerView());
            }
        }

        private void IncompleteCount_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                var ledgerView = new LedgerView();
                ledgerView.ApplyFilter(LedgerView.LedgerFilter.Incomplete);
                var mw = System.Windows.Application.Current.MainWindow as MainWindow;
                if (mw != null) mw.NavigateTo(ledgerView);
            }
            catch { }
        }

        private void OverpaidCount_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                var ledgerView = new LedgerView();
                ledgerView.ApplyFilter(LedgerView.LedgerFilter.Overpaid);
                var mw = System.Windows.Application.Current.MainWindow as MainWindow;
                if (mw != null) mw.NavigateTo(ledgerView);
            }
            catch { }
        }

        private async void PaymentsView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await Task.Run(() => PopulateFilters());
                await Task.Run(() => RefreshFinanceSummary());
                await LoadExchangeRatesAsync();
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

        private async Task LoadExchangeRatesAsync()
        {
            try
            {
                var rates = await _exchangeRateService.GetLatestRatesAsync();
                if (rates != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            UsdText.Text = $"Alış: {rates.UsdBuy}  Satış: {rates.UsdSell}";
                            EurText.Text = $"Alış: {rates.EurBuy}  Satış: {rates.EurSell}";
                            GbpText.Text = $"Alış: {rates.GbpBuy}  Satış: {rates.GbpSell}";
                            SarText.Text = $"Alış: {rates.SarBuy}  Satış: {rates.SarSell}";
                            LastRateUpdateText.Text = $"Son kur güncelleme: {rates.LastUpdated:dd.MM.yyyy HH:mm}";
                        }
                        catch { }
                    });
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(() => LastRateUpdateText.Text = "Son kur güncelleme: -");
                }
            }
            catch { }
        }

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
    }
}
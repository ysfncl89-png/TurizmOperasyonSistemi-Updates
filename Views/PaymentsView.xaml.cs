using System.Windows.Controls;
using System.Windows;
using System.Linq;
using System.Collections.Generic;
using TurizmOperasyonSistemi.Views;

namespace TurizmOperasyonSistemi.Views
{
    public partial class PaymentsView : UserControl
    {
        public PaymentsView()
        {
            InitializeComponent();
            Loaded += PaymentsView_Loaded;
        }



        private void Ledger_Click(object sender, RoutedEventArgs e)
        {
            var mw = System.Windows.Application.Current.MainWindow as MainWindow;
            if (mw != null)
            {
                mw.NavigateTo(new LedgerView());
            }
        }

        private async void PaymentsView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await System.Threading.Tasks.Task.Run(() => LoadFinanceSummary());
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
                    }
                    catch { }
                });
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
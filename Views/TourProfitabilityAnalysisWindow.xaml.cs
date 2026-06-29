using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TurizmOperasyonSistemi.Data;

namespace TurizmOperasyonSistemi.Views
{
    public partial class TourProfitabilityAnalysisWindow : UserControl
    {
        private readonly List<TourOption> _tourOptions = new();
        private List<Models.Registration> _currentRegistrations = new();
        private string _currentPrimaryCurrency = string.Empty;
        private decimal _currentTourTotal;
        private decimal _currentPaidTotal;
        private decimal _currentRemainingTotal;

        public TourProfitabilityAnalysisWindow()
        {
            InitializeComponent();
            Loaded += TourProfitabilityAnalysisWindow_Loaded;
        }

        private void TourProfitabilityAnalysisWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadTourOptions();
        }

        private void LoadTourOptions()
        {
            try
            {
                using var db = new AppDbContext();

                _tourOptions.Clear();
                _tourOptions.AddRange(
                    db.Registrations
                        .AsNoTracking()
                        .Where(r => r.IsActive)
                        .GroupBy(r => new
                        {
                            TourType = (r.TourType ?? "").Trim(),
                            Period = (r.Period ?? "").Trim(),
                            GroupType = (r.GroupType ?? "").Trim(),
                            DaysCount = (r.DaysCount ?? "").Trim()
                        })
                        .Select(g => new TourOption
                        {
                            TourType = g.Key.TourType,
                            Period = g.Key.Period,
                            GroupType = g.Key.GroupType,
                            DaysCount = g.Key.DaysCount
                        })
                        .OrderBy(x => x.TourType)
                        .ThenBy(x => x.Period)
                        .ThenBy(x => x.GroupType)
                        .ThenBy(x => x.DaysCount));

                TourComboBox.ItemsSource = _tourOptions;
                if (_tourOptions.Any())
                {
                    TourComboBox.SelectedIndex = 0;
                }
                else
                {
                    HintTextBlock.Text = "Veritaban\u0131nda aktif tur kayd\u0131 bulunamad\u0131.";
                    UpdateSummary(Enumerable.Empty<Models.Registration>());
                }
            }
            catch (Exception ex)
            {
                HintTextBlock.Text = $"Tur listesi y\u00FCklenemedi: {ex.Message}";
            }
        }

        private void TourComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = TourComboBox.SelectedItem as TourOption;
            if (selected == null)
            {
                UpdateSummary(Enumerable.Empty<Models.Registration>());
                return;
            }

            try
            {
                using var db = new AppDbContext();
                var regs = db.Registrations
                    .AsNoTracking()
                    .Where(r => r.IsActive
                        && (r.TourType ?? "") == selected.TourType
                        && (r.Period ?? "") == selected.Period
                        && (r.GroupType ?? "") == selected.GroupType
                        && (r.DaysCount ?? "") == selected.DaysCount)
                    .ToList();

                UpdateSummary(regs);
                HintTextBlock.Text = $"{selected.DisplayText} i\u00E7in {regs.Count} kay\u0131t bulundu. Giderleri girip Hesapla'ya basabilirsiniz.";
            }
            catch (Exception ex)
            {
                HintTextBlock.Text = $"Hesaplama s\u0131ras\u0131nda hata olu\u015Ftu: {ex.Message}";
            }
        }

        private void UpdateSummary(IEnumerable<Models.Registration> regs)
        {
            _currentRegistrations = regs.ToList();

            if (!_currentRegistrations.Any())
            {
                _currentPrimaryCurrency = string.Empty;
                _currentTourTotal = 0m;
                _currentPaidTotal = 0m;
                _currentRemainingTotal = 0m;

                TotalPriceTextBlock.Text = "-";
                TotalPaidTextBlock.Text = "-";
                TotalRemainingTextBlock.Text = "-";
                SummaryTourTotalTextBlock.Text = "-";
                TotalExpenseTextBlock.Text = "-";
                TotalProfitLossTextBlock.Text = "-";
                InstantProfitLossTextBlock.Text = "-";
                return;
            }

            var grouped = _currentRegistrations
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Currency) ? "Se\u00E7ilmedi" : r.Currency.Trim())
                .Select(g => new
                {
                    Currency = g.Key,
                    TotalPrice = g.Sum(x => x.TotalPrice),
                    Paid = g.Sum(x => x.PaidAmount),
                    Remaining = g.Sum(x => x.RemainingDebt)
                })
                .ToList();

            _currentPrimaryCurrency = grouped
                .OrderByDescending(x => x.TotalPrice)
                .Select(x => x.Currency)
                .FirstOrDefault() ?? string.Empty;

            var primary = grouped.FirstOrDefault(x => string.Equals(x.Currency, _currentPrimaryCurrency, StringComparison.OrdinalIgnoreCase))
                          ?? grouped.First();

            _currentTourTotal = primary.TotalPrice;
            _currentPaidTotal = primary.Paid;
            _currentRemainingTotal = primary.Remaining;

            TotalPriceTextBlock.Text = string.Join("\n", grouped.Select(x => FormatAmount(x.TotalPrice, x.Currency)));
            TotalPaidTextBlock.Text = string.Join("\n", grouped.Select(x => FormatAmount(x.Paid, x.Currency)));
            TotalRemainingTextBlock.Text = string.Join("\n", grouped.Select(x => FormatAmount(x.Remaining, x.Currency)));

            SummaryTourTotalTextBlock.Text = FormatAmount(_currentTourTotal, _currentPrimaryCurrency);
            TotalExpenseTextBlock.Text = "-";
            TotalProfitLossTextBlock.Text = "-";
            InstantProfitLossTextBlock.Text = "-";
        }

        private void CalculateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_currentRegistrations.Any())
                {
                HintTextBlock.Text = "\u00D6nce bir tur se\u00E7in.";
                    return;
                }

                var visaExpense = ReadAmount(VisaExpenseTextBox);
                var hotelExpense = ReadAmount(HotelExpenseTextBox);
                var ticketExpense = ReadAmount(TicketExpenseTextBox);
                var bagExpense = ReadAmount(BagExpenseTextBox);
                var totalExpense = visaExpense + hotelExpense + ticketExpense + bagExpense;

                var tourProfitLoss = _currentTourTotal - totalExpense;
                var instantProfitLoss = _currentPaidTotal - totalExpense;

                TotalExpenseTextBlock.Text = FormatAmount(totalExpense, _currentPrimaryCurrency);
                TotalProfitLossTextBlock.Text = FormatSignedAmount(tourProfitLoss, _currentPrimaryCurrency);
                InstantProfitLossTextBlock.Text = FormatSignedAmount(instantProfitLoss, _currentPrimaryCurrency);

                HintTextBlock.Text = $"Hesapland\u0131. Ana para birimi: {(_currentPrimaryCurrency ?? "Se\u00E7ilmedi")}.";
            }
            catch (Exception ex)
            {
                HintTextBlock.Text = $"Hesaplama s\u0131ras\u0131nda hata olu\u015Ftu: {ex.Message}";
            }
        }

        private static decimal ReadAmount(TextBox textBox)
        {
            if (textBox == null)
            {
                return 0m;
            }

            var text = (textBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0m;
            }

            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out var value))
            {
                return value;
            }

            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            var cleaned = new string(text.Where(c => char.IsDigit(c) || c == ',' || c == '.').ToArray());
            if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.CurrentCulture, out value))
            {
                return value;
            }

            if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            return 0m;
        }

        private static string FormatAmount(decimal amount, string currency)
        {
            if (string.IsNullOrWhiteSpace(currency))
            {
                return amount.ToString("N0", CultureInfo.CurrentCulture);
            }

            var code = currency.Trim().ToUpperInvariant();
            var symbol = code switch
            {
                "TRY" or "TL" => "\u20BA",
                "USD" => "$",
                "EUR" => "\u20AC",
                "GBP" => "\u00A3",
                "SAR" => "\uFDFC",
                _ => ""
            };

            return string.IsNullOrEmpty(symbol)
                ? $"{amount:N0} {currency}"
                : $"{symbol}{amount:N0}";
        }

        private static string FormatSignedAmount(decimal amount, string currency)
        {
            var prefix = amount >= 0 ? "+" : "-";
            return $"{prefix}{FormatAmount(Math.Abs(amount), currency)}";
        }

        public sealed class TourOption : IEquatable<TourOption>
        {
            public string TourType { get; set; } = string.Empty;
            public string Period { get; set; } = string.Empty;
            public string GroupType { get; set; } = string.Empty;
            public string DaysCount { get; set; } = string.Empty;

            public string DisplayText => $"{TourType} \u2022 {Period} \u2022 {GroupType} \u2022 {DaysCount}";

            public override string ToString() => DisplayText;

            public bool Equals(TourOption other)
            {
                if (other == null) return false;
                return string.Equals(TourType, other.TourType, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Period, other.Period, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(GroupType, other.GroupType, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(DaysCount, other.DaysCount, StringComparison.OrdinalIgnoreCase);
            }

            public override bool Equals(object obj) => Equals(obj as TourOption);

            public override int GetHashCode()
            {
                return HashCode.Combine(
                    TourType?.ToLowerInvariant(),
                    Period?.ToLowerInvariant(),
                    GroupType?.ToLowerInvariant(),
                    DaysCount?.ToLowerInvariant());
            }
        }
    }
}

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
        private List<TourOption> _tourOptions = new();

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

                _tourOptions = db.Registrations
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
                    .ThenBy(x => x.DaysCount)
                    .ToList();

                TourComboBox.ItemsSource = _tourOptions;
                if (_tourOptions.Any())
                {
                    TourComboBox.SelectedIndex = 0;
                }
                else
                {
                    HintTextBlock.Text = "Veritabanında aktif tur kaydı bulunamadı.";
                    UpdateSummary(Enumerable.Empty<Models.Registration>());
                }
            }
            catch (Exception ex)
            {
                HintTextBlock.Text = $"Tur listesi yüklenemedi: {ex.Message}";
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
                HintTextBlock.Text = $"{selected.DisplayText} için {regs.Count} kayıt bulundu.";
            }
            catch (Exception ex)
            {
                HintTextBlock.Text = $"Hesaplama sırasında hata oluştu: {ex.Message}";
            }
        }

        private void UpdateSummary(IEnumerable<Models.Registration> regs)
        {
            var list = regs.ToList();
            if (!list.Any())
            {
                TotalPriceTextBlock.Text = "-";
                TotalPaidTextBlock.Text = "-";
                TotalRemainingTextBlock.Text = "-";
                return;
            }

            var grouped = list.GroupBy(r => string.IsNullOrWhiteSpace(r.Currency) ? "Seçilmedi" : r.Currency.Trim())
                .Select(g => new
                {
                    Currency = g.Key,
                    TotalPrice = g.Sum(x => x.TotalPrice),
                    Paid = g.Sum(x => x.PaidAmount),
                    Remaining = g.Sum(x => x.RemainingDebt)
                })
                .ToList();

            TotalPriceTextBlock.Text = string.Join("\n", grouped.Select(x => FormatAmount(x.TotalPrice, x.Currency)));
            TotalPaidTextBlock.Text = string.Join("\n", grouped.Select(x => FormatAmount(x.Paid, x.Currency)));
            TotalRemainingTextBlock.Text = string.Join("\n", grouped.Select(x => FormatAmount(x.Remaining, x.Currency)));
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
                "TRY" or "TL" => "₺",
                "USD" => "$",
                "EUR" => "€",
                "GBP" => "£",
                "SAR" => "﷼",
                _ => ""
            };

            return string.IsNullOrEmpty(symbol)
                ? $"{amount:N0} {currency}"
                : $"{symbol}{amount:N0}";
        }

        public sealed class TourOption : IEquatable<TourOption>
        {
            public string TourType { get; set; } = string.Empty;
            public string Period { get; set; } = string.Empty;
            public string GroupType { get; set; } = string.Empty;
            public string DaysCount { get; set; } = string.Empty;

            public string DisplayText => $"{TourType} • {Period} • {GroupType} • {DaysCount}";

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

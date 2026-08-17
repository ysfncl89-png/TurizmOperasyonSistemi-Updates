using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TurizmOperasyonSistemi.Data;
using Microsoft.Win32;
using System.IO;
using TurizmOperasyonSistemi.Services;

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
        private bool _useCustomRates;
        private bool _suppressCustomRatesCheckChanged;
        private decimal _customUsdRate;
        private decimal _customEurRate;
        private decimal _customGbpRate;
        private decimal _customSarRate;

        public TourProfitabilityAnalysisWindow()
        {
            InitializeComponent();
            Loaded += TourProfitabilityAnalysisWindow_Loaded;
        }

        private async void TourProfitabilityAnalysisWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadTourOptionsAsync();
        }

        private async System.Threading.Tasks.Task LoadTourOptionsAsync()
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
                    HintTextBlock.Text = "Veritabanında aktif tur kaydı bulunamadı.";
                    await UpdateSummaryAsync(Enumerable.Empty<Models.Registration>());
                }
            }
            catch (Exception ex)
            {
                HintTextBlock.Text = $"Tur listesi yüklenemedi: {ex.Message}";
            }
        }

        private async void TourComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = TourComboBox.SelectedItem as TourOption;
            if (selected == null)
            {
                await UpdateSummaryAsync(Enumerable.Empty<Models.Registration>());
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

                await UpdateSummaryAsync(regs);
                HintTextBlock.Text = $"{selected.DisplayText} için {regs.Count} kayıt bulundu. Giderleri giriniz - hesaplama otomatik yapılacak.";
            }
            catch (Exception ex)
            {
                HintTextBlock.Text = $"Hesaplama sırasında hata oluştu: {ex.Message}";
            }
        }

        private async System.Threading.Tasks.Task UpdateSummaryAsync(IEnumerable<Models.Registration> regs, bool resetExpenses = true)
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
                TotalExpenseTextBlock.Text = "-";
                TotalProfitLossTextBlock.Text = "-";
                InstantProfitLossTextBlock.Text = "-";
                ProfitMarginTextBlock.Text = "-";
                ProfitRateTextBlock.Text = "-";
                InstantProfitMarginTextBlock.Text = "-";
                InstantProfitRateTextBlock.Text = "-";
                return;
            }

            var grouped = _currentRegistrations
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Currency) ? "Seçilmedi" : r.Currency.Trim())
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

            // Eğer birden fazla para birimi varsa, diğer para birimlerini de dönüşüm yaparak dikkate al
            decimal convertedTourTotal = 0m;
            decimal convertedPaidTotal = 0m;
            decimal convertedRemainingTotal = 0m;

            try
            {
                // Varsayılan olarak kurlar 0. Eğer servis erişilemezse eski davranışa düş
                decimal usdRate = 0m, eurRate = 0m, gbpRate = 0m, sarRate = 0m;
                try
                {
                    if (_useCustomRates)
                    {
                        usdRate = _customUsdRate;
                        eurRate = _customEurRate;
                        gbpRate = _customGbpRate;
                        sarRate = _customSarRate;
                    }
                    else
                    {
                        var svc = new ExchangeRateService();
                        var rates = await svc.GetLatestRatesAsync();
                        if (rates != null)
                        {
                            decimal.TryParse(rates.UsdBuy, NumberStyles.Any, CultureInfo.InvariantCulture, out usdRate);
                            decimal.TryParse(rates.EurBuy, NumberStyles.Any, CultureInfo.InvariantCulture, out eurRate);
                            decimal.TryParse(rates.GbpBuy, NumberStyles.Any, CultureInfo.InvariantCulture, out gbpRate);
                            decimal.TryParse(rates.SarBuy, NumberStyles.Any, CultureInfo.InvariantCulture, out sarRate);
                        }
                    }
                }
                catch { }

                // Normalize fonksiyonu: para birimi metinlerini standardize et
                string NormalizeCurrencyTop(string cur)
                {
                    if (string.IsNullOrWhiteSpace(cur)) return string.Empty;
                    var up = cur.Trim().ToUpperInvariant();
                    if (up == "USD" || up == "US" || up.Contains("DOLAR") || up.Contains("DOLLAR") || up.Contains("$")) return "USD";
                    if (up == "EUR" || up.Contains("EURO") || up.Contains("€")) return "EUR";
                    if (up == "GBP" || up.Contains("STERLIN") || up.Contains("POUND") || up.Contains("£")) return "GBP";
                    if (up == "SAR" || up.Contains("RIYAL") || up.Contains("RİYAL") || up.Contains("﷼")) return "SAR";
                    if (up == "TRY" || up == "TL" || up.Contains("TURK") || up.Contains("TÜRK")) return "TRY";
                    return up;
                }

                bool hasUsd = grouped.Any(x => NormalizeCurrencyTop(x.Currency) == "USD");

                decimal ConvertToUsdLocal(decimal amount, string currency, decimal usdRate, decimal eurRate, decimal gbpRate, decimal sarRate)
                {
                    if (amount == 0m) return 0m;
                    var code = NormalizeCurrencyTop(currency);
                    try
                    {
                        if (code == "USD") return amount;
                        if (code == "EUR") return (eurRate > 0 && usdRate > 0) ? amount * (eurRate / usdRate) : 0m;
                        if (code == "GBP") return (gbpRate > 0 && usdRate > 0) ? amount * (gbpRate / usdRate) : 0m;
                        if (code == "SAR") return (sarRate > 0 && usdRate > 0) ? amount * (sarRate / usdRate) : 0m;
                        if (code == "TRY") return (usdRate > 0) ? amount / usdRate : 0m; // TRY -> USD
                    }
                    catch { }
                    return 0m; // tanınmayan para birimi
                }


                if (hasUsd)
                {
                    // Tüm para birimlerini USD'ye çevir ve topla
                    foreach (var g in grouped)
                    {
                        convertedTourTotal += ConvertToUsdLocal(g.TotalPrice, g.Currency, usdRate, eurRate, gbpRate, sarRate);
                        convertedPaidTotal += ConvertToUsdLocal(g.Paid, g.Currency, usdRate, eurRate, gbpRate, sarRate);
                        convertedRemainingTotal += ConvertToUsdLocal(g.Remaining, g.Currency, usdRate, eurRate, gbpRate, sarRate);
                    }

                    // Eğer kur bilgisi alınamadıysa (usdRate==0) fallback: primary kullan
                    if (convertedTourTotal == 0m && convertedPaidTotal == 0m && convertedRemainingTotal == 0m)
                    {
                        convertedTourTotal = primary.TotalPrice;
                        convertedPaidTotal = primary.Paid;
                        convertedRemainingTotal = primary.Remaining;
                    }

// Hedef para birimi artık USD olacak
                    _currentPrimaryCurrency = "USD";
                }
                else
                {
                    // Önceki davranış: seçili primary para birimine çevir (TRY veya diğerleri)
                    static decimal ToTry(decimal amount, string currency, decimal usdRate, decimal eurRate, decimal gbpRate, decimal sarRate)
                    {
                        var cur = (currency ?? "").Trim().ToUpperInvariant();
                        if (string.IsNullOrEmpty(cur) || cur == "TRY" || cur == "TL") return amount;
                        if (cur == "USD")
                        {
                            if (usdRate > 0) return amount * usdRate;
                            return 0m;
                        }
                        if (cur == "EUR")
                        {
                            if (eurRate > 0) return amount * eurRate;
                            return 0m;
                        }
                        if (cur == "GBP")
                        {
                            if (gbpRate > 0) return amount * gbpRate;
                            return 0m;
                        }
                        if (cur == "SAR")
                        {
                            if (sarRate > 0) return amount * sarRate;
                            return 0m;
                        }
                        return 0m;
                    }

                    static decimal FromTryTo(decimal amountInTry, string targetCurrency, decimal usdRate, decimal eurRate, decimal gbpRate, decimal sarRate)
                    {
                        var tgt = (targetCurrency ?? "").Trim().ToUpperInvariant();
                        if (string.IsNullOrEmpty(tgt) || tgt == "TRY" || tgt == "TL") return amountInTry;
                        if (tgt == "USD")
                        {
                            if (usdRate > 0) return amountInTry / usdRate;
                            return 0m;
                        }
                        if (tgt == "EUR")
                        {
                            if (eurRate > 0) return amountInTry / eurRate;
                            return 0m;
                        }
                        if (tgt == "GBP")
                        {
                            if (gbpRate > 0) return amountInTry / gbpRate;
                            return 0m;
                        }
                        if (tgt == "SAR")
                        {
                            if (sarRate > 0) return amountInTry / sarRate;
                            return 0m;
                        }
                        return 0m;
                    }

                    foreach (var g in grouped)
                    {
                        var amtTry = ToTry(g.TotalPrice, g.Currency, usdRate, eurRate, gbpRate, sarRate);
                        var paidTry = ToTry(g.Paid, g.Currency, usdRate, eurRate, gbpRate, sarRate);
                        var remTry = ToTry(g.Remaining, g.Currency, usdRate, eurRate, gbpRate, sarRate);

                        if (string.IsNullOrWhiteSpace(_currentPrimaryCurrency) || _currentPrimaryCurrency.Trim().ToUpperInvariant() == "TRY" || _currentPrimaryCurrency.Trim().ToUpperInvariant() == "TL")
                        {
                            convertedTourTotal += amtTry;
                            convertedPaidTotal += paidTry;
                            convertedRemainingTotal += remTry;
                        }
                        else
                        {
                            convertedTourTotal += FromTryTo(amtTry, _currentPrimaryCurrency, usdRate, eurRate, gbpRate, sarRate);
                            convertedPaidTotal += FromTryTo(paidTry, _currentPrimaryCurrency, usdRate, eurRate, gbpRate, sarRate);
                            convertedRemainingTotal += FromTryTo(remTry, _currentPrimaryCurrency, usdRate, eurRate, gbpRate, sarRate);
                        }
                    }

                    if (convertedTourTotal == 0m && convertedPaidTotal == 0m && convertedRemainingTotal == 0m)
                    {
                        convertedTourTotal = primary.TotalPrice;
                        convertedPaidTotal = primary.Paid;
                        convertedRemainingTotal = primary.Remaining;
                    }
                }
            }
            catch
            {
                // Hata durumunda önceki davranışa dön
                convertedTourTotal = primary.TotalPrice;
                convertedPaidTotal = primary.Paid;
                convertedRemainingTotal = primary.Remaining;
            }

            _currentTourTotal = convertedTourTotal;
            _currentPaidTotal = convertedPaidTotal;
            _currentRemainingTotal = convertedRemainingTotal;

            TotalPriceTextBlock.Text = string.Join("\n", grouped.Select(x => FormatAmount(x.TotalPrice, x.Currency)));
            TotalPaidTextBlock.Text = string.Join("\n", grouped.Select(x => FormatAmount(x.Paid, x.Currency)));
            TotalRemainingTextBlock.Text = string.Join("\n", grouped.Select(x => FormatAmount(x.Remaining, x.Currency)));

            TotalExpenseTextBlock.Text = "-";
            TotalProfitLossTextBlock.Text = "-";
            InstantProfitLossTextBlock.Text = "-";
            ProfitMarginTextBlock.Text = "-";
            ProfitRateTextBlock.Text = "-";
            InstantProfitMarginTextBlock.Text = "-";
            InstantProfitRateTextBlock.Text = "-";

            if (resetExpenses)
            {
                // Yeni tur seçildiğinde önceki turun giderlerini temizle.
                HotelExpenseTextBox.Text = "";
                HotelMedineExpenseTextBox.Text = "";
                TicketExpenseTextBox.Text = "";
                VisaExpenseTextBox.Text = "";
                BagExpenseTextBox.Text = "";
                StaffExpenseTextBox.Text = "";
                OtherExpenseTextBox.Text = "";
            }
        }

        private async void CustomRatesCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressCustomRatesCheckChanged)
            {
                return;
            }

            if (!TryShowCustomRatesDialog(out var usdRate, out var eurRate, out var gbpRate, out var sarRate))
            {
                _suppressCustomRatesCheckChanged = true;
                CustomRatesCheckBox.IsChecked = false;
                _suppressCustomRatesCheckChanged = false;
                return;
            }

            _customUsdRate = usdRate;
            _customEurRate = eurRate;
            _customGbpRate = gbpRate;
            _customSarRate = sarRate;
            _useCustomRates = true;

            if (_currentRegistrations.Any())
            {
                await UpdateSummaryAsync(_currentRegistrations, resetExpenses: false);
                CalculateResults();
            }

            HintTextBlock.Text = "Özel kurlar kullanılıyor. İşareti kaldırarak TCMB kurlarına dönebilirsiniz.";
        }

        private async void CustomRatesCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_suppressCustomRatesCheckChanged || !_useCustomRates)
            {
                return;
            }

            _useCustomRates = false;
            if (_currentRegistrations.Any())
            {
                await UpdateSummaryAsync(_currentRegistrations, resetExpenses: false);
                CalculateResults();
            }

            HintTextBlock.Text = "TCMB kurları kullanılıyor.";
        }

        private bool TryShowCustomRatesDialog(out decimal usdRate, out decimal eurRate, out decimal gbpRate, out decimal sarRate)
        {
            usdRate = eurRate = gbpRate = sarRate = 0m;
            decimal parsedUsdRate = 0m, parsedEurRate = 0m, parsedGbpRate = 0m, parsedSarRate = 0m;
            var culture = CultureInfo.GetCultureInfo("tr-TR");
            var dialogBackground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 24, 39));
            var dialogForeground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(226, 232, 240));
            var dialog = new Window
            {
                Title = "Özel Döviz Kurları",
                Width = 360,
                Height = 390,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = dialogBackground,
                Foreground = dialogForeground
            };

            var owner = Window.GetWindow(this);
            if (owner != null)
            {
                dialog.Owner = owner;
            }

            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock
            {
                Text = "TCMB yerine kullanılacak 1 birim karşılığı TL kurlarını girin.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            });

            TextBox CreateRateTextBox(decimal value) => new()
            {
                Text = value > 0 ? value.ToString("N4", culture) : string.Empty,
                Height = 30,
                Margin = new Thickness(0, 3, 0, 9),
                Padding = new Thickness(7, 4, 7, 4),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 41, 55)),
                Foreground = dialogForeground,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 65, 85))
            };

            panel.Children.Add(new TextBlock { Text = "USD / TL" });
            var usdTextBox = CreateRateTextBox(_customUsdRate);
            panel.Children.Add(usdTextBox);
            panel.Children.Add(new TextBlock { Text = "EUR / TL" });
            var eurTextBox = CreateRateTextBox(_customEurRate);
            panel.Children.Add(eurTextBox);
            panel.Children.Add(new TextBlock { Text = "GBP / TL" });
            var gbpTextBox = CreateRateTextBox(_customGbpRate);
            panel.Children.Add(gbpTextBox);
            panel.Children.Add(new TextBlock { Text = "SAR / TL" });
            var sarTextBox = CreateRateTextBox(_customSarRate);
            panel.Children.Add(sarTextBox);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var cancelButton = new Button { Content = "Vazgeç", MinWidth = 80, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            var applyButton = new Button { Content = "Uygula", MinWidth = 80, IsDefault = true };
            buttons.Children.Add(cancelButton);
            buttons.Children.Add(applyButton);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            applyButton.Click += (_, _) =>
            {
                if (!TryReadCustomRate(usdTextBox.Text, out var parsedUsd)
                    || !TryReadCustomRate(eurTextBox.Text, out var parsedEur)
                    || !TryReadCustomRate(gbpTextBox.Text, out var parsedGbp)
                    || !TryReadCustomRate(sarTextBox.Text, out var parsedSar))
                {
                    MessageBox.Show(dialog, "USD, EUR, GBP ve SAR için sıfırdan büyük geçerli bir kur girin.", "Geçersiz kur", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                parsedUsdRate = parsedUsd;
                parsedEurRate = parsedEur;
                parsedGbpRate = parsedGbp;
                parsedSarRate = parsedSar;
                dialog.DialogResult = true;
            };

            if (dialog.ShowDialog() != true)
            {
                return false;
            }

            usdRate = parsedUsdRate;
            eurRate = parsedEurRate;
            gbpRate = parsedGbpRate;
            sarRate = parsedSarRate;
            return true;
        }

        private static bool TryReadCustomRate(string text, out decimal value)
        {
            return decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value) && value > 0m
                   || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value) && value > 0m;
        }

        private void ExpenseTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            CalculateResults();
        }

        private void CalculateResults()
        {
            try
            {
                if (!_currentRegistrations.Any())
                {
                    TotalExpenseTextBlock.Text = "-";
                    TotalProfitLossTextBlock.Text = "-";
                    InstantProfitLossTextBlock.Text = "-";
                    ProfitMarginTextBlock.Text = "-";
                    ProfitRateTextBlock.Text = "-";
                    InstantProfitMarginTextBlock.Text = "-";
                    InstantProfitRateTextBlock.Text = "-";
                    return;
                }

                var hotelExpense = ReadAmount(HotelExpenseTextBox);
                var hotelMedineExpense = ReadAmount(HotelMedineExpenseTextBox);
                var ticketExpense = ReadAmount(TicketExpenseTextBox);
                var visaExpense = ReadAmount(VisaExpenseTextBox);
                var bagExpense = ReadAmount(BagExpenseTextBox);
                var staffExpense = ReadAmount(StaffExpenseTextBox);
                var otherExpense = ReadAmount(OtherExpenseTextBox);

                var totalExpense = hotelExpense + hotelMedineExpense + ticketExpense + visaExpense + bagExpense + staffExpense + otherExpense;

                var tourProfitLoss = _currentTourTotal - totalExpense;
                var instantProfitLoss = _currentPaidTotal - totalExpense;

                TotalExpenseTextBlock.Text = FormatAmount(totalExpense, _currentPrimaryCurrency);
                TotalProfitLossTextBlock.Text = FormatSignedAmountWithColor(tourProfitLoss, _currentPrimaryCurrency, TotalProfitLossTextBlock);
                InstantProfitLossTextBlock.Text = FormatSignedAmountWithColor(instantProfitLoss, _currentPrimaryCurrency, InstantProfitLossTextBlock);

                // Kar Marjı = (Net Kar / Toplam Satış) × 100
                var profitMargin = _currentTourTotal != 0 ? (tourProfitLoss / _currentTourTotal) * 100 : 0;
                ProfitMarginTextBlock.Text = FormatSignedAmountWithColor(profitMargin, "%", ProfitMarginTextBlock);

                // Kar Oranı = (Net Kar / Toplam Maliyet) × 100
                var profitRate = totalExpense != 0 ? (tourProfitLoss / totalExpense) * 100 : 0;
                ProfitRateTextBlock.Text = FormatSignedAmountWithColor(profitRate, "%", ProfitRateTextBlock);

                // Tahsilata göre Kar Marjı = (Tahsilata göre Net Kar / Tahsil Edilen) × 100
                var instantProfitMargin = _currentPaidTotal != 0 ? (instantProfitLoss / _currentPaidTotal) * 100 : 0;
                InstantProfitMarginTextBlock.Text = FormatSignedAmountWithColor(instantProfitMargin, "%", InstantProfitMarginTextBlock);

                // Tahsilata göre Kar Oranı = (Tahsilata göre Net Kar / Toplam Maliyet) × 100
                var instantProfitRate = totalExpense != 0 ? (instantProfitLoss / totalExpense) * 100 : 0;
                InstantProfitRateTextBlock.Text = FormatSignedAmountWithColor(instantProfitRate, "%", InstantProfitRateTextBlock);
            }
            catch (Exception ex)
            {
                HintTextBlock.Text = $"Hesaplama sırasında hata oluştu: {ex.Message}";
            }
        }

        private void CalculateButton_Click(object sender, RoutedEventArgs e)
        {
            CalculateResults();
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
                "TRY" or "TL" => "₺",
                "USD" => "$",
                "EUR" => "€",
                "GBP" => "£",
                "SAR" => "﷼",
                "%" => "%",
                _ => ""
            };

            return string.IsNullOrEmpty(symbol)
                ? $"{amount:N0} {currency}"
                : code == "%" 
                    ? $"{amount:N2}{symbol}"
                    : $"{symbol}{amount:N0}";
        }

        private static string FormatSignedAmount(decimal amount, string currency)
        {
            var prefix = amount >= 0 ? "+" : "-";
            return $"{prefix}{FormatAmount(Math.Abs(amount), currency)}";
        }

        private static string FormatSignedAmountWithColor(decimal amount, string currency, TextBlock textBlock)
        {
            // Rengi değiştir: yeşil pozitif, kırmızı negatif
            if (amount >= 0)
            {
                textBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 16, 185, 129)); // Yeşil #10B981
            }
            else
            {
                textBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 239, 68, 68)); // Kırmızı #EF4444
            }

            var prefix = amount >= 0 ? "+" : "-";
            return $"{prefix}{FormatAmount(Math.Abs(amount), currency)}";
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

        // ============ EXPORT METHODS ============
        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Öncelikle popup'ı açmayı dene (ikonlu büyük butonlar)
                var popup = this.FindName("ExportPopup") as System.Windows.Controls.Primitives.Popup;
                if (popup != null)
                {
                    popup.PlacementTarget = this.FindName("ExportButton") as UIElement;
                    popup.IsOpen = true;
                    return;
                }

                // Eğer popup bulunamazsa fallback olarak ContextMenu aç
                var btn = this.FindName("ExportButton") as Button;
                if (btn?.ContextMenu != null)
                {
                    btn.ContextMenu.PlacementTarget = btn;
                    btn.ContextMenu.IsOpen = true;
                }
            }
            catch { }
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

        private string GetExportDefaultFileName(string extWithoutDot)
        {
            var name = "Tur_Karlilık_Analizi";

            try
            {
                var selected = TourComboBox.SelectedItem as TourOption;
                if (selected != null)
                {
                    var tourParts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(selected.TourType)) tourParts.Add(selected.TourType.Trim());
                    if (!string.IsNullOrWhiteSpace(selected.Period)) tourParts.Add(selected.Period.Trim());
                    if (!string.IsNullOrWhiteSpace(selected.GroupType)) tourParts.Add(selected.GroupType.Trim());
                    if (!string.IsNullOrWhiteSpace(selected.DaysCount)) tourParts.Add(selected.DaysCount.Trim());

                    if (tourParts.Count > 0)
                    {
                        var tourName = string.Join("_", tourParts);
                        name = $"Tur_Karlilık_Analizi_{tourName}";
                    }
                }
            }
            catch { }

            // Tarih ve saat ekle
            name += $"_{DateTime.Now:yyyyMMdd_HHmmss}";

            // Karakterleri temizle
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');

            // Uzunluğu sınırla
            const int maxLength = 120;
            if (name.Length > maxLength) name = name.Substring(0, maxLength).Trim();

            return name + "." + extWithoutDot;
        }

        private void ExportWord(string filePath)
        {
            try
            {
                var headers = GetExportHeaders();
                var rows = GetExportRows(headers);
                var title = GetExportTitle();
                ExportHelper.ExportWord(filePath, rows, headers, title);
                MessageBox.Show("Çıktı başarıyla kaydedildi.", "Başarı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Çıktı kaydedilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportExcel(string filePath)
        {
            try
            {
                var headers = GetExportHeaders();
                var rows = GetExportRows(headers);
                var title = GetExportTitle();
                ExportHelper.ExportExcel(filePath, rows, headers, title);
                MessageBox.Show("Çıktı başarıyla kaydedildi.", "Başarı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Çıktı kaydedilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportPdf(string filePath)
        {
            try
            {
                var headers = GetExportHeaders();
                // PDF için Türkçe karakterleri transliterate et
                var headersForPdf = headers.Select(h => TransliterateTurkish(h)).ToList();
                var rowsForPdf = GetExportRowsForPdf(headers);
                var titleForPdf = TransliterateTurkish(GetExportTitle());
                ExportHelper.ExportPdf(filePath, rowsForPdf, headersForPdf, titleForPdf);
                MessageBox.Show("Çıktı başarıyla kaydedildi.", "Başarı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Çıktı kaydedilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetExportTitle()
        {
            var selected = TourComboBox.SelectedItem as TourOption;
            return selected != null ? $"Tur Kârlılık Analizi - {selected.DisplayText}" : "Tur Kârlılık Analizi";
        }

        private List<string> GetExportHeaders()
        {
            return new List<string>
            {
                "Bölüm",
                "Açıklama",
                "Değer"
            };
        }

        private List<Dictionary<string, string>> GetExportRows(List<string> headers)
        {
            var rows = new List<Dictionary<string, string>>();

            // TUR ÖZETI - Satış Bilgileri
            var line1 = new Dictionary<string, string> { { "Bölüm", "TUR ÖZETİ" }, { "Açıklama", "Toplam Satış" }, { "Değer", TotalPriceTextBlock.Text } };
            rows.Add(line1);

            var line2 = new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Tahsil Edilen" }, { "Değer", TotalPaidTextBlock.Text } };
            rows.Add(line2);

            var line3 = new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Bekleyen Tahsilat" }, { "Değer", TotalRemainingTextBlock.Text } };
            rows.Add(line3);

            // GİDERLER
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "" }, { "Değer", "" } }); // Boş satır
            rows.Add(new Dictionary<string, string> { { "Bölüm", "GİDERLER" }, { "Açıklama", "Otel ve Yemek Mekke" }, { "Değer", HotelExpenseTextBox.Text ?? "" } });
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Otel ve Yemek Medine" }, { "Değer", HotelMedineExpenseTextBox.Text ?? "" } });
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Uçak Bileti" }, { "Değer", TicketExpenseTextBox.Text ?? "" } });
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Vize Masrafı" }, { "Değer", VisaExpenseTextBox.Text ?? "" } });
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Çanta / Kitapçık" }, { "Değer", BagExpenseTextBox.Text ?? "" } });
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Eleman Gideri" }, { "Değer", StaffExpenseTextBox.Text ?? "" } });
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Diğer Gider" }, { "Değer", OtherExpenseTextBox.Text ?? "" } });

            // KAR / ZARAR ANALİZİ
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "" }, { "Değer", "" } }); // Boş satır
            rows.Add(new Dictionary<string, string> { { "Bölüm", "KAR / ZARAR ANALİZİ" }, { "Açıklama", "Toplam Maliyet" }, { "Değer", TotalExpenseTextBlock.Text } });
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Net Kâr" }, { "Değer", TotalProfitLossTextBlock.Text } });
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Tahsilata Göre" }, { "Değer", InstantProfitLossTextBlock.Text } });
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Kâr Marjı (Net Kâr)" }, { "Değer", ProfitMarginTextBlock.Text } });
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Kâr Oranı (Net Kâr)" }, { "Değer", ProfitRateTextBlock.Text } });
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Kâr Marjı (Tahsil.Göre)" }, { "Değer", InstantProfitMarginTextBlock.Text } });
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Kâr Oranı (Tahsil.Göre)" }, { "Değer", InstantProfitRateTextBlock.Text } });

            // ÖDEMELER bölümü: seçili tura ait ödemeleri ekle
            try
            {
                var selected = TourComboBox.SelectedItem as TourOption;
                if (selected != null && _currentRegistrations != null && _currentRegistrations.Any())
                {
                    // Query payments for current registrations
                    using var db = new AppDbContext();
                    var regIds = _currentRegistrations.Select(r => r.Id).ToList();
                    var payments = db.Payments
                        .AsNoTracking()
                        .Where(p => regIds.Contains(p.RegistrationId))
                        .Include(p => p.Registration)
                        .ThenInclude(r => r.Person)
                        .OrderBy(p => p.CreatedAt)
                        .ToList();

                    if (payments.Any())
                    {
                        rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "" }, { "Değer", "" } });
                        // Build a separate payments table with detailed columns (Sıra, Ad, Soyad, TC Kimlik, Pasaport numarası, Toplam Fiyat, Para Birimi, Ödenen, Kalan, Açıklama)
                        var paymentHeaders = new[] { "Sıra", "Ad", "Soyad", "TC Kimlik", "Pasaport numarası", "Toplam Fiyat", "Para Birimi", "Ödenen", "Kalan", "Açıklama" };
                        // Insert marker that ExportHelper recognizes to start a new table with these headers
                        rows.Add(new Dictionary<string, string> { { "__TABLE_HEADERS", string.Join(",", paymentHeaders) } });

                        // Group payments by person (or registration id) so payments for the same person appear together
                        var grouped = payments.GroupBy(p =>
                            p.Registration?.Person != null
                                ? $"{(p.Registration.Person.FirstName ?? string.Empty).Trim()} {(p.Registration.Person.LastName ?? string.Empty).Trim()}".Trim().ToUpperInvariant()
                                : $"Kayıt #{p.RegistrationId}")
                            .OrderBy(g => g.Key);

                        // Also load registrations with person info to compute per-person totals
                        var regsWithPerson = db.Registrations
                            .AsNoTracking()
                            .Where(r => regIds.Contains(r.Id))
                            .Include(r => r.Person)
                            .ToList();

                        var idx = 1;
                        foreach (var grp in grouped)
                        {
                            var personKey = grp.Key;

                            // Find registrations that belong to this person (match by person name or registration id fallback)
                            var regsForPerson = regsWithPerson.Where(r =>
                            {
                                if (r.Person != null)
                                {
                                    var nameKey = $"{(r.Person.FirstName ?? string.Empty).Trim()} {(r.Person.LastName ?? string.Empty).Trim()}".Trim().ToUpperInvariant();
                                    return string.Equals(nameKey, personKey, StringComparison.OrdinalIgnoreCase);
                                }
                                return ($"Kayıt #{r.Id}") == personKey;
                            }).ToList();

                            decimal regTotalPrice = regsForPerson.Any() ? regsForPerson.Sum(r => r.TotalPrice) : 0m;
                            decimal regPaid = regsForPerson.Any() ? regsForPerson.Sum(r => r.PaidAmount) : 0m;
                            decimal regRemaining = regsForPerson.Any() ? regsForPerson.Sum(r => r.RemainingDebt) : 0m;

                            // Determine person details from registration if available
                            string firstName = string.Empty, lastName = string.Empty, tc = string.Empty, passport = string.Empty;
                            if (regsForPerson.Any() && regsForPerson.First().Person != null)
                            {
                                var p = regsForPerson.First().Person;
                                firstName = (p.FirstName ?? string.Empty).Trim();
                                lastName = (p.LastName ?? string.Empty).Trim();
                                tc = (p.TcKimlikNo ?? string.Empty).Trim();
                                passport = (p.PassportNumber ?? string.Empty).Trim();
                            }
                            else
                            {
                                // try to split the personKey into first/last
                                var parts = personKey.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length > 0) firstName = parts[0];
                                if (parts.Length > 1) lastName = string.Join(' ', parts.Skip(1));
                            }

                            var entries = grp.OrderBy(p => p.CreatedAt).Select(p =>
                            {
                                var date = p.CreatedAt.ToString("yyyy-MM-dd");
                                var amountStr = FormatAmount(p.OriginalAmount, p.OriginalCurrency ?? string.Empty);
                                var method = string.IsNullOrWhiteSpace(p.PaymentMethod) ? string.Empty : p.PaymentMethod;
                                var note = string.IsNullOrWhiteSpace(p.Note) ? string.Empty : p.Note;
                                var parts = new List<string> { $"{date}", amountStr };
                                if (!string.IsNullOrEmpty(method)) parts.Add(method);
                                if (!string.IsNullOrEmpty(note)) parts.Add(note);
                                return string.Join(" - ", parts);
                            });

                            var paymentsJoined = string.Join("\n", entries);

                            var row = new Dictionary<string, string>
                            {
                                { "Sıra", idx.ToString() },
                                { "Ad", firstName },
                                { "Soyad", lastName },
                                { "TC Kimlik", tc },
                                { "Pasaport numarası", passport },
                                { "Toplam Fiyat", FormatAmount(regTotalPrice, _currentPrimaryCurrency) },
                                { "Para Birimi", _currentPrimaryCurrency },
                                { "Ödenen", FormatAmount(regPaid, _currentPrimaryCurrency) },
                                { "Kalan", FormatAmount(regRemaining, _currentPrimaryCurrency) },
                                { "Açıklama", paymentsJoined }
                            };

                            rows.Add(row);
                            idx++;
                        }
                    }
                }
            }
            catch { /* don't break export on payment fetch errors */ }

            return rows;
        }

        private List<Dictionary<string, string>> GetExportRowsForPdf(List<string> headers)
        {
            // PDF için Türkçe karakterleri transliterate et
            var rows = GetExportRows(headers);

            var transliteratedRows = new List<Dictionary<string, string>>();
            foreach (var row in rows)
            {
                var transliteratedRow = new Dictionary<string, string>();
                foreach (var kvp in row)
                {
                    // Key ve value ikisini de transliterate et
                    var transliteratedKey = TransliterateTurkish(kvp.Key);
                    var transliteratedValue = TransliterateTurkish(kvp.Value);
                    transliteratedRow[transliteratedKey] = transliteratedValue;
                }
                transliteratedRows.Add(transliteratedRow);
            }

            return transliteratedRows;
        }

        private string TransliterateTurkish(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            return value
                .Replace("İ", "I").Replace("ı", "i")
                .Replace("Ş", "S").Replace("ş", "s")
                .Replace("Ğ", "G").Replace("ğ", "g")
                .Replace("Ü", "U").Replace("ü", "u")
                .Replace("Ö", "O").Replace("ö", "o")
                .Replace("Ç", "C").Replace("ç", "c")
                .Replace("â", "a").Replace("Â", "A");
        }
    }
}

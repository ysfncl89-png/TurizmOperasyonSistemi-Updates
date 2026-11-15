using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TurizmOperasyonSistemi.Data;
using Microsoft.Win32;
using System.IO;
using TurizmOperasyonSistemi.Services;

namespace TurizmOperasyonSistemi.Views
{
    public partial class TourProfitabilityAnalysisWindow : UserControl
    {
        private const string PaymentExportTemplateName = "TourProfitabilityAnalysis.Payments";
        private static readonly (string Key, string Header)[] PaymentExportColumns =
        {
            ("sequence", "Sıra"),
            ("firstName", "Ad"),
            ("lastName", "Soyad"),
            ("tcKimlik", "TC Kimlik"),
            ("passportNumber", "Pasaport numarası"),
            ("paymentDate", "Ödeme Tarihi"),
            ("paymentAmount", "Ödeme Tutarı"),
            ("currency", "Para Birimi"),
            ("paymentMethod", "Ödeme Yöntemi"),
            ("note", "Açıklama")
        };

        private readonly List<TourOption> _tourOptions = new();
        private readonly List<CustomExpenseEntry> _customExpenses = new();
        private readonly ListTemplateStore _paymentExportTemplateStore = new();
        private readonly Dictionary<string, bool> _paymentExportColumnVisibility = PaymentExportColumns
            .ToDictionary(column => column.Key, _ => true, StringComparer.OrdinalIgnoreCase);
        private List<Models.Registration> _currentRegistrations = new();
        private string _currentPrimaryCurrency = string.Empty;
        private decimal _currentTourTotal;
        private decimal _currentPaidTotal;
        private decimal _currentRemainingTotal;
        private bool _useCustomRates;
        private bool _suppressCustomRatesCheckChanged;
        private int _summaryUpdateVersion;
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
            var updateVersion = ++_summaryUpdateVersion;
            var useCustomRates = _useCustomRates;
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
                    if (useCustomRates)
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
                        if (updateVersion != _summaryUpdateVersion || useCustomRates != _useCustomRates)
                        {
                            return;
                        }

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


                // Gider kutuları USD cinsinden olduğu için gelir ve tahsilatları da USD bazına çevir.
                foreach (var g in grouped)
                {
                    convertedTourTotal += ConvertToUsdLocal(g.TotalPrice, g.Currency, usdRate, eurRate, gbpRate, sarRate);
                    convertedPaidTotal += ConvertToUsdLocal(g.Paid, g.Currency, usdRate, eurRate, gbpRate, sarRate);
                    convertedRemainingTotal += ConvertToUsdLocal(g.Remaining, g.Currency, usdRate, eurRate, gbpRate, sarRate);
                }

                if (convertedTourTotal == 0m && convertedPaidTotal == 0m && convertedRemainingTotal == 0m)
                {
                    // Kur alınamadığında mevcut primary para birimindeki önceki toplamları koru.
                    convertedTourTotal = primary.TotalPrice;
                    convertedPaidTotal = primary.Paid;
                    convertedRemainingTotal = primary.Remaining;
                    _currentPrimaryCurrency = primary.Currency;
                }
                else
                {
                    _currentPrimaryCurrency = "USD";
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
                HotelExpenseDescriptionTextBox.Text = "";
                HotelMedineExpenseDescriptionTextBox.Text = "";
                TicketExpenseDescriptionTextBox.Text = "";
                VisaExpenseDescriptionTextBox.Text = "";
                BagExpenseDescriptionTextBox.Text = "";
                StaffExpenseDescriptionTextBox.Text = "";
                foreach (var expense in _customExpenses)
                {
                    expense.AmountTextBox.Text = "";
                    expense.DescriptionTextBox.Text = "";
                }
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

            if (_useCustomRates && CustomRatesCheckBox.IsChecked == true)
            {
                HintTextBlock.Text = "Özel kurlar kullanılıyor; kâr/zarar USD bazında hesaplanıyor. İşareti kaldırarak TCMB kurlarına dönebilirsiniz.";
            }
        }

        private async void CustomRatesCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_suppressCustomRatesCheckChanged || !_useCustomRates)
            {
                return;
            }

            _useCustomRates = false;
            HintTextBlock.Text = "TCMB kurları alınıyor...";
            if (_currentRegistrations.Any())
            {
                await UpdateSummaryAsync(_currentRegistrations, resetExpenses: false);
                CalculateResults();
            }

            if (!_useCustomRates && CustomRatesCheckBox.IsChecked != true)
            {
                HintTextBlock.Text = "TCMB kurları kullanılıyor; kâr/zarar USD bazında hesaplanıyor.";
            }
        }

        private void DesignPaymentExportColumnsButton_Click(object sender, RoutedEventArgs e)
        {
            var savedColumns = _paymentExportTemplateStore.LoadTemplate(PaymentExportTemplateName);
            if (savedColumns != null)
            {
                foreach (var column in PaymentExportColumns)
                {
                    var savedColumn = savedColumns.FirstOrDefault(saved =>
                        string.Equals(saved.Key, column.Key, StringComparison.OrdinalIgnoreCase));
                    if (savedColumn != null)
                    {
                        _paymentExportColumnVisibility[column.Key] = savedColumn.IsVisible;
                    }
                }
            }

            var foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(226, 232, 240));
            var dialog = new Window
            {
                Title = "Listeyi Tasarla - Ödeme Yapan Kişiler",
                Width = 440,
                Height = 560,
                MinWidth = 380,
                MinHeight = 440,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(11, 18, 32)),
                Foreground = foreground
            };

            var owner = Window.GetWindow(this);
            if (owner != null)
            {
                dialog.Owner = owner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            heading.Children.Add(new TextBlock
            {
                Text = "Ödeme tablosunda gösterilecek sütunlar",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = foreground
            });
            heading.Children.Add(new TextBlock
            {
                Text = "İşaretli sütunlar Word, Excel ve PDF çıktısına eklenir. Seçiminiz kaydedilir.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184)),
                Margin = new Thickness(0, 6, 0, 0)
            });
            Grid.SetRow(heading, 0);
            root.Children.Add(heading);

            var columnPanel = new StackPanel();
            var checkBoxes = new List<(string Key, CheckBox CheckBox)>();
            foreach (var column in PaymentExportColumns)
            {
                var checkBox = new CheckBox
                {
                    Content = column.Header,
                    IsChecked = _paymentExportColumnVisibility[column.Key],
                    Foreground = foreground,
                    FontSize = 13,
                    Margin = new Thickness(4, 8, 4, 8)
                };
                checkBoxes.Add((column.Key, checkBox));
                columnPanel.Children.Add(checkBox);
            }

            var columnsBorder = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 27, 45)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 65, 85)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = columnPanel
                }
            };
            Grid.SetRow(columnsBorder, 1);
            root.Children.Add(columnsBorder);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var cancelButton = new Button
            {
                Content = "Vazgeç",
                MinWidth = 90,
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true
            };
            var applyButton = new Button
            {
                Content = "Uygula",
                MinWidth = 90,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(109, 40, 217)),
                Foreground = System.Windows.Media.Brushes.White,
                IsDefault = true
            };
            applyButton.Click += (_, _) =>
            {
                foreach (var item in checkBoxes)
                {
                    _paymentExportColumnVisibility[item.Key] = item.CheckBox.IsChecked == true;
                }

                _paymentExportTemplateStore.SaveTemplate(
                    PaymentExportTemplateName,
                    PaymentExportColumns.Select((column, index) => new ListTemplateStore.ColumnDto
                    {
                        Key = column.Key,
                        Title = column.Header,
                        Order = index + 1,
                        IsVisible = _paymentExportColumnVisibility[column.Key]
                    }).ToList());

                dialog.DialogResult = true;
            };
            buttons.Children.Add(cancelButton);
            buttons.Children.Add(applyButton);
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);

            dialog.Content = root;
            dialog.ShowDialog();
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

        private void AddExpenseButton_Click(object sender, RoutedEventArgs e)
        {
            var expenseName = ShowAddExpenseDialog();
            if (string.IsNullOrWhiteSpace(expenseName))
            {
                return;
            }

            AddCustomExpense(expenseName);
            CalculateResults();
        }

        private string ShowAddExpenseDialog()
        {
            var dialogForeground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(226, 232, 240));
            var dialog = new Window
            {
                Title = "Yeni Gider",
                Width = 360,
                Height = 190,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 24, 39)),
                Foreground = dialogForeground
            };

            var owner = Window.GetWindow(this);
            if (owner != null)
            {
                dialog.Owner = owner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock
            {
                Text = "Giderin adı",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var nameTextBox = new TextBox
            {
                Height = 32,
                MaxLength = 60,
                Padding = new Thickness(7, 4, 7, 4),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 41, 55)),
                Foreground = dialogForeground,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(139, 92, 246))
            };
            panel.Children.Add(nameTextBox);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            buttons.Children.Add(new Button
            {
                Content = "Vazgeç",
                MinWidth = 80,
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true
            });

            var addButton = new Button { Content = "Ekle", MinWidth = 80, IsDefault = true };
            addButton.Click += (_, _) =>
            {
                var name = nameTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    MessageBox.Show(dialog, "Lütfen giderin adını girin.", "Gider adı gerekli", MessageBoxButton.OK, MessageBoxImage.Warning);
                    nameTextBox.Focus();
                    return;
                }

                if (_customExpenses.Any(expense => string.Equals(expense.Name, name, StringComparison.CurrentCultureIgnoreCase)))
                {
                    MessageBox.Show(dialog, "Bu isimde bir gider zaten eklenmiş.", "Gider zaten mevcut", MessageBoxButton.OK, MessageBoxImage.Warning);
                    nameTextBox.SelectAll();
                    nameTextBox.Focus();
                    return;
                }

                dialog.DialogResult = true;
            };
            buttons.Children.Add(addButton);
            panel.Children.Add(buttons);
            dialog.Content = panel;
            dialog.Loaded += (_, _) => nameTextBox.Focus();

            return dialog.ShowDialog() == true ? nameTextBox.Text.Trim() : null;
        }

        private void AddCustomExpense(string expenseName)
        {
            var amountTextBox = new TextBox
            {
                Height = 28,
                FontSize = 11,
                Padding = new Thickness(6, 3, 6, 3),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            amountTextBox.SetBinding(FrameworkElement.WidthProperty, new Binding("ActualWidth")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Border), 1),
                Converter = (IValueConverter)Resources["HalfConverter"]
            });
            amountTextBox.TextChanged += ExpenseTextBox_TextChanged;
            var descriptionTextBox = new TextBox
            {
                Height = 28,
                FontSize = 11,
                Padding = new Thickness(6, 3, 6, 3),
                ToolTip = "Gider açıklaması"
            };

            var expenseRow = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
            titleRow.Children.Add(new TextBlock
            {
                Text = "🧾",
                FontSize = 14,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = expenseName,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(203, 213, 225)),
                FontSize = 11,
                MaxWidth = 220,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            });
            expenseRow.Children.Add(titleRow);

            var amountRow = new Grid();
            amountRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            amountRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            amountRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            amountRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(amountTextBox, 0);
            amountRow.Children.Add(amountTextBox);

            var descriptionLabel = new TextBlock
            {
                Text = "Açıklama",
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 4, 0)
            };
            Grid.SetColumn(descriptionLabel, 1);
            amountRow.Children.Add(descriptionLabel);
            Grid.SetColumn(descriptionTextBox, 2);
            amountRow.Children.Add(descriptionTextBox);

            var currencySymbol = new TextBlock
            {
                Text = "$",
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129)),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            };
            currencySymbol.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = System.Windows.Media.Color.FromRgb(16, 185, 129),
                BlurRadius = 10,
                ShadowDepth = 0,
                Opacity = 0.95
            };
            Grid.SetColumn(currencySymbol, 3);
            amountRow.Children.Add(currencySymbol);
            expenseRow.Children.Add(amountRow);

            CustomExpensesPanel.Children.Add(expenseRow);
            _customExpenses.Add(new CustomExpenseEntry(expenseName, amountTextBox, descriptionTextBox));
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
                var customExpensesTotal = _customExpenses.Sum(expense => ReadAmount(expense.AmountTextBox));

                var totalExpense = hotelExpense + hotelMedineExpense + ticketExpense + visaExpense + bagExpense + staffExpense + customExpensesTotal;

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

        private sealed class CustomExpenseEntry
        {
            public CustomExpenseEntry(string name, TextBox amountTextBox, TextBox descriptionTextBox)
            {
                Name = name;
                AmountTextBox = amountTextBox;
                DescriptionTextBox = descriptionTextBox;
            }

            public string Name { get; }
            public TextBox AmountTextBox { get; }
            public TextBox DescriptionTextBox { get; }
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
                var rows = GetExportRows(headers);
                ExportHelper.ExportPdf(filePath, rows, headers, GetExportTitle());
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
                "Değer",
                "Gider Açıklaması"
            };
        }

        private static string FormatExpenseForExport(string amount)
        {
            var value = amount?.Trim();
            if (string.IsNullOrWhiteSpace(value) || value.Contains('$'))
            {
                return value ?? string.Empty;
            }

            return $"{value} $";
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
            rows.Add(new Dictionary<string, string> { { "Bölüm", "GİDERLER" }, { "Açıklama", "Otel ve Yemek Mekke" }, { "Değer", FormatExpenseForExport(HotelExpenseTextBox.Text) }, { "Gider Açıklaması", HotelExpenseDescriptionTextBox.Text ?? "" } });
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Otel ve Yemek Medine" }, { "Değer", FormatExpenseForExport(HotelMedineExpenseTextBox.Text) }, { "Gider Açıklaması", HotelMedineExpenseDescriptionTextBox.Text ?? "" } });
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Uçak Bileti" }, { "Değer", FormatExpenseForExport(TicketExpenseTextBox.Text) }, { "Gider Açıklaması", TicketExpenseDescriptionTextBox.Text ?? "" } });
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Vize Masrafı" }, { "Değer", FormatExpenseForExport(VisaExpenseTextBox.Text) }, { "Gider Açıklaması", VisaExpenseDescriptionTextBox.Text ?? "" } });
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Çanta / Kitapçık" }, { "Değer", FormatExpenseForExport(BagExpenseTextBox.Text) }, { "Gider Açıklaması", BagExpenseDescriptionTextBox.Text ?? "" } });
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Eleman Gideri" }, { "Değer", FormatExpenseForExport(StaffExpenseTextBox.Text) }, { "Gider Açıklaması", StaffExpenseDescriptionTextBox.Text ?? "" } });
            foreach (var expense in _customExpenses)
            {
                rows.Add(new Dictionary<string, string>
                {
                    { "Bölüm", "" },
                    { "Açıklama", expense.Name },
                    { "Değer", FormatExpenseForExport(expense.AmountTextBox.Text) },
                    { "Gider Açıklaması", expense.DescriptionTextBox.Text ?? "" }
                });
            }

            // KAR / ZARAR ANALİZİ
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "" }, { "Değer", "" } }); // Boş satır
            rows.Add(new Dictionary<string, string> { { "Bölüm", "KAR / ZARAR ANALİZİ" }, { "Açıklama", "Toplam Maliyet" }, { "Değer", TotalExpenseTextBlock.Text } });
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Net Kâr" }, { "Değer", TotalProfitLossTextBlock.Text } });
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Tahsilata Göre" }, { "Değer", InstantProfitLossTextBlock.Text } });
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Kâr Marjı (Net Kâr)" }, { "Değer", ProfitMarginTextBlock.Text } });
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Kâr Oranı (Net Kâr)" }, { "Değer", ProfitRateTextBlock.Text } });
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Kâr Marjı (Tahsil.Göre)" }, { "Değer", InstantProfitMarginTextBlock.Text } });
            rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "Kâr Oranı (Tahsil.Göre)" }, { "Değer", InstantProfitRateTextBlock.Text } });

            if (_useCustomRates)
            {
                rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "" }, { "Değer", "" } });
                rows.Add(new Dictionary<string, string>
                {
                    { "__TABLE_HEADERS", "Kur Tablosu,1 Birimin TL Karşılığı" }
                });

                var customRates = new[]
                {
                    (Currency: "USD / TL", Rate: _customUsdRate),
                    (Currency: "EUR / TL", Rate: _customEurRate),
                    (Currency: "GBP / TL", Rate: _customGbpRate),
                    (Currency: "SAR / TL", Rate: _customSarRate)
                };

                foreach (var rate in customRates)
                {
                    rows.Add(new Dictionary<string, string>
                    {
                        { "Kur Tablosu", rate.Currency },
                        { "1 Birimin TL Karşılığı", rate.Rate.ToString("N4", CultureInfo.CurrentCulture) }
                    });
                }

                rows.Add(new Dictionary<string, string>
                {
                    { "Kur Tablosu", "Kur Kaynağı" },
                    { "1 Birimin TL Karşılığı", "Kullanıcı tarafından girilmiştir; TCMB kuru değildir." }
                });
            }

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
                    var selectedPaymentColumns = PaymentExportColumns
                        .Where(column => _paymentExportColumnVisibility.TryGetValue(column.Key, out var isVisible) && isVisible)
                        .ToList();

                    if (payments.Any() && selectedPaymentColumns.Any())
                    {
                        rows.Add(new Dictionary<string, string> { { "Bölüm", "" }, { "Açıklama", "" }, { "Değer", "" } });
                        // Her kişi yalnızca bir kez listelenir. Birden fazla ödeme varsa,
                        // ödeme alanları aynı hücrede satır satır gösterilir; böylece kişiye
                        // ait sabit bilgiler tekrar etmezken her ödemenin kendi para birimi korunur.
                        // Insert marker that ExportHelper recognizes to start a new table with these headers
                        rows.Add(new Dictionary<string, string>
                        {
                            { "__TABLE_HEADERS", string.Join(",", selectedPaymentColumns.Select(column => column.Header)) }
                        });

                        var paymentGroups = payments
                            .GroupBy(payment => payment.Registration?.Person != null
                                ? $"person:{payment.Registration.Person.Id}"
                                : $"registration:{payment.RegistrationId}")
                            .OrderBy(group => group.Min(payment => payment.CreatedAt));

                        var idx = 1;
                        foreach (var paymentGroup in paymentGroups)
                        {
                            var person = paymentGroup.First().Registration?.Person;
                            var paymentDates = new List<string>();
                            var paymentAmounts = new List<string>();
                            var paymentCurrencies = new List<string>();
                            var paymentMethods = new List<string>();
                            var paymentNotes = new List<string>();

                            foreach (var payment in paymentGroup.OrderBy(payment => payment.CreatedAt))
                            {
                                // OriginalCurrency ödeme esnasında girilen gerçek para birimidir.
                                // Eski/verisi eksik kayıtlar için kayıt para birimine geri düşülür.
                                var paymentCurrency = string.IsNullOrWhiteSpace(payment.OriginalCurrency)
                                    ? payment.Registration?.Currency ?? string.Empty
                                    : payment.OriginalCurrency.Trim();

                                paymentDates.Add(payment.CreatedAt.ToString("yyyy-MM-dd"));
                                paymentAmounts.Add(FormatAmount(payment.OriginalAmount, paymentCurrency));
                                paymentCurrencies.Add(paymentCurrency);
                                paymentMethods.Add(payment.PaymentMethod?.Trim() ?? string.Empty);
                                paymentNotes.Add(payment.Note?.Trim() ?? string.Empty);
                            }

                            var paymentRowValues = new Dictionary<string, string>
                            {
                                { "sequence", idx.ToString() },
                                { "firstName", (person?.FirstName ?? string.Empty).Trim() },
                                { "lastName", (person?.LastName ?? string.Empty).Trim() },
                                { "tcKimlik", (person?.TcKimlikNo ?? string.Empty).Trim() },
                                { "passportNumber", (person?.PassportNumber ?? string.Empty).Trim() },
                                { "paymentDate", string.Join(Environment.NewLine, paymentDates) },
                                { "paymentAmount", string.Join(Environment.NewLine, paymentAmounts) },
                                { "currency", string.Join(Environment.NewLine, paymentCurrencies) },
                                { "paymentMethod", string.Join(Environment.NewLine, paymentMethods) },
                                { "note", string.Join(Environment.NewLine, paymentNotes) }
                            };

                            rows.Add(selectedPaymentColumns.ToDictionary(
                                column => column.Header,
                                column => paymentRowValues[column.Key]));
                            idx++;
                        }
                    }
                }
            }
            catch { /* don't break export on payment fetch errors */ }

            return rows;
        }

    }
}

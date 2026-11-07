using System.Globalization;
using TOSLite.Mobile.Data;
using TOSLite.Mobile.Models;
using TOSLite.Mobile.Services;

namespace TOSLite.Mobile.Views;

public sealed class PaymentRegistrationOption
{
    public RegistrationRecord Registration { get; init; } = new();
    public string DisplayText => $"{Registration.PersonName} · {Registration.TourType} · {Registration.Period} · Kalan {Registration.RemainingDebt:N2} {Registration.Currency}";
}

public partial class PaymentsPage : ContentPage
{
    private readonly RegistrationRepository _registrations;
    private readonly PersonRepository _persons;
    private readonly SettingsRepository _settings;
    private readonly PaymentRepository _payments;
    private readonly ListExportService _export;
    private readonly DownloadFileService _downloads;
    private readonly List<RegistrationRecord> _allRegistrations = [];
    private RegistrationRecord? _selected;
    private string? _documentPath;

    public PaymentsPage(RegistrationRepository registrations, PersonRepository persons, SettingsRepository settings, PaymentRepository payments, ListExportService export, DownloadFileService downloads)
    {
        InitializeComponent();
        _registrations = registrations;
        _persons = persons;
        _settings = settings;
        _payments = payments;
        _export = export;
        _downloads = downloads;
        MobileTheme.Apply(this);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _payments.InitializeAsync();
            _allRegistrations.Clear();
            _allRegistrations.AddRange(await _registrations.GetAllAsync());
            CurrencyPicker.ItemsSource = (await _settings.GetOptionsAsync("Para Birimi", includeInactive: false)).Select(x => x.Value).ToList();
            MethodPicker.ItemsSource = (await _settings.GetOptionsAsync("Ödeme Yöntemi", includeInactive: false)).Select(x => x.Value).ToList();
            if (CurrencyPicker.Items.Count > 0) CurrencyPicker.SelectedIndex = 0;
            if (MethodPicker.Items.Count > 0) MethodPicker.SelectedIndex = 0;
            InitializeChartFilters();
            ApplySearch();
            await RefreshSummaryAsync();
        }
        catch (Exception ex) { await DisplayAlertAsync("Hata", $"Finans verileri yüklenemedi: {ex.Message}", "Tamam"); }
    }

    private void ApplySearch()
    {
        var query = SearchEntry.Text?.Trim() ?? string.Empty;
        var filtered = _allRegistrations.Where(x => string.IsNullOrWhiteSpace(query)
            || x.PersonName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || x.TourType.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || x.Period.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || x.GroupType.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
        RegistrationPicker.ItemsSource = filtered.Select(x => new PaymentRegistrationOption { Registration = x }).ToList();
        if (_selected != null)
            RegistrationPicker.SelectedItem = ((IEnumerable<PaymentRegistrationOption>)RegistrationPicker.ItemsSource).FirstOrDefault(x => x.Registration.Id == _selected.Id);
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => ApplySearch();

    private async void OnRegistrationChanged(object? sender, EventArgs e)
    {
        _selected = (RegistrationPicker.SelectedItem as PaymentRegistrationOption)?.Registration;
        await RefreshSelectedAsync();
    }

    private async Task RefreshSelectedAsync()
    {
        if (_selected == null)
        {
            AccountSummaryLabel.Text = "Cari hesap görüntülemek için tur kaydı seçin.";
            SetPaymentsEmptyMessage("Ödeme hareketlerini görmek için önce yukarıdaki arama alanında kişiyi bulun, ardından ödeme yapılacak tur kaydını seçin.");
            PaymentsCollection.ItemsSource = null;
            return;
        }
        _selected = await _registrations.GetByIdAsync(_selected.Id) ?? _selected;
        AccountSummaryLabel.Text = $"Toplam {_selected.TotalPrice:N2} {_selected.Currency} · Tahsil {_selected.PaidAmount:N2} · Kalan {_selected.RemainingDebt:N2}";
        var payments = (await _payments.GetForRegistrationAsync(_selected.Id)).Where(x => !x.IsDeleted).ToList();
        SetPaymentsEmptyMessage("Seçilen tur kaydı için henüz ödeme hareketi yok. Yeni tahsilat veya iade eklemek için aşağıdaki formu kullanabilirsiniz.");
        PaymentsCollection.ItemsSource = payments;
    }

    private void SetPaymentsEmptyMessage(string message)
    {
        if (PaymentsCollection.EmptyView is Label emptyLabel)
            emptyLabel.Text = message;
    }

    private async Task RefreshSummaryAsync()
    {
        _allRegistrations.Clear();
        _allRegistrations.AddRange(await _registrations.GetAllAsync());
        BuildCurrencySummary(GetChartRegistrations());
        ApplySearch();
    }

    private void InitializeChartFilters()
    {
        SetChartFilterItems(ChartPeriodPicker, _allRegistrations.Select(x => x.Period), "Tüm dönemler");
        SetChartFilterItems(ChartTourPicker, _allRegistrations.Select(x => x.TourType), "Tüm tur türleri");
        SetChartFilterItems(ChartGroupPicker, _allRegistrations.Select(x => x.GroupType), "Tüm grup türleri");
        SetChartFilterItems(ChartDaysPicker, _allRegistrations.Select(x => x.DaysCount), "Tüm süreler");
    }

    private static void SetChartFilterItems(Picker picker, IEnumerable<string> values, string allLabel)
    {
        var options = new[] { allLabel }.Concat(values.Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)).ToList();
        picker.ItemsSource = options;
        picker.SelectedIndex = 0;
    }

    private IEnumerable<RegistrationRecord> GetChartRegistrations()
    {
        var period = GetChartFilter(ChartPeriodPicker, "Tüm dönemler");
        var tour = GetChartFilter(ChartTourPicker, "Tüm tur türleri");
        var group = GetChartFilter(ChartGroupPicker, "Tüm grup türleri");
        var days = GetChartFilter(ChartDaysPicker, "Tüm süreler");
        return _allRegistrations.Where(x => x.IsActive
            && (period == "Tüm dönemler" || x.Period.Equals(period, StringComparison.CurrentCultureIgnoreCase))
            && (tour == "Tüm tur türleri" || x.TourType.Equals(tour, StringComparison.CurrentCultureIgnoreCase))
            && (group == "Tüm grup türleri" || x.GroupType.Equals(group, StringComparison.CurrentCultureIgnoreCase))
            && (days == "Tüm süreler" || x.DaysCount.Equals(days, StringComparison.CurrentCultureIgnoreCase)));
    }

    private static string GetChartFilter(Picker picker, string allLabel) => picker.SelectedItem?.ToString() ?? allLabel;

    private void OnToggleChartFiltersClicked(object? sender, EventArgs e) => ChartFiltersPanel.IsVisible = !ChartFiltersPanel.IsVisible;

    private void OnChartFilterChanged(object? sender, EventArgs e)
    {
        if (_allRegistrations.Count > 0)
            BuildCurrencySummary(GetChartRegistrations());
    }

    private void BuildCurrencySummary(IEnumerable<RegistrationRecord> registrations)
    {
        CurrencySummaryLayout.Children.Clear();
        var groups = registrations
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Currency) ? "Para birimi yok" : x.Currency.Trim())
            .OrderBy(group => string.Equals(group.Key, "Seçilmedi", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var sectionColors = new[] { "SectionBlue", "SectionViolet", "SectionTeal", "SectionAmber", "SectionRose", "SectionCyan", "SectionLime", "SectionOrange" };
        var sectionIndex = 0;
        foreach (var group in groups)
        {
            var collected = group.Sum(x => x.PaidAmount);
            var remaining = group.Sum(x => x.RemainingDebt);
            var total = Math.Max(0, collected) + Math.Max(0, remaining);
            var collectedRatio = total == 0 ? 0 : (double)(Math.Max(0, collected) / total);
            var remainingRatio = total == 0 ? 0 : (double)(Math.Max(0, remaining) / total);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 10 };
            row.Children.Add(CreateDonutCard("Tahsilat", collected, collectedRatio, group.Key, "Success"));
            var remainingCard = CreateDonutCard("Kalan", remaining, remainingRatio, group.Key, remaining < 0 ? "Warning" : "Danger");
            Grid.SetColumn(remainingCard, 1);
            row.Children.Add(remainingCard);
            var sectionColor = sectionColors[sectionIndex++ % sectionColors.Length];
            var currencyCard = new Border
            {
                Stroke = (Color)Application.Current!.Resources[$"{sectionColor}Border"],
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 },
                BackgroundColor = (Color)Application.Current.Resources[sectionColor],
                Padding = 16,
                Content = new VerticalStackLayout
                {
                    Spacing = 8,
                    Children = { new Label { Text = group.Key, FontAttributes = FontAttributes.Bold, FontSize = 15, TextColor = (Color)Application.Current!.Resources["TextPrimary"] }, row }
                }
            };
            CurrencySummaryLayout.Children.Add(currencyCard);
        }
    }

    private static Border CreateDonutCard(string title, decimal amount, double ratio, string currency, string colorResource)
    {
        var color = (Color)Application.Current!.Resources[colorResource];
        var trackColor = (Color)Application.Current.Resources["BorderColor"];
        return new Border
        {
            Stroke = trackColor,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            BackgroundColor = (Color)Application.Current.Resources["SurfaceAlt"],
            Padding = 10,
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                HorizontalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label { Text = title, FontAttributes = FontAttributes.Bold, FontSize = 13, TextColor = color, HorizontalTextAlignment = TextAlignment.Center },
                    new GraphicsView { HeightRequest = 76, WidthRequest = 76, Drawable = new DonutDrawable(ratio, color, trackColor), HorizontalOptions = LayoutOptions.Center },
                    new Label { Text = $"{amount:N2} {currency}", FontAttributes = FontAttributes.Bold, FontSize = 13, TextColor = (Color)Application.Current.Resources["TextPrimary"], HorizontalTextAlignment = TextAlignment.Center },
                    new Label { Text = $"Toplamın %{ratio * 100:N1}’i", FontSize = 11, TextColor = (Color)Application.Current.Resources["TextSecondary"], HorizontalTextAlignment = TextAlignment.Center }
                }
            }
        };
    }

    private sealed class DonutDrawable(double ratio, Color valueColor, Color trackColor) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var diameter = Math.Min(dirtyRect.Width, dirtyRect.Height) - 8;
            var bounds = new RectF((dirtyRect.Width - diameter) / 2, (dirtyRect.Height - diameter) / 2, diameter, diameter);
            canvas.StrokeSize = 9;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeColor = trackColor;
            canvas.DrawEllipse(bounds);
            if (ratio <= 0) return;
            canvas.StrokeColor = valueColor;
            var normalizedRatio = Math.Clamp(ratio, 0, 1);
            if (normalizedRatio >= 0.999)
            {
                canvas.DrawEllipse(bounds);
                return;
            }
            var startAngle = -90f;
            var endAngle = startAngle + 360f * (float)normalizedRatio;
            canvas.DrawArc(bounds, startAngle, endAngle, false, false);
        }
    }

    private async void OnDocumentClicked(object? sender, EventArgs e)
    {
        try
        {
            var file = await FilePicker.Default.PickAsync();
            if (file == null) return;
            var localPath = Path.Combine(FileSystem.AppDataDirectory, "payment-documents", $"{Guid.NewGuid():N}_{Path.GetFileName(file.FileName)}");
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            await using var source = await file.OpenReadAsync();
            await using var destination = File.Create(localPath);
            await source.CopyToAsync(destination);
            _documentPath = localPath;
            DocumentLabel.Text = file.FileName;
        }
        catch (Exception ex) { await DisplayAlertAsync("Dekont seçilemedi", ex.Message, "Tamam"); }
    }

    private async void OnSavePaymentClicked(object? sender, EventArgs e)
    {
        if (_selected == null) { await DisplayAlertAsync("Kayıt seçin", "Ödeme hareketi için tur kaydı seçilmelidir.", "Tamam"); return; }
        if (!decimal.TryParse(AmountEntry.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var amount) && !decimal.TryParse(AmountEntry.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out amount))
        { await DisplayAlertAsync("Tutar geçersiz", "Ödeme tutarını kontrol edin.", "Tamam"); return; }
        if (amount <= 0) { await DisplayAlertAsync("Tutar geçersiz", "Tutar sıfırdan büyük olmalıdır.", "Tamam"); return; }
        var selectedCurrency = CurrencyPicker.SelectedItem?.ToString() ?? _selected.Currency;
        var amountCurrency = CurrencyCode(selectedCurrency);
        var registrationCurrency = CurrencyCode(_selected.Currency);
        decimal? rate = null;
        var converted = amount;
        if (!string.Equals(amountCurrency, registrationCurrency, StringComparison.OrdinalIgnoreCase))
        {
            if (!decimal.TryParse(RateEntry.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var parsedRate) && !decimal.TryParse(RateEntry.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out parsedRate) || parsedRate <= 0)
            { await DisplayAlertAsync("Kur gerekli", "Farklı para birimleri için geçerli dönüşüm kuru girin.", "Tamam"); return; }
            rate = parsedRate;
            converted = amount * parsedRate;
        }
        var refund = RefundSwitch.IsToggled;
        var signedAmount = refund ? -amount : amount;
        var signedConverted = refund ? -converted : converted;
        try
        {
            await _payments.SaveAsync(new PaymentRecord
            {
                RegistrationId = _selected.Id, CreatedAt = DateTime.UtcNow, OriginalAmount = signedAmount,
                OriginalCurrency = selectedCurrency, AmountInRegistrationCurrency = signedConverted, Rate = rate,
                Note = NoteEntry.Text?.Trim() ?? string.Empty, PaymentMethod = MethodPicker.SelectedItem?.ToString() ?? string.Empty,
                UploadedDocumentPath = _documentPath ?? string.Empty
            });
            AmountEntry.Text = string.Empty; NoteEntry.Text = string.Empty; RateEntry.Text = string.Empty;
            _documentPath = null; DocumentLabel.Text = "Dekont eklenmedi"; RefundSwitch.IsToggled = false;
            await RefreshSelectedAsync(); await RefreshSummaryAsync();
        }
        catch (Exception ex) { await DisplayAlertAsync("Ödeme kaydedilemedi", ex.Message, "Tamam"); }
    }

    private async void OnDeletePaymentClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not PaymentRecord payment) return;
        if (!await DisplayAlertAsync("Ödeme hareketini sil", "Ödeme kaydı silinecek ve cari toplamlar yeniden hesaplanacak.", "Sil", "Vazgeç")) return;
        await _payments.DeleteAsync(payment.Id);
        await RefreshSelectedAsync(); await RefreshSummaryAsync();
    }

    private async void OnExportClicked(object? sender, EventArgs e)
    {
        try
        {
            var format = await DisplayActionSheetAsync("Finans özetini hangi formatta indirelim?", "Vazgeç", null, "Word (.docx)", "Excel (.xlsx)", "PDF (.pdf)");
            if (string.IsNullOrWhiteSpace(format) || format == "Vazgeç") return;

            var filtered = GetExportRegistrations();
            if (filtered.Count == 0)
            {
                await DisplayAlertAsync("Kayıt bulunamadı", "Seçilen arama veya kayda uygun finans kaydı yok.", "Tamam");
                return;
            }
            var headers = new[] { "Kişi", "Tur", "Dönem", "Grup", "Gün", "Toplam", "Para Birimi", "Tahsil", "Kalan" };
            var rows = filtered.Select(registration => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
            {
                ["Kişi"] = registration.PersonName, ["Tur"] = registration.TourType, ["Dönem"] = registration.Period,
                ["Grup"] = registration.GroupType, ["Gün"] = registration.DaysCount,
                ["Toplam"] = registration.TotalPrice.ToString("N2", CultureInfo.CurrentCulture), ["Para Birimi"] = registration.Currency,
                ["Tahsil"] = registration.PaidAmount.ToString("N2", CultureInfo.CurrentCulture), ["Kalan"] = registration.RemainingDebt.ToString("N2", CultureInfo.CurrentCulture)
            }).ToList();
            var (extension, contentType, bytes) = CreateExport(format, "Finans Özeti", headers, rows);
            var fileName = $"Finans_Ozeti_{DateTime.Now:yyyyMMdd_HHmm}{extension}";
            await _downloads.SaveAsync(fileName, contentType, bytes);
            await DisplayAlertAsync("İndirme tamamlandı", $"Finans özeti İndirilenler klasörüne kaydedildi:\n{fileName}", "Tamam");
        }
        catch (Exception ex) { await DisplayAlertAsync("Dışa aktarma başarısız", ex.Message, "Tamam"); }
    }

    private async void OnExportPaymentsClicked(object? sender, EventArgs e)
    {
        if (_selected == null)
        {
            await DisplayAlertAsync("Kayıt seçin", "Ödeme hareketlerini indirmek için önce üst bölümden tur kaydı seçin.", "Tamam");
            return;
        }
        try
        {
            var format = await DisplayActionSheetAsync("Ödeme hareketlerini hangi formatta indirelim?", "Vazgeç", null, "Word (.docx)", "Excel (.xlsx)", "PDF (.pdf)");
            if (string.IsNullOrWhiteSpace(format) || format == "Vazgeç") return;
            var payments = (await _payments.GetForRegistrationAsync(_selected.Id)).Where(x => !x.IsDeleted).OrderByDescending(x => x.CreatedAt).ToList();
            if (payments.Count == 0)
            {
                await DisplayAlertAsync("Hareket bulunamadı", "Seçilen tur kaydında indirilecek ödeme hareketi yok.", "Tamam");
                return;
            }
            var headers = new[] { "Tarih", "Miktar (Orj)", "Dönüştürülmüş", "Kur", "Ödeme Yöntemi", "Açıklama" };
            var rows = payments.Select(payment => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
            {
                ["Tarih"] = payment.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture),
                ["Miktar (Orj)"] = $"{payment.OriginalAmount:N2} {payment.OriginalCurrency}",
                ["Dönüştürülmüş"] = $"{payment.AmountInRegistrationCurrency:N2} {_selected.Currency}",
                ["Kur"] = payment.Rate?.ToString("N4", CultureInfo.CurrentCulture) ?? string.Empty,
                ["Ödeme Yöntemi"] = payment.PaymentMethod,
                ["Açıklama"] = payment.Note
            }).ToList();
            var title = $"Ödeme Hareketleri - {_selected.PersonName}";
            var (extension, contentType, bytes) = CreateExport(format, title, headers, rows);
            var fileName = $"Odeme_Hareketleri_{SafeFileName(_selected.PersonName)}_{DateTime.Now:yyyyMMdd_HHmm}{extension}";
            await _downloads.SaveAsync(fileName, contentType, bytes);
            await DisplayAlertAsync("İndirme tamamlandı", $"Ödeme hareketleri İndirilenler klasörüne kaydedildi:\n{fileName}", "Tamam");
        }
        catch (Exception ex) { await DisplayAlertAsync("Dışa aktarma başarısız", ex.Message, "Tamam"); }
    }

    private static string SafeFileName(string value)
    {
        foreach (var character in Path.GetInvalidFileNameChars())
            value = value.Replace(character, '-');
        return string.IsNullOrWhiteSpace(value) ? "Kisi" : value.Trim();
    }

    private List<RegistrationRecord> GetExportRegistrations()
    {
        if (_selected != null)
            return _allRegistrations.Where(x => x.Id == _selected.Id).ToList();
        var query = SearchEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
            return _allRegistrations.ToList();
        return _allRegistrations.Where(x =>
            x.PersonName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || x.TourType.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || x.Period.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || x.GroupType.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    private (string Extension, string ContentType, byte[] Bytes) CreateExport(string format, string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyDictionary<string, string>> rows) => format switch
    {
        "Word (.docx)" => (".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", _export.CreateWord(title, headers, rows)),
        "Excel (.xlsx)" => (".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", _export.CreateExcel(title, headers, rows)),
        _ => (".pdf", "application/pdf", _export.CreatePdf(title, headers, rows))
    };

    private static string CurrencyCode(string value)
    {
        if (value.Contains("euro", StringComparison.OrdinalIgnoreCase) || value.Contains("EUR", StringComparison.OrdinalIgnoreCase) || value.Contains('€')) return "EUR";
        if (value.Contains("dolar", StringComparison.OrdinalIgnoreCase) || value.Contains("USD", StringComparison.OrdinalIgnoreCase) || value.Contains('$')) return "USD";
        if (value.Contains("sterlin", StringComparison.OrdinalIgnoreCase) || value.Contains("GBP", StringComparison.OrdinalIgnoreCase) || value.Contains('£')) return "GBP";
        if (value.Contains("riyali", StringComparison.OrdinalIgnoreCase) || value.Contains("SAR", StringComparison.OrdinalIgnoreCase)) return "SAR";
        if (value.Contains("Türk", StringComparison.OrdinalIgnoreCase) || value.Contains("TRY", StringComparison.OrdinalIgnoreCase) || value.Contains("TL", StringComparison.OrdinalIgnoreCase) || value.Contains('₺')) return "TRY";
        return value.Trim();
    }
}

using TOSLite.Mobile.Data;
using TOSLite.Mobile.Models;
using TOSLite.Mobile.Services;

namespace TOSLite.Mobile.Views;

public partial class ListsPage : ContentPage
{
    private enum ListKind
    {
        Phone,
        Health,
        TourGeneral,
        Visa,
        Passport,
        Ticket,
        Transfer,
        Airline,
        Reference,
        Rooms,
        Payments,
        Cargo
    }

    private readonly PersonRepository _personRepository;
    private readonly RegistrationRepository _registrationRepository;
    private readonly RoomAllocationRepository _roomAllocationRepository;
    private readonly ListExportService _listExportService;
    private readonly DownloadFileService _downloadFileService;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private List<PersonRecord> _people = [];
    private List<RegistrationRecord> _registrations = [];
    private Dictionary<int, PersonRecord> _peopleById = [];
    private ListKind? _selectedKind;
    private bool _updatingFilters;
    private bool _initialized;
    private List<ListDisplayRow> _visibleRows = [];

    public ListsPage(
        PersonRepository personRepository,
        RegistrationRepository registrationRepository,
        RoomAllocationRepository roomAllocationRepository,
        ListExportService listExportService,
        DownloadFileService downloadFileService)
    {
        InitializeComponent();
        _personRepository = personRepository;
        _registrationRepository = registrationRepository;
        _roomAllocationRepository = roomAllocationRepository;
        _listExportService = listExportService;
        _downloadFileService = downloadFileService;
        MobileTheme.Apply(this);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await EnsureDataLoadedAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Hata", $"Liste verileri yüklenemedi: {ex.Message}", "Tamam");
        }
    }

    private async Task EnsureDataLoadedAsync()
    {
        if (_initialized) return;
        await _initializationGate.WaitAsync();
        try
        {
            if (_initialized) return;
            await _registrationRepository.InitializeAsync();
            await _roomAllocationRepository.InitializeAsync();
            _people = (await _personRepository.GetAllAsync()).Where(person => !person.IsDeleted).ToList();
            _peopleById = _people.ToDictionary(person => person.Id);
            _registrations = (await _registrationRepository.GetAllAsync())
                .Where(registration => registration.IsActive && _peopleById.ContainsKey(registration.PersonId))
                .OrderBy(registration => registration.Id)
                .ToList();
            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private async void OnListTypeClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string kindName }
            || !Enum.TryParse(kindName, out ListKind kind)) return;

        try
        {
            await EnsureDataLoadedAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Hata", $"Liste verileri yüklenemedi: {ex.Message}", "Tamam");
            return;
        }

        _selectedKind = kind;
        var isPersonList = kind is ListKind.Phone or ListKind.Health;
        var isRoomList = kind == ListKind.Rooms;
        IReadOnlyList<string> hotels = isRoomList
            ? await _roomAllocationRepository.GetHotelsAsync()
            : Array.Empty<string>();

        _updatingFilters = true;
        try
        {
            DashboardPanel.IsVisible = false;
            ResultsPanel.IsVisible = true;
            ResultsTitle.Text = GetTitle(kind);
            SearchEntry.Text = string.Empty;
            CriteriaFilters.IsVisible = !isPersonList;
            HotelFilterPanel.IsVisible = isRoomList;
            ExtraFilterPanel.IsVisible = kind is ListKind.Visa or ListKind.Ticket or ListKind.Airline or ListKind.Cargo;
            SecondaryFilterPanel.IsVisible = kind == ListKind.Airline;
            SetPickerItems(TourTypePicker, _registrations.Select(item => item.TourType));
            SetPickerItems(PeriodPicker, _registrations.Select(item => item.Period));
            SetPickerItems(GroupTypePicker, _registrations.Select(item => item.GroupType));
            SetPickerItems(DaysPicker, _registrations.Select(item => item.DaysCount));
            HotelPicker.ItemsSource = hotels.ToList();
            HotelPicker.SelectedIndex = -1;
            ConfigureExtraFilters(kind);
        }
        finally
        {
            _updatingFilters = false;
        }

        await ApplyFiltersAsync();
    }

    private void ConfigureExtraFilters(ListKind kind)
    {
        switch (kind)
        {
            case ListKind.Visa:
                ExtraFilterLabel.Text = "Vize talebi";
                SetPickerItems(ExtraFilterPicker, _registrations.Select(item => item.VisaRequest));
                break;
            case ListKind.Ticket:
            case ListKind.Airline:
                ExtraFilterLabel.Text = "Bilet talebi";
                SetPickerItems(ExtraFilterPicker, _registrations.Select(item => item.TicketRequest));
                break;
            case ListKind.Cargo:
                ExtraFilterLabel.Text = "Kargo talebi";
                SetPickerItems(ExtraFilterPicker, _registrations.Select(item => item.CargoRequest));
                break;
        }

        if (kind == ListKind.Airline)
        {
            SecondaryFilterLabel.Text = "Aktarma";
            SetPickerItems(SecondaryFilterPicker, _registrations.Select(item => item.HasTransfer));
        }
    }

    private static void SetPickerItems(Picker picker, IEnumerable<string> values)
    {
        picker.ItemsSource = new[] { "Tümü" }
            .Concat(values.Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase))
            .ToList();
        picker.SelectedIndex = 0;
    }

    private async void OnFilterChanged(object? sender, EventArgs e)
    {
        if (_updatingFilters || _selectedKind == null) return;
        await ApplyFiltersAsync();
    }

    private async Task ApplyFiltersAsync()
    {
        if (_selectedKind == null) return;

        if (_selectedKind == ListKind.Rooms)
        {
            await LoadRoomRowsAsync();
            return;
        }

        IEnumerable<ListDisplayRow> rows = _selectedKind switch
        {
            ListKind.Phone => _people.Select(CreatePhoneRow),
            ListKind.Health => _people.Select(CreateHealthRow),
            _ => CreateRegistrationRows(_selectedKind.Value)
        };

        var query = SearchEntry.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(query))
        {
            rows = rows.Where(row => Contains(row.Title, query)
                || Contains(row.Subtitle, query)
                || Contains(row.Detail, query)
                || Contains(row.Context, query));
        }

        var results = rows.ToList();
        _visibleRows = results;
        ResultsCollection.ItemsSource = results;
        StatusLabel.Text = $"{results.Count} kayıt";
    }

    private IEnumerable<ListDisplayRow> CreateRegistrationRows(ListKind kind)
    {
        var tourType = SelectedValue(TourTypePicker);
        var period = SelectedValue(PeriodPicker);
        var groupType = SelectedValue(GroupTypePicker);
        var days = SelectedValue(DaysPicker);
        var extra = SelectedValue(ExtraFilterPicker);
        var secondary = SelectedValue(SecondaryFilterPicker);

        var registrations = _registrations.Where(registration =>
            Matches(registration.TourType, tourType)
            && Matches(registration.Period, period)
            && Matches(registration.GroupType, groupType)
            && Matches(registration.DaysCount, days));

        registrations = kind switch
        {
            ListKind.Visa => registrations.Where(registration => Matches(registration.VisaRequest, extra)),
            ListKind.Ticket => registrations.Where(registration => Matches(registration.TicketRequest, extra)),
            ListKind.Airline => registrations
                .Where(registration => Matches(registration.TicketRequest, extra))
                .Where(registration => Matches(registration.HasTransfer, secondary)),
            ListKind.Cargo => registrations.Where(registration => Matches(registration.CargoRequest, extra)),
            _ => registrations
        };

        return registrations.Select(registration => CreateRegistrationRow(registration, kind));
    }

    private async Task LoadRoomRowsAsync()
    {
        var hotel = HotelPicker.SelectedItem as string;
        var tourType = SelectedValue(TourTypePicker);
        var period = SelectedValue(PeriodPicker);
        var groupType = SelectedValue(GroupTypePicker);
        var days = SelectedValue(DaysPicker);
        if (string.IsNullOrWhiteSpace(hotel) || tourType == null || period == null || groupType == null || days == null)
        {
            _visibleRows = [];
            ResultsCollection.ItemsSource = Array.Empty<ListDisplayRow>();
            StatusLabel.Text = "Oda listesi için otel, tur, dönem, grup ve gün kriterlerini seçin.";
            return;
        }

        try
        {
            var criteria = new RoomingCriteria
            {
                HotelName = hotel,
                TourType = tourType,
                Period = period,
                GroupType = groupType,
                DaysCount = days
            };
            var rooms = await _roomAllocationRepository.GetAllocationsAsync(criteria);
            var rows = rooms.Select(room => new ListDisplayRow
            {
                Title = $"{room.HotelName} · Oda {room.RoomNumber}",
                Subtitle = $"{room.RoomType} · {room.TourType} · {room.Period}",
                Detail = room.Occupants.Count == 0 ? "Henüz yolcu yok" : string.Join(", ", room.Occupants.Select(occupant => occupant.PersonName)),
                Context = $"{room.GroupType} · {room.DaysCount} gün · {room.CheckInDate} – {room.CheckOutDate}",
                ExportValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Oda No"] = string.Empty,
                    ["Oda Türü"] = room.RoomType,
                    ["Kapasite"] = room.Capacity.ToString(),
                    ["Yerleşen Kişi Sayısı"] = room.Occupants.Count.ToString(),
                    ["Yerleşen Kişiler"] = string.Join("\n", room.Occupants.Select((occupant, index) => $"{index + 1}. {occupant.PersonName}")),
                    ["Connection"] = room.IsConnection ? "Evet" : "Hayır",
                    ["__HotelName"] = room.HotelName,
                    ["__CheckInDate"] = FormatExportDate(room.CheckInDate),
                    ["__CheckOutDate"] = FormatExportDate(room.CheckOutDate)
                }
            }).ToList();
            var filteredRows = FilterRows(rows);
            _visibleRows = filteredRows;
            ResultsCollection.ItemsSource = filteredRows;
            StatusLabel.Text = $"{filteredRows.Count} oda";
        }
        catch (Exception ex)
        {
            _visibleRows = [];
            ResultsCollection.ItemsSource = Array.Empty<ListDisplayRow>();
            StatusLabel.Text = "Oda listesi yüklenemedi.";
            await DisplayAlertAsync("Hata", ex.Message, "Tamam");
        }
    }

    private async void OnExportClicked(object? sender, EventArgs e)
    {
        if (_selectedKind == null || _visibleRows.Count == 0)
        {
            await DisplayAlertAsync("Dışa Aktar", "Dışa aktarılacak kayıt bulunamadı.", "Tamam");
            return;
        }

        var format = await DisplayActionSheetAsync("Dosya biçimini seçin", "Vazgeç", null, "Word (.docx)", "Excel (.xlsx)", "PDF (.pdf)");
        if (string.IsNullOrWhiteSpace(format) || format == "Vazgeç") return;

        try
        {
            var kind = _selectedKind.Value;
            var rows = _visibleRows.Select(row => (IReadOnlyDictionary<string, string>)row.ExportValues).ToList();
            var headers = _visibleRows[0].ExportValues.Keys
                .Where(header => !header.StartsWith("__", StringComparison.Ordinal))
                .ToList();
            if (_selectedKind == ListKind.Airline)
            {
                headers = headers.Select(TransformForAirline).ToList();
                rows = _visibleRows.Select(row => (IReadOnlyDictionary<string, string>)row.ExportValues
                    .Where(pair => !pair.Key.StartsWith("__", StringComparison.Ordinal))
                    .ToDictionary(pair => TransformForAirline(pair.Key), pair => TransformForAirline(pair.Value), StringComparer.OrdinalIgnoreCase)).ToList();
            }

            var options = CreateExportOptions(kind);
            var exportTitle = BuildExportTitle(kind, options);
            var fileName = BuildExportBaseFileName(kind, options);
            var (extension, contentType, bytes) = format switch
            {
                "Word (.docx)" => (".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", _listExportService.CreateWord(exportTitle, headers, rows, options)),
                "Excel (.xlsx)" => (".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", _listExportService.CreateExcel(exportTitle, headers, rows, options)),
                _ => (".pdf", "application/pdf", _listExportService.CreatePdf(exportTitle, headers, rows, options))
            };

            var fullFileName = SafeFileName(fileName, kind == ListKind.Rooms ? '_' : '-') + extension;
            await _downloadFileService.SaveAsync(fullFileName, contentType, bytes);
            await DisplayAlertAsync("İndirme tamamlandı", $"Dosya İndirilenler klasörüne kaydedildi:\n{fullFileName}", "Tamam");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Dışa Aktarma Hatası", ex.Message, "Tamam");
        }
    }

    private static string TransformForAirline(string value) => value
        .Replace("İ", "I").Replace("ı", "i").Replace("Ş", "S").Replace("ş", "s")
        .Replace("Ğ", "G").Replace("ğ", "g").Replace("Ü", "U").Replace("ü", "u")
        .Replace("Ö", "O").Replace("ö", "o").Replace("Ç", "C").Replace("ç", "c")
        .ToUpperInvariant();

    private ListExportOptions CreateExportOptions(ListKind kind)
    {
        var firstRoom = kind == ListKind.Rooms ? _visibleRows[0].ExportValues : null;
        var roomTypes = kind == ListKind.Rooms
            ? _visibleRows.Select(row => row.ExportValues.TryGetValue("Oda Türü", out var roomType) ? roomType : string.Empty)
                .Where(roomType => !string.IsNullOrWhiteSpace(roomType))
                .GroupBy(roomType => roomType.Trim(), StringComparer.CurrentCultureIgnoreCase)
                .Select(group => new { RoomType = group.Key, Count = group.Count() })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => RoomTypeSortOrder(item.RoomType))
                .ToList()
            : [];
        var roomSummary = string.Join(", ", roomTypes.Select(item => $"{item.Count} {item.RoomType}"));
        return new ListExportOptions
        {
            UseDesktopTemplate = true,
            IsRoomAllocation = kind == ListKind.Rooms,
            HotelName = firstRoom?.GetValueOrDefault("__HotelName") ?? string.Empty,
            CheckInDate = firstRoom?.GetValueOrDefault("__CheckInDate") ?? string.Empty,
            CheckOutDate = firstRoom?.GetValueOrDefault("__CheckOutDate") ?? string.Empty,
            TourType = SelectedValue(TourTypePicker) ?? string.Empty,
            Period = SelectedValue(PeriodPicker) ?? string.Empty,
            GroupType = SelectedValue(GroupTypePicker) ?? string.Empty,
            DaysCount = SelectedValue(DaysPicker) ?? string.Empty,
            Footers = kind == ListKind.Rooms ? [$"TOPLAM ODA : {_visibleRows.Count}", roomSummary] : []
        };
    }

    private string BuildExportTitle(ListKind kind, ListExportOptions options)
    {
        if (kind == ListKind.Rooms)
        {
            var baseName = BuildExportBaseFileName(kind, options);
            return string.Join("\n", new[] { baseName, options.HotelName, $"Giriş: {options.CheckInDate}", $"Çıkış: {options.CheckOutDate}" });
        }

        var parts = new List<string> { GetDesktopModeName(kind) + " Liste" };
        if (!string.IsNullOrWhiteSpace(options.TourType)) parts.Add(options.TourType);
        if (!string.IsNullOrWhiteSpace(options.Period)) parts.Add(options.Period);
        if (!string.IsNullOrWhiteSpace(options.GroupType)) parts.Add(options.GroupType);
        if (!string.IsNullOrWhiteSpace(options.DaysCount)) parts.Add(options.DaysCount + " Günlük");
        return string.Join("\n", parts);
    }

    private string BuildExportBaseFileName(ListKind kind, ListExportOptions options)
    {
        if (kind == ListKind.Rooms)
            return string.Join(" - ", new[]
            {
                options.TourType,
                options.Period,
                string.Join(" ", new[] { options.GroupType, options.DaysCount }.Where(value => !string.IsNullOrWhiteSpace(value))),
                options.HotelName,
                "Otel Odalama Listesi"
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

        var parts = new List<string> { GetDesktopModeName(kind) + " Liste" };
        if (!string.IsNullOrWhiteSpace(options.TourType)) parts.Add(options.TourType);
        if (!string.IsNullOrWhiteSpace(options.Period)) parts.Add(options.Period);
        if (!string.IsNullOrWhiteSpace(options.DaysCount)) parts.Add(options.DaysCount + " Günlük");
        if (!string.IsNullOrWhiteSpace(options.GroupType)) parts.Add(options.GroupType);
        var query = SearchEntry.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(query)) parts.Add(query);
        return string.Join(" - ", parts);
    }

    private static string GetDesktopModeName(ListKind kind) => kind switch
    {
        ListKind.Phone => "Telefon",
        ListKind.Health => "Sağlık",
        ListKind.TourGeneral => "Genel",
        ListKind.Visa => "Vize",
        ListKind.Passport => "Pasaport",
        ListKind.Ticket => "Bilet",
        ListKind.Transfer => "Aktarma",
        ListKind.Airline => "Havayolu",
        ListKind.Reference => "Referans",
        ListKind.Payments => "Ödeme Listesi",
        ListKind.Cargo => "Kargo Listesi",
        _ => "Liste"
    };

    private static string SafeFileName(string value, char replacement)
    {
        foreach (var character in "<>:\"/\\|?*".Concat(Enumerable.Range(0, 32).Select(value => (char)value)))
            value = value.Replace(character, replacement);
        return value.Trim();
    }

    private static string FormatExportDate(string value) => DateTime.TryParse(value, out var date)
        ? date.ToString("dd.MM.yyyy")
        : string.IsNullOrWhiteSpace(value) ? "-" : value;

    private static int RoomTypeSortOrder(string roomType)
    {
        var normalized = roomType.Trim().ToUpperInvariant();
        if (normalized.Contains("SINGLE") || normalized.Contains("SNGL")) return 0;
        if (normalized.Contains("DBL")) return 1;
        if (normalized.Contains("TRPL") || normalized.Contains("TRIPLE") || normalized.Contains("TRİPLE")) return 2;
        if (normalized.Contains("TWIN")) return 3;
        if (normalized.Contains("QUAD")) return 4;
        if (normalized.Contains("QUINT")) return 5;
        return 50;
    }

    private List<ListDisplayRow> FilterRows(IEnumerable<ListDisplayRow> rows)
    {
        var query = SearchEntry.Text?.Trim();
        return string.IsNullOrWhiteSpace(query)
            ? rows.ToList()
            : rows.Where(row => Contains(row.Title, query)
                || Contains(row.Subtitle, query)
                || Contains(row.Detail, query)
                || Contains(row.Context, query)).ToList();
    }

    private ListDisplayRow CreateRegistrationRow(RegistrationRecord registration, ListKind kind)
    {
        _peopleById.TryGetValue(registration.PersonId, out var person);
        var contact = person?.PhoneNumber ?? string.Empty;
        var criteria = $"{registration.TourType} · {registration.Period} · {registration.GroupType} · {registration.DaysCount}";
        var row = new ListDisplayRow { Title = registration.PersonName, Context = criteria };
        var firstName = person?.FirstName ?? registration.PersonName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        var lastName = person?.LastName ?? string.Join(" ", registration.PersonName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1));
        var commonValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ad"] = firstName,
            ["Soyad"] = lastName,
            ["TC Kimlik"] = person?.TcKimlikNo ?? string.Empty,
            ["Telefon numarası"] = contact,
            ["Adres"] = person?.Address ?? string.Empty,
            ["Pasaport tipi"] = person?.PassportType ?? string.Empty,
            ["Pasaport numarası"] = person?.PassportNumber ?? string.Empty,
            ["Pasaport Başlangıç tarihi"] = DateText(person?.PassportStartDate),
            ["Pasaport bitiş tarihi"] = DateText(person?.PassportEndDate),
            ["Vize Talebi"] = registration.VisaRequest,
            ["Vize Durumu"] = registration.VisaStatusChoice,
            ["Pasaportu Geldi mi ?"] = PassportArrivedText(registration),
            ["Bilet Talebi"] = registration.TicketRequest,
            ["Bilet Durumu"] = registration.TicketStatusChoice,
            ["Katılım yapılacak şehir"] = registration.DepartureCity,
            ["Aktarma var mı ?"] = registration.HasTransfer,
            ["Aktarma şehri"] = registration.TransferCity,
            ["Referans"] = registration.Reference,
            ["Kargo Talebi"] = registration.CargoRequest,
            ["Toplam Fiyat"] = registration.TotalPrice.ToString("N2"),
            ["Para Birimi"] = registration.Currency,
            ["Ödenen"] = registration.PaidAmount.ToString("N2"),
            ["Kalan"] = registration.RemainingDebt.ToString("N2"),
            ["Pasaport Türü"] = person?.PassportType ?? string.Empty,
            ["Oda tipi"] = registration.RoomType,
            ["İlçe"] = registration.District,
            ["Sıra"] = string.Empty
        };
        row.ExportValues = kind switch
        {
            ListKind.Visa => SelectExportValues(commonValues, "Sıra", "Ad", "Soyad", "TC Kimlik", "Pasaport tipi", "Pasaport numarası", "Pasaport Başlangıç tarihi", "Pasaport bitiş tarihi", "Vize Talebi", "Vize Durumu", "Pasaportu Geldi mi ?", "Telefon numarası", "Adres"),
            ListKind.Passport => SelectExportValues(commonValues, "Sıra", "Ad", "Soyad", "TC Kimlik", "Pasaport tipi", "Pasaport numarası", "Pasaport Başlangıç tarihi", "Pasaport bitiş tarihi", "Telefon numarası", "Adres"),
            ListKind.Ticket => SelectExportValues(commonValues, "Sıra", "Ad", "Soyad", "TC Kimlik", "Bilet Talebi", "Bilet Durumu", "Telefon numarası", "Adres", "Katılım yapılacak şehir"),
            ListKind.Transfer => SelectExportValues(commonValues, "Sıra", "Ad", "Soyad", "TC Kimlik", "Aktarma var mı ?", "Aktarma şehri", "Telefon numarası", "Adres", "Katılım yapılacak şehir"),
            ListKind.Airline => SelectExportValues(commonValues, "Sıra", "Ad", "Soyad", "TC Kimlik", "Katılım yapılacak şehir", "Bilet Talebi", "Telefon numarası", "Adres"),
            ListKind.Reference => SelectExportValues(commonValues, "Sıra", "Ad", "Soyad", "TC Kimlik", "Referans", "Telefon numarası", "Adres"),
            ListKind.Payments => SelectExportValues(commonValues, "Sıra", "Ad", "Soyad", "TC Kimlik", "Pasaport numarası", "Toplam Fiyat", "Para Birimi", "Ödenen", "Kalan"),
            ListKind.Cargo => SelectExportValues(commonValues, "Sıra", "Ad", "Soyad", "TC Kimlik", "Telefon numarası", "Adres", "Kargo Talebi"),
            _ => SelectExportValues(commonValues, "Sıra", "Ad", "Soyad", "TC Kimlik", "Telefon numarası", "Adres", "Pasaport tipi", "Pasaport numarası", "Pasaport Başlangıç tarihi", "Pasaport bitiş tarihi", "Kan Grubu", "Aşı durumu", "hastalık durumu", "Yaş aralığı", "Vize Talebi", "Bilet Talebi", "Kargo Talebi", "Oda tipi", "Katılım yapılacak şehir", "Aktarma var mı ?", "Aktarma şehri", "İlçe", "Toplam Fiyat", "Para Birimi", "Ödenen", "Kalan", "Referans")
        };

        switch (kind)
        {
            case ListKind.Visa:
                row.Subtitle = $"Vize talebi: {ValueOrDash(registration.VisaRequest)} · Durum: {ValueOrDash(registration.VisaStatusChoice)}";
                row.Detail = $"Pasaport: {ValueOrDash(person?.PassportType)} {ValueOrDash(person?.PassportNumber)} · Geldi: {PassportArrivedText(registration)}";
                row.Context = $"{criteria} · Tel: {ValueOrDash(contact)}";
                break;
            case ListKind.Passport:
                row.Subtitle = $"{ValueOrDash(person?.PassportType)} · {ValueOrDash(person?.PassportNumber)}";
                row.Detail = $"Başlangıç: {DateText(person?.PassportStartDate)} · Bitiş: {DateText(person?.PassportEndDate)} · Tel: {ValueOrDash(contact)}";
                break;
            case ListKind.Ticket:
                row.Subtitle = $"Bilet: {ValueOrDash(registration.TicketRequest)} · Durum: {ValueOrDash(registration.TicketStatusChoice)}";
                row.Detail = $"Katılım şehri: {ValueOrDash(registration.DepartureCity)} · Tel: {ValueOrDash(contact)}";
                break;
            case ListKind.Transfer:
                row.Subtitle = $"Aktarma: {ValueOrDash(registration.HasTransfer)} · {ValueOrDash(registration.TransferCity)}";
                row.Detail = $"Katılım şehri: {ValueOrDash(registration.DepartureCity)} · Tel: {ValueOrDash(contact)}";
                break;
            case ListKind.Airline:
                row.Subtitle = $"Katılım şehri: {ValueOrDash(registration.DepartureCity)} · Bilet: {ValueOrDash(registration.TicketRequest)}";
                row.Detail = $"Aktarma: {ValueOrDash(registration.HasTransfer)} · Tel: {ValueOrDash(contact)}";
                break;
            case ListKind.Reference:
                row.Subtitle = $"Referans: {ValueOrDash(registration.Reference)}";
                row.Detail = $"Tel: {ValueOrDash(contact)} · Adres: {ValueOrDash(person?.Address)}";
                break;
            case ListKind.Payments:
                row.Subtitle = $"Toplam: {Money(registration.TotalPrice, registration.Currency)} · Ödenen: {Money(registration.PaidAmount, registration.Currency)}";
                row.Detail = $"Kalan: {Money(registration.RemainingDebt, registration.Currency)}";
                break;
            case ListKind.Cargo:
                row.Subtitle = $"Kargo talebi: {ValueOrDash(registration.CargoRequest)}";
                row.Detail = $"Tel: {ValueOrDash(contact)} · Adres: {ValueOrDash(person?.Address)}";
                break;
            default:
                row.Subtitle = criteria;
                row.Detail = $"Tel: {ValueOrDash(contact)} · Bilet: {ValueOrDash(registration.TicketRequest)} · Vize: {ValueOrDash(registration.VisaRequest)}";
                break;
        }

        return row;
    }

    private static ListDisplayRow CreatePhoneRow(PersonRecord person) => new()
    {
        Title = $"{person.FirstName} {person.LastName}".Trim(),
        Subtitle = ValueOrDash(person.PhoneNumber),
        Detail = ValueOrDash(person.Address),
        Context = $"TC Kimlik: {ValueOrDash(person.TcKimlikNo)}",
        ExportValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ad"] = person.FirstName,
            ["Soyad"] = person.LastName,
            ["TC Kimlik"] = person.TcKimlikNo,
            ["Telefon numarası"] = person.PhoneNumber,
            ["Adres"] = person.Address
        }
    };

    private static ListDisplayRow CreateHealthRow(PersonRecord person) => new()
    {
        Title = $"{person.FirstName} {person.LastName}".Trim(),
        Subtitle = $"Kan grubu: {ValueOrDash(person.BloodType)}",
        Detail = $"Sağlık durumu: {ValueOrDash(person.HealthStatus)} · Aşı: {ValueOrDash(person.VaccineStatus)}",
        Context = $"Tel: {ValueOrDash(person.PhoneNumber)}",
        ExportValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ad"] = person.FirstName,
            ["Soyad"] = person.LastName,
            ["TC Kimlik"] = person.TcKimlikNo,
            ["Uyruk"] = person.Nationality ?? string.Empty,
            ["Kan Grubu"] = person.BloodType,
            ["Hastalık Durumu"] = person.HealthStatus,
            ["Aşı Durumu"] = person.VaccineStatus
        }
    };

    private static Dictionary<string, string> SelectExportValues(Dictionary<string, string> values, params string[] headers) =>
        headers.ToDictionary(header => header, header => values.TryGetValue(header, out var value) ? value : string.Empty, StringComparer.OrdinalIgnoreCase);

    private static string GetTitle(ListKind kind) => kind switch
    {
        ListKind.Phone => "Telefon listesi",
        ListKind.Health => "Sağlık listesi",
        ListKind.TourGeneral => "Tur kayıtları · Genel",
        ListKind.Visa => "Tur kayıtları · Vize",
        ListKind.Passport => "Tur kayıtları · Pasaport",
        ListKind.Ticket => "Tur kayıtları · Bilet",
        ListKind.Transfer => "Tur kayıtları · Aktarma",
        ListKind.Airline => "Tur kayıtları · Havayolu",
        ListKind.Reference => "Tur kayıtları · Referans",
        ListKind.Rooms => "Oda listesi",
        ListKind.Payments => "Ödeme listesi",
        ListKind.Cargo => "Kargo listesi",
        _ => "Listeler"
    };

    private static string? SelectedValue(Picker picker)
    {
        var value = picker.SelectedItem as string;
        return string.IsNullOrWhiteSpace(value) || value == "Tümü" ? null : value;
    }

    private static bool Matches(string value, string? selected) => selected == null || string.Equals(value, selected, StringComparison.CurrentCultureIgnoreCase);

    private static bool Contains(string? value, string query) => value?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true;

    private static string ValueOrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "Belirtilmedi" : value.Trim();

    private static string DateText(DateTime? date) => date?.ToString("dd.MM.yyyy") ?? "Belirtilmedi";

    private static string PassportArrivedText(RegistrationRecord registration) => !string.IsNullOrWhiteSpace(registration.PassportArrivedValue)
        ? registration.PassportArrivedValue
        : registration.PassportArrived?.ToString() ?? "Belirtilmedi";

    private static string Money(decimal amount, string currency) => $"{amount:N2} {ValueOrDash(currency)}";

    private async void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updatingFilters || _selectedKind == null) return;
        await ApplyFiltersAsync();
    }

    private void OnBackToCategoriesClicked(object? sender, EventArgs e)
    {
        _selectedKind = null;
        ResultsPanel.IsVisible = false;
        DashboardPanel.IsVisible = true;
    }

    private async void OnHomeClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}

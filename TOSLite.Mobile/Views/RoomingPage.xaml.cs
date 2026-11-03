using System.Collections.ObjectModel;
using TOSLite.Mobile.Data;
using TOSLite.Mobile.Models;

namespace TOSLite.Mobile.Views;

public partial class RoomingPage : ContentPage
{
    private readonly RoomAllocationRepository _repository;
    private readonly ObservableCollection<RoomAllocationRecord> _rooms = [];
    private readonly ObservableCollection<RoomingCandidate> _candidates = [];
    private readonly List<RoomingCriteria> _criteria = [];
    private IReadOnlyList<string> _roomTypes = [];
    private RoomAllocationRecord? _selectedRoom;
    private RoomAllocationRecord? _firstRoomToConnect;
    private bool _loading;
    private bool _checkInDateSelected;
    private bool _checkOutDateSelected;

    public RoomingPage(RoomAllocationRepository repository)
    {
        InitializeComponent();
        _repository = repository;
        MobileTheme.Apply(this);
        RoomsCollection.ItemsSource = _rooms;
        CandidatesCollection.ItemsSource = _candidates;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            _loading = true;
            _checkInDateSelected = false;
            _checkOutDateSelected = false;
            await _repository.InitializeAsync();
            HotelPicker.ItemsSource = (await _repository.GetHotelsAsync()).ToList();
            _roomTypes = await _repository.GetRoomTypesAsync();
            RoomTypePicker.ItemsSource = _roomTypes.ToList();
            _criteria.Clear();
            _criteria.AddRange(await _repository.GetCriteriaAsync());
            CriteriaPicker.ItemsSource = _criteria;
            if (_criteria.Count == 0)
            {
                CriteriaLabel.Text = "Önce Tur Kayıtları modülünde aktif tur kaydı oluşturun.";
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Hata", $"Odalama verileri yüklenemedi: {ex.Message}", "Tamam");
        }
        finally
        {
            _loading = false;
        }
    }

    private async void OnCriteriaChanged(object? sender, EventArgs e)
    {
        if (_loading || CriteriaPicker.SelectedIndex < 0 || HotelPicker.SelectedIndex < 0) return;
        await LoadCriteriaDataAsync();
    }

    private async void OnHotelChanged(object? sender, EventArgs e)
    {
        if (_loading || CriteriaPicker.SelectedIndex < 0 || HotelPicker.SelectedIndex < 0) return;
        await LoadCriteriaDataAsync();
    }

    private async Task LoadCriteriaDataAsync()
    {
        var criteria = SelectedCriteria();
        if (criteria == null) return;
        criteria.HotelName = HotelPicker.SelectedItem?.ToString() ?? string.Empty;
        CriteriaLabel.Text = $"{criteria.TourType} · {criteria.Period} · {criteria.GroupType} · {criteria.DaysCount}";
        try
        {
            var allocations = await _repository.GetAllocationsAsync(criteria);
            _rooms.Clear();
            foreach (var room in allocations) _rooms.Add(room);
            if (allocations.Count > 0)
            {
                if (DateTime.TryParseExact(allocations[0].CheckInDate, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var checkIn))
                {
                    CheckInPicker.Date = checkIn;
                    _checkInDateSelected = true;
                }
                if (DateTime.TryParseExact(allocations[0].CheckOutDate, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var checkOut))
                {
                    CheckOutPicker.Date = checkOut;
                    _checkOutDateSelected = true;
                }
            }
            await RefreshCandidatesAsync(criteria);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Hata", $"Oda kayıtları yüklenemedi: {ex.Message}", "Tamam");
        }
    }

    private async Task RefreshCandidatesAsync(RoomingCriteria? criteria = null)
    {
        criteria ??= SelectedCriteria();
        _candidates.Clear();
        if (criteria == null) return;
        criteria.HotelName = HotelPicker.SelectedItem?.ToString() ?? string.Empty;
        var assignedIds = _rooms.SelectMany(x => x.Occupants).Select(x => x.PassengerId).ToHashSet();
        foreach (var candidate in await _repository.GetCandidatesAsync(criteria).ConfigureAwait(true))
        {
            if (!assignedIds.Contains(candidate.PersonId)) _candidates.Add(candidate);
        }
        CandidatesHeading.Text = $"Yolcu havuzu ({_candidates.Count})";
    }

    private void OnRoomSelected(object? sender, SelectionChangedEventArgs e)
    {
        _selectedRoom = e.CurrentSelection.FirstOrDefault() as RoomAllocationRecord;
    }

    private void OnRoomPickerChanged(object? sender, EventArgs e)
    {
        _selectedRoom = RoomPicker.SelectedItem as RoomAllocationRecord;
        RoomsCollection.SelectedItem = _selectedRoom;
    }

    private void OnCheckInDateSelected(object? sender, DateChangedEventArgs e) => _checkInDateSelected = true;

    private void OnCheckOutDateSelected(object? sender, DateChangedEventArgs e) => _checkOutDateSelected = true;

    private async void OnAddRoomClicked(object? sender, EventArgs e)
    {
        if (SelectedCriteria() == null || HotelPicker.SelectedIndex < 0)
        {
            await DisplayAlertAsync("Eksik bilgi", "Önce otel ve tur kriteri seçin.", "Tamam");
            return;
        }
        if (RoomTypePicker.SelectedItem is not string roomType)
        {
            await DisplayAlertAsync("Eksik bilgi", "Oda ayarlarından bir oda türü seçin.", "Tamam");
            return;
        }
        var usedNumbers = _rooms.Select(x => int.TryParse(x.RoomNumber, out var n) ? n : -1).Where(x => x > 0).ToHashSet();
        var number = 1;
        while (usedNumbers.Contains(number)) number++;
        var criteria = SelectedCriteria()!;
        var room = new RoomAllocationRecord
        {
            RoomNumber = number.ToString(),
            RoomType = roomType,
            HotelName = HotelPicker.SelectedItem!.ToString()!,
            TourType = criteria.TourType,
            Period = criteria.Period,
            GroupType = criteria.GroupType,
            DaysCount = criteria.DaysCount,
            CheckInDate = Convert.ToDateTime(CheckInPicker.Date).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            CheckOutDate = Convert.ToDateTime(CheckOutPicker.Date).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
        };
        _rooms.Add(room);
        RoomPicker.ItemsSource = _rooms;
        RoomPicker.SelectedItem = room;
        RoomsCollection.SelectedItem = room;
    }

    private async void OnPlaceCandidateClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: RoomingCandidate candidate }) return;
        if (_selectedRoom == null)
        {
            await DisplayAlertAsync("Oda seçilmedi", "Önce sağdaki oda listesinden yerleştirme yapılacak odayı seçin.", "Tamam");
            return;
        }
        if (_selectedRoom.Occupants.Count >= _selectedRoom.Capacity)
        {
            var allow = await DisplayAlertAsync("Kapasite aşımı", $"Oda kapasitesi {_selectedRoom.Capacity} kişiye ulaştı. Masaüstü programındaki gibi kapasite üstü yerleşim yapılsın mı?", "Evet", "Hayır");
            if (!allow) return;
        }
        _selectedRoom.Occupants.Add(new RoomOccupantRecord
        {
            PassengerId = candidate.PersonId,
            PersonName = candidate.PersonName,
            RoomType = candidate.RoomType
        });
        RefreshRoomItems();
        await RefreshCandidatesAsync();
    }

    private async void OnRemoveOccupantClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: RoomAllocationRecord room }) return;
        if (_selectedRoom == null || _selectedRoom.Id != room.Id && _selectedRoom != room)
            _selectedRoom = room;
        if (room.Occupants.Count == 0) return;
        var names = room.Occupants.Select(x => x.PersonName).ToArray();
        var selectedName = await DisplayActionSheetAsync("Odadan çıkarılacak yolcu", "Vazgeç", null, names);
        if (selectedName == null || selectedName == "Vazgeç") return;
        var occupant = room.Occupants.FirstOrDefault(x => x.PersonName == selectedName);
        if (occupant != null) room.Occupants.Remove(occupant);
        RefreshRoomItems();
        await RefreshCandidatesAsync();
    }

    private async void OnRemoveRoomClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: RoomAllocationRecord room }) return;
        _rooms.Remove(room);
        RoomPicker.ItemsSource = null;
        RoomPicker.ItemsSource = _rooms;
        if (_selectedRoom == room) _selectedRoom = null;
        RefreshRoomItems();
        await RefreshCandidatesAsync();
    }

    private async void OnToggleConnectionClicked(object? sender, EventArgs e)
    {
        if (_selectedRoom == null)
        {
            await DisplayAlertAsync("Oda seçilmedi", "Bağlantı için önce ilk odayı, ardından diğer odayı seçin.", "Tamam");
            return;
        }
        if (_firstRoomToConnect == null)
        {
            _firstRoomToConnect = _selectedRoom;
            ConnectionLabel.Text = $"İlk oda seçildi: {_firstRoomToConnect.RoomNumber}. Şimdi ikinci odayı seçin.";
            return;
        }
        if (_firstRoomToConnect == _selectedRoom)
        {
            await DisplayAlertAsync("Geçersiz seçim", "Bir odayı kendisiyle bağlayamazsınız.", "Tamam");
            return;
        }
        var connectionId = $"Group_{Guid.NewGuid():N}";
        _firstRoomToConnect.IsConnection = true;
        _selectedRoom.IsConnection = true;
        _firstRoomToConnect.ConnectionGroupId = connectionId;
        _selectedRoom.ConnectionGroupId = connectionId;
        _firstRoomToConnect = null;
        ConnectionLabel.Text = "Seçilen odalar bağlantı grubunda.";
        RefreshRoomItems();
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        var criteria = SelectedCriteria();
        if (criteria == null || HotelPicker.SelectedIndex < 0)
        {
            await DisplayAlertAsync("Eksik bilgi", "Otel ve tur kriteri seçin.", "Tamam");
            return;
        }
        if (_rooms.Count == 0 || _rooms.All(x => x.Occupants.Count == 0))
        {
            await DisplayAlertAsync("Eksik odalama", "Kaydetmek için en az bir oda açıp içine en az bir kişi yerleştirin.", "Tamam");
            return;
        }
        if (!_checkInDateSelected || !_checkOutDateSelected)
        {
            await DisplayAlertAsync("Tarih eksik", "Lütfen masaüstündeki odalama akışındaki gibi giriş ve çıkış tarihlerini seçin.", "Tamam");
            return;
        }
        criteria.HotelName = HotelPicker.SelectedItem!.ToString()!;
        foreach (var room in _rooms)
        {
            room.HotelName = criteria.HotelName;
            room.TourType = criteria.TourType;
            room.Period = criteria.Period;
            room.GroupType = criteria.GroupType;
            room.DaysCount = criteria.DaysCount;
            room.CheckInDate = Convert.ToDateTime(CheckInPicker.Date).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            room.CheckOutDate = Convert.ToDateTime(CheckOutPicker.Date).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }
        try
        {
            var capacityExceeded = _rooms.Any(x => x.Occupants.Count > x.Capacity);
            if (capacityExceeded && !await DisplayAlertAsync("Kapasite aşımı", "Bir veya daha fazla oda kapasite üstünde. Yine de devam edilsin mi?", "Evet", "Hayır")) return;
            await _repository.SaveBatchAsync(criteria, _rooms, capacityExceeded);
            await DisplayAlertAsync("Kaydedildi", "Odalama kayıtları telefona kaydedildi.", "Tamam");
            await LoadCriteriaDataAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Kayıt hatası", ex.Message, "Tamam");
        }
    }

    private void RefreshRoomItems()
    {
        RoomsCollection.ItemsSource = null;
        RoomsCollection.ItemsSource = _rooms;
        RoomPicker.ItemsSource = null;
        RoomPicker.ItemsSource = _rooms;
        ConnectionLabel.Text = string.Join(" · ", _rooms.Where(x => x.IsConnection).Select(x => $"Oda {x.RoomNumber}: {x.ConnectionGroupId}"));
        _ = RefreshCandidatesAsync();
    }

    private RoomingCriteria? SelectedCriteria() =>
        CriteriaPicker.SelectedIndex >= 0 && CriteriaPicker.SelectedIndex < _criteria.Count
            ? _criteria[CriteriaPicker.SelectedIndex]
            : null;

    private async void OnHomeClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}

namespace TOSLite.Mobile.Models;

public sealed class RoomAllocationRecord
{
    public int Id { get; set; }
    public int HotelId { get; set; }
    public string HotelName { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public string RoomType { get; set; } = string.Empty;
    public string TourType { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public string GroupType { get; set; } = string.Empty;
    public string DaysCount { get; set; } = string.Empty;
    public string CheckInDate { get; set; } = string.Empty;
    public string CheckOutDate { get; set; } = string.Empty;
    public bool IsConnection { get; set; }
    public string ConnectionGroupId { get; set; } = string.Empty;
    public string SyncId { get; set; } = string.Empty;
    public DateTime LastModifiedUtc { get; set; }
    public bool IsDeleted { get; set; }
    public List<RoomOccupantRecord> Occupants { get; set; } = [];
    public int Capacity => ResolveCapacity(RoomType);
    public string OccupantSummary => Occupants.Count == 0
        ? "Henüz yolcu yok"
        : string.Join(", ", Occupants.Select(x => x.PersonName));
    public string DisplayText => $"Oda {RoomNumber} · {RoomType} · {Occupants.Count}/{Capacity} kişi";

    public static int ResolveCapacity(string roomType)
    {
        var type = (roomType ?? string.Empty).ToUpperInvariant();
        if (type.Contains("SNGL") || type.Contains("SINGLE")) return 1;
        if (type.Contains("DBL") || type.Contains("TWIN")) return 2;
        if (type.Contains("TRPL") || type.Contains("TRİPLE") || type.Contains("TRIPLE")) return 3;
        if (type.Contains("QUAD")) return 4;
        if (type.Contains("QUINT")) return 5;
        return 2;
    }
}

public sealed class RoomOccupantRecord
{
    public int Id { get; set; }
    public int RoomAllocationId { get; set; }
    public int PassengerId { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public string RoomType { get; set; } = string.Empty;
}

public sealed class RoomingCriteria
{
    public string HotelName { get; set; } = string.Empty;
    public string TourType { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public string GroupType { get; set; } = string.Empty;
    public string DaysCount { get; set; } = string.Empty;
    public string DisplayText => $"{TourType} · {Period} · {GroupType} · {DaysCount}";
}

public sealed class RoomingCandidate
{
    public int PersonId { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public string RoomType { get; set; } = string.Empty;
}

namespace TOSLite.Mobile.Models;

public sealed class RegistrationRecord
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public int? DesktopRegistrationId { get; set; }
    public string TourType { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public string GroupType { get; set; } = string.Empty;
    public string DaysCount { get; set; } = string.Empty;
    public string VisaRequest { get; set; } = "Seçilmedi";
    public string TicketRequest { get; set; } = "Seçilmedi";
    public string VisaStatusChoice { get; set; } = "Seçilmedi";
    public string TicketStatusChoice { get; set; } = "Seçilmedi";
    public int? PassportArrived { get; set; }
    public string PassportArrivedValue { get; set; } = string.Empty;
    public string CargoRequest { get; set; } = "Seçilmedi";
    public string RoomType { get; set; } = "Seçilmedi";
    public string DepartureCity { get; set; } = "Seçilmedi";
    public string HasTransfer { get; set; } = "Yok";
    public string TransferCity { get; set; } = string.Empty;
    public string District { get; set; } = "Belirtilmedi";
    public decimal TotalPrice { get; set; }
    public string Currency { get; set; } = "Seçilmedi";
    public decimal PaidAmount { get; set; }
    public decimal RemainingDebt { get; set; }
    public string Reference { get; set; } = "Belirtilmedi";
    public string Status { get; set; } = "Aktif";
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public bool IsActive { get; set; } = true;
    public DateTime LastModifiedUtc { get; set; }
    public string PersonName { get; set; } = string.Empty;
}

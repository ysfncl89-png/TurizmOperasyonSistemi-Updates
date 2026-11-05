namespace TOSLite.Mobile.Models;

public sealed class CargoQueueRecord
{
    public int Id { get; set; }
    public string SyncId { get; set; } = Guid.NewGuid().ToString("N");
    public int? DesktopQueueId { get; set; }
    public int RegistrationId { get; set; }
    public int? DesktopRegistrationId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Tc { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string TourType { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public string GroupType { get; set; } = string.Empty;
    public string DaysCount { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public bool Printed { get; set; }
    public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
    public string DisplayTitle => $"{FullName} · {TourType} · {Period}";
}

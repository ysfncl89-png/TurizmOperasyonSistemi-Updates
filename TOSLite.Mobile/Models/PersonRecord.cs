namespace TOSLite.Mobile.Models;

public sealed class PersonRecord
{
    public int Id { get; set; }
    public string ClientId { get; set; } = Guid.NewGuid().ToString("N");
    public int? DesktopPersonId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string TcKimlikNo { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public DateTime BirthDate { get; set; } = DateTime.Now;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? Aciklama { get; set; }
    public string PassportType { get; set; } = string.Empty;
    public string PassportNumber { get; set; } = string.Empty;
    public DateTime? PassportStartDate { get; set; }
    public DateTime? PassportEndDate { get; set; }
    public string BloodType { get; set; } = string.Empty;
    public string HealthStatus { get; set; } = string.Empty;
    public string VaccineStatus { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public string AgeRange { get; set; } = string.Empty;
    public string? Nationality { get; set; }
    public DateTime LastModifiedUtc { get; set; }
}

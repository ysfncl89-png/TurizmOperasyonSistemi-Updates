using System.Text.Json.Serialization;

namespace TurizmOperasyonSistemi.Services;

public sealed class MobileSyncPairRequest
{
    public string PairingCode { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
}

public sealed class MobileSyncPackage
{
    public string DeviceId { get; set; } = string.Empty;
    public Dictionary<int, string> PersonLocalIds { get; set; } = [];
    public List<MobilePersonDto> Persons { get; set; } = [];
    public List<MobileRegistrationDto> Registrations { get; set; } = [];
    public List<int> DeletedRegistrationIds { get; set; } = [];
    public List<MobileSettingDto> Settings { get; set; } = [];
    public List<MobileRoomingDto> Rooming { get; set; } = [];
    public List<MobilePaymentDto> Payments { get; set; } = [];
    public List<MobileCargoQueueDto> CargoQueue { get; set; } = [];
}

public sealed class MobilePaymentDto
{
    public int Id { get; set; }
    public string SyncId { get; set; } = string.Empty;
    public int? DesktopPaymentId { get; set; }
    public int RegistrationId { get; set; }
    public int? DesktopRegistrationId { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal OriginalAmount { get; set; }
    public string OriginalCurrency { get; set; } = string.Empty;
    public decimal AmountInRegistrationCurrency { get; set; }
    public decimal? Rate { get; set; }
    public string Note { get; set; } = string.Empty;
    public string UploadedDocumentPath { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public DateTime LastModifiedUtc { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class MobileCargoQueueDto
{
    public int Id { get; set; }
    public string SyncId { get; set; } = string.Empty;
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
    public DateTime AddedAt { get; set; }
    public bool Printed { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class MobileSyncDeviceRequest
{
    public string DeviceId { get; set; } = string.Empty;
}

public sealed class MobilePersonDto
{
    public int LocalId { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public int? DesktopPersonId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string TcKimlikNo { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public DateTime BirthDate { get; set; }
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
    public bool IsActive { get; set; }
    public bool IsDeleted { get; set; }
    public string AgeRange { get; set; } = string.Empty;
    public string? Nationality { get; set; }
    public DateTime LastModifiedUtc { get; set; }
}

public sealed class MobileRegistrationDto
{
    public int Id { get; set; }
    public int? DesktopRegistrationId { get; set; }
    public int PersonId { get; set; }
    public int? DesktopPersonId { get; set; }
    public string TourType { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public string GroupType { get; set; } = string.Empty;
    public string DaysCount { get; set; } = string.Empty;
    public string VisaRequest { get; set; } = string.Empty;
    public string TicketRequest { get; set; } = string.Empty;
    public string VisaStatusChoice { get; set; } = string.Empty;
    public string TicketStatusChoice { get; set; } = string.Empty;
    public int? PassportArrived { get; set; }
    public string PassportArrivedValue { get; set; } = string.Empty;
    public string CargoRequest { get; set; } = string.Empty;
    public string RoomType { get; set; } = string.Empty;
    public string DepartureCity { get; set; } = string.Empty;
    public string HasTransfer { get; set; } = string.Empty;
    public string TransferCity { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public decimal TotalPrice { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal PaidAmount { get; set; }
    public decimal RemainingDebt { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public bool IsActive { get; set; }
    public DateTime LastModifiedUtc { get; set; }
}

public sealed class MobileSettingDto
{
    public string Type { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
}

public sealed class MobileRoomingDto
{
    public int Id { get; set; }
    public string SyncId { get; set; } = string.Empty;
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
    public List<int> PassengerIds { get; set; } = [];
    public List<int> PassengerDesktopIds { get; set; } = [];
    public DateTime LastModifiedUtc { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class MobileSyncConflictDto
{
    public string RecordType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class MobileSyncResultDto
{
    public int ImportedPersons { get; set; }
    public int ImportedRegistrations { get; set; }
    public int ImportedSettings { get; set; }
    public int ImportedRooming { get; set; }
    public List<MobileSettingDto> Settings { get; set; } = [];
    public List<MobilePersonDto> Persons { get; set; } = [];
    public List<MobilePersonDto> DeletedPersons { get; set; } = [];
    public List<MobileRegistrationDto> Registrations { get; set; } = [];
    public List<int> AcknowledgedDeletedRegistrationIds { get; set; } = [];
    public List<MobileRoomingDto> Rooming { get; set; } = [];
    public List<MobilePaymentDto> Payments { get; set; } = [];
    public List<MobileCargoQueueDto> CargoQueue { get; set; } = [];
    public List<MobileSyncItemDto> ImportedItems { get; set; } = [];
    public List<MobileSyncConflictDto> Conflicts { get; set; } = [];
}

public sealed class MobileSyncItemDto
{
    public string RecordType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

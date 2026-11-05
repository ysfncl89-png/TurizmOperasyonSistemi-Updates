namespace TOSLite.Mobile.Models;

public sealed class PaymentRecord
{
    public int Id { get; set; }
    public string SyncId { get; set; } = Guid.NewGuid().ToString("N");
    public int? DesktopPaymentId { get; set; }
    public int RegistrationId { get; set; }
    public int? DesktopRegistrationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public decimal OriginalAmount { get; set; }
    public string OriginalCurrency { get; set; } = string.Empty;
    public decimal AmountInRegistrationCurrency { get; set; }
    public decimal? Rate { get; set; }
    public string Note { get; set; } = string.Empty;
    public string UploadedDocumentPath { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public string RegistrationCurrency { get; set; } = string.Empty;
    public string TourSummary { get; set; } = string.Empty;
}

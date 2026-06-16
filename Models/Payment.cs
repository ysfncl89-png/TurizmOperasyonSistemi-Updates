using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TurizmOperasyonSistemi.Models
{
    public class Payment
    {
        [Key]
        public int Id { get; set; }

        public int RegistrationId { get; set; }

        [ForeignKey("RegistrationId")]
        public virtual Registration Registration { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Original entered amount and currency (as user entered)
        public decimal OriginalAmount { get; set; }
        public string OriginalCurrency { get; set; }

        // If converted, amount in registration currency
        public decimal AmountInRegistrationCurrency { get; set; }
        public decimal? Rate { get; set; }

        public string Note { get; set; }
        public string UploadedDocumentPath { get; set; }
        // Payment method chosen from settings (e.g. Nakit, Kredi Kartı)
        public string PaymentMethod { get; set; }
    }
}

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TurizmOperasyonSistemi.Models
{
    public class Registration
    {
        [Key]
        public int Id { get; set; }

        // 1. ADIM: Kişi Bağlantısı (Foreign Key)
        public int PersonId { get; set; }

        [ForeignKey("PersonId")]
        public virtual Person Person { get; set; }

        // 2. ADIM: Tur Bilgileri (Zorunlu Alanlar)
        [Required]
        public string TourType { get; set; }      // Tur Türü (Örn: Kültür, Hac-Umre vb.)

        [Required]
        public string Period { get; set; }        // Dönem (Örn: Yaz 2026, Dönem 1 vb.)

        [Required]
        public string GroupType { get; set; }     // Grup Türü (Örn: Öğrenci, Aile vb.)

        [Required]
        public string DaysCount { get; set; }     // Kaç Günlük Grup (Örn: 7 Gün, 14 Gün)

        // 3. ADIM: Vize ve Bilet
        public string VisaRequest { get; set; }    // Vize Talebi
        public string TicketRequest { get; set; }  // Bilet Talebi

        // 4. ADIM: Kargo Talebi
        public string CargoRequest { get; set; }   // Kargo Talebi

        // 5. ADIM: Oda Türü
        public string RoomType { get; set; }       // Oda Türü (Örn: Single, Double, Triple)

        // 6. ADIM: Katılım Yapılacak Şehir ve Aktarma
        public string DepartureCity { get; set; }  // Katılım Yapılacak Şehir
        public string HasTransfer { get; set; }    // Aktarma var mı? (Örn: Var / Yok)
        public string TransferCity { get; set; }   // Aktarma Şehri (Eğer "Var" seçildiyse)
        public string District { get; set; }       // İlçe (TextBox'tan gelecek string)

        // 7. ADIM: Fiyat ve Ödeme
        public decimal TotalPrice { get; set; }     // Toplam Fiyat (Fiyat Kutusu)
        public string Currency { get; set; }       // Para Birimi (EUR, USD, TRY vb.)
        public decimal PaidAmount { get; set; }    // Ödenen Tutar
        public decimal RemainingDebt { get; set; } // Kalan Borç (Otomatik hesaplanıp veri tabanına yazılacak)

        // 8. ADIM: Referans
        public string Reference { get; set; }      // Referans Kimse (TextBox)

        // Sistem Takip Alanları
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public bool IsActive { get; set; } = true;
    }
}
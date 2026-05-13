using System;

namespace TurizmOperasyonSistemi.Models
{
    public class Registration
    {
        public int Id { get; set; }

        // KİŞİ BAĞLANTISI
        public int PersonId { get; set; }
        public Person Person { get; set; }

        // TUR BİLGİLERİ
        public string TourType { get; set; } = "";
        public string Period { get; set; } = "";
        public string GroupType { get; set; } = "";
        public int DayCount { get; set; }

        // VİZE
        public string VisaStatus { get; set; } = "";

        // BİLET
        public string TicketStatus { get; set; } = "";

        // AKTARMA
        public string TransferStatus { get; set; } = "";

        // KARGO
        public string CargoStatus { get; set; } = "";

        // ODA
        public string RoomType { get; set; } = "";

        // ŞEHİR
        public string JoinCity { get; set; } = "";

        // ÜCRET
        public decimal TourPrice { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal RemainingDebt { get; set; }

        // PARA BİRİMİ
        public string CurrencyType { get; set; } = "";

        // TARİH
        public DateTime RegistrationDate { get; set; } = DateTime.Now;

        // AKTİF/PASİF
        public bool IsActive { get; set; } = true;
    }
}
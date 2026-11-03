using System;

namespace TurizmOperasyonSistemi.Models
{
    public class Person
    {
        public int Id { get; set; }

        public string FirstName { get; set; }

        public string LastName { get; set; }

        public string TcKimlikNo { get; set; }

        public string Gender { get; set; }

        public DateTime BirthDate { get; set; }

        public string PhoneNumber { get; set; }

        public string Address { get; set; }

        // Yeni alan: Açıklama (kullanıcı notları/ek açıklamalar)
        public string? Aciklama { get; set; }

        public string PassportType { get; set; }

        public string PassportNumber { get; set; }

        public DateTime? PassportStartDate { get; set; }

        public DateTime? PassportEndDate { get; set; }

        public string BloodType { get; set; }

        public string HealthStatus { get; set; }

        public string VaccineStatus { get; set; }

        public bool IsActive { get; set; } = true;

        public string AgeRange { get; set; }

        // Yeni alan: Uyruk (Nationality)
        public string? Nationality { get; set; }

        public DateTime LastModifiedUtc { get; set; }

        // (Açıklama ve Müsaitlik sütunları kaldırıldı)
    }
}
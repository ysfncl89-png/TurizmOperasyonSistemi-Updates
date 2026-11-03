using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace TurizmOperasyonSistemi.Models
{
    public class RoomAllocation
    {
        [Key]
        public int Id { get; set; } // Veritabanı için benzersiz ID

        public int HotelId { get; set; } // Hangi otelde açıldığı
        public virtual Hotel Hotel { get; set; } // İlişkili nesne

        public string RoomNumber { get; set; } // Örn: 101, 102 veya "Oda 1"
        public string RoomType { get; set; } // Örn: DBL, TRPL, SINGLE
        public string TourType { get; set; } // Bağlı olduğu tur türü
        public string Period { get; set; } // Bağlı olduğu dönem
        public string GroupType { get; set; } // Bağlı olduğu grup türü
        public string DaysCount { get; set; } // Kaç günlük grup
        // Giriş ve çıkış tarihlerini metin olarak saklıyoruz (veritabanı sütunları dinamik ekleniyor)
        public string CheckInDate { get; set; }
        public string CheckOutDate { get; set; }
        public bool IsConnection { get; set; } // Connection oda mı?
        public string ConnectionGroupId { get; set; } // Bağlantı grubu ID'si
        public string SyncId { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime LastModifiedUtc { get; set; }
        public bool IsDeleted { get; set; }

        // Bir odada birden fazla yolcu olabilir (Bire-Çok ilişki)
        public virtual ICollection<RoomOccupant> Occupants { get; set; } = new List<RoomOccupant>();
    }
}

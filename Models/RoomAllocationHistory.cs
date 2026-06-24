using System;
using System.ComponentModel.DataAnnotations;

namespace TurizmOperasyonSistemi.Models
{
    public class RoomAllocationHistory
    {
        [Key]
        public int Id { get; set; }

        // Original allocation id (if any)
        public int RoomAllocationId { get; set; }

        public int HotelId { get; set; }
        public string HotelName { get; set; }
        public string RoomNumber { get; set; }
        public string RoomType { get; set; }
        public string TourType { get; set; }
        public string Period { get; set; }
        public string GroupType { get; set; }
        public string DaysCount { get; set; }

        public int PersonId { get; set; }
        public int RegistrationId { get; set; }

        public DateTime RemovedAt { get; set; }
        public bool Restored { get; set; }
        public DateTime? RestoredAt { get; set; }
    }
}

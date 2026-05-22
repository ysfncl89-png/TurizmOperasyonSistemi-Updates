using System.ComponentModel.DataAnnotations;

namespace TurizmOperasyonSistemi.Models
{
    public class RoomOccupant
    {
        [Key]
        public int Id { get; set; }

        public int RoomAllocationId { get; set; } // Hangi odaya atandı?
        public virtual RoomAllocation RoomAllocation { get; set; }

        public int PassengerId { get; set; } // Kişiler modülündeki yolcu ID'si
    }
}
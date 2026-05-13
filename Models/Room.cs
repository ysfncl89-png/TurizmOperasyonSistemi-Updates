namespace TurizmOperasyonSistemi.Models
{
    public class Room
    {
        public int Id { get; set; }

        public string RoomNumber { get; set; }

        public string RoomType { get; set; }

        public int Capacity { get; set; }

        public string GenderType { get; set; }

        public bool IsFull { get; set; }
        public bool IsActive { get; set; } = true;

        // RELATION

        public int HotelId { get; set; }

        public Hotel Hotel { get; set; }
    }
}
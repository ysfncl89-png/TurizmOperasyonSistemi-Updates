using System.Collections.Generic;

namespace TurizmOperasyonSistemi.Models
{
    public class Hotel
    {
        public int Id { get; set; }

        public string HotelName { get; set; }

        public string City { get; set; }

        public string Country { get; set; }

        public int Capacity { get; set; }

        public string Notes { get; set; }

        public bool IsActive { get; set; } = true;

        public ICollection<Room> Rooms { get; set; }
    }
}
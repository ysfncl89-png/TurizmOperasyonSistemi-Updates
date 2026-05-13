using System;

namespace TurizmOperasyonSistemi.Models
{
    public class RoomAssignment
    {
        public int Id { get; set; }

        public int RegistrationId { get; set; }
        public Registration Registration { get; set; }

        public int RoomId { get; set; }
        public Room Room { get; set; }

        public DateTime AssignmentDate { get; set; } = DateTime.Now;

        public bool IsActive { get; set; } = true;
    }
}
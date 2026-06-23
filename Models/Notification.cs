using System;

namespace TurizmOperasyonSistemi.Models
{
    public class Notification
    {
        public int Id { get; set; }
        public string Key { get; set; }
        // Serialized payload (JSON) when complex object provided
        public string PayloadJson { get; set; }
        // Optional human-readable message
        public string Message { get; set; }
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}

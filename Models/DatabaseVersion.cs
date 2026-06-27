using System;

namespace TurizmOperasyonSistemi.Models
{
    /// <summary>
    /// Database versiyonlama bilgisini tutar
    /// </summary>
    public class DatabaseVersion
    {
        public int Id { get; set; }
        public string Version { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Description { get; set; }
    }
}

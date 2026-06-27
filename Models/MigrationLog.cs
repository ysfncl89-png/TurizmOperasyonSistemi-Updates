using System;

namespace TurizmOperasyonSistemi.Models
{
    /// <summary>
    /// Migration uygulanmasının geçmişini tutar
    /// </summary>
    public class MigrationLog
    {
        public int Id { get; set; }
        public string MigrationName { get; set; }
        public DateTime AppliedAt { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }
}

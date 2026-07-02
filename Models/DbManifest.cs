using System;

namespace TurizmOperasyonSistemi.Models
{
    public class DbManifest
    {
        public long DbVersion { get; set; }
        public string AppVersion { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastModified { get; set; }
        public string MachineId { get; set; }
        public string User { get; set; }
        public string Checksum { get; set; }
        public long DbSize { get; set; }
        public int BackupVersion { get; set; }
    }
}

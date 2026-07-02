using System;

namespace TurizmOperasyonSistemi.Models
{
    public class SyncState
    {
        public DateTime LastSyncAt { get; set; }
        public string LastSyncMachine { get; set; }
        public long LastSyncDbVersion { get; set; }
        public string Status { get; set; }
        public string Details { get; set; }
    }
}

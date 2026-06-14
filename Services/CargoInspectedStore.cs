using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TurizmOperasyonSistemi.Services
{
    // Simple AppData-based persistence to avoid requiring a DB schema migration.
    // Stores a list of registration ids that have been marked as "cargo inspected".
    public class CargoInspectedStore
    {
        private static readonly string AppDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TurizmOperasyonSistemi");
        private static readonly string FilePath = Path.Combine(AppDataPath, "cargo_inspected.json");

        private HashSet<int> _ids;

        public CargoInspectedStore()
        {
            Load();
        }

        private void Load()
        {
            try
            {
                if (!Directory.Exists(AppDataPath)) Directory.CreateDirectory(AppDataPath);
                if (!File.Exists(FilePath)) { _ids = new HashSet<int>(); return; }
                var txt = File.ReadAllText(FilePath);
                var list = JsonSerializer.Deserialize<List<int>>(txt) ?? new List<int>();
                _ids = new HashSet<int>(list);
            }
            catch
            {
                _ids = new HashSet<int>();
            }
        }

        private void Save()
        {
            try
            {
                if (!Directory.Exists(AppDataPath)) Directory.CreateDirectory(AppDataPath);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(_ids.ToList()));
            }
            catch { }
        }

        public bool IsInspected(int registrationId)
        {
            try { return _ids.Contains(registrationId); } catch { return false; }
        }

        public void MarkInspected(int registrationId)
        {
            try
            {
                if (_ids.Add(registrationId)) Save();
            }
            catch { }
        }

        public void Unmark(int registrationId)
        {
            try
            {
                if (_ids.Remove(registrationId)) Save();
            }
            catch { }
        }
    }
}

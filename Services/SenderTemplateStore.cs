using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TurizmOperasyonSistemi.Services
{
    public class SenderTemplate
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
    }

    public class SenderTemplateStore
    {
        private static readonly string AppDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TurizmOperasyonSistemi");
        private static readonly string FilePath = Path.Combine(AppDataPath, "sender_templates.json");

        private List<SenderTemplate> _items = new List<SenderTemplate>();

        public SenderTemplateStore()
        {
            Load();
        }

        private void Load()
        {
            try
            {
                if (!Directory.Exists(AppDataPath)) Directory.CreateDirectory(AppDataPath);
                if (!File.Exists(FilePath)) { _items = new List<SenderTemplate>(); return; }
                var txt = File.ReadAllText(FilePath);
                _items = JsonSerializer.Deserialize<List<SenderTemplate>>(txt) ?? new List<SenderTemplate>();
            }
            catch
            {
                _items = new List<SenderTemplate>();
            }
        }

        private void Save()
        {
            try
            {
                if (!Directory.Exists(AppDataPath)) Directory.CreateDirectory(AppDataPath);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(_items));
            }
            catch { }
        }

        public IReadOnlyList<SenderTemplate> GetAll() => _items.AsReadOnly();

        public void Add(SenderTemplate t)
        {
            try
            {
                if (t == null) return;
                if (string.IsNullOrWhiteSpace(t.Id)) t.Id = Guid.NewGuid().ToString();
                _items.Add(t);
                Save();
            }
            catch { }
        }

        public void Remove(string id)
        {
            try
            {
                var ex = _items.FirstOrDefault(x => x.Id == id);
                if (ex != null) { _items.Remove(ex); Save(); }
            }
            catch { }
        }
    }
}

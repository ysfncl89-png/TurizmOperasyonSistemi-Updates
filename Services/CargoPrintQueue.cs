using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace TurizmOperasyonSistemi.Services
{
    public class PrintQueueItem
    {
        public int RegistrationId { get; set; }
        public string FullName { get; set; }
        public string Tc { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        // New: carry grouping metadata so print pages can be grouped by tour/period
        public string TourType { get; set; }
        public string Period { get; set; }
        public string GroupType { get; set; }
        public string DaysCount { get; set; }
        // How many copies of this card should be printed
        public int Quantity { get; set; } = 1;
        public DateTime AddedAt { get; set; }
        public bool Printed { get; set; }
    }

    // Simple singleton print queue service persisted to AppData
    public sealed class CargoPrintQueue
    {
        private static readonly Lazy<CargoPrintQueue> _lazy = new(() => new CargoPrintQueue());
        public static CargoPrintQueue Instance => _lazy.Value;

        private readonly string _filePath;
        private readonly object _sync = new();

        public ObservableCollection<PrintQueueItem> Items { get; } = new ObservableCollection<PrintQueueItem>();

        // Event fired when an item is removed from the queue
        public event Action<PrintQueueItem>? ItemRemoved;

        private CargoPrintQueue()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "TurizmOperasyonSistemi");
            try { Directory.CreateDirectory(dir); } catch { }
            _filePath = Path.Combine(dir, "cargo_print_queue.json");

            LoadFromFile();

            // Auto-save on change
            Items.CollectionChanged += Items_CollectionChanged;
        }

        private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // Save asynchronously to avoid UI blocking
            ThreadPool.QueueUserWorkItem(_ => SaveToFile());
        }

        public void Add(PrintQueueItem item)
        {
            if (item == null) return;
            item.AddedAt = DateTime.UtcNow;
            lock (_sync)
            {
                Items.Add(item);
            }
        }

        public void Remove(PrintQueueItem item)
        {
            if (item == null) return;
            lock (_sync)
            {
                Items.Remove(item);
                try { ItemRemoved?.Invoke(item); } catch { }
            }
        }

        public void MarkPrinted(PrintQueueItem item)
        {
            if (item == null) return;
            item.Printed = true;
            ThreadPool.QueueUserWorkItem(_ => SaveToFile());
        }

        private void LoadFromFile()
        {
            try
            {
                if (!File.Exists(_filePath)) return;
                var json = File.ReadAllText(_filePath);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var arr = JsonSerializer.Deserialize<PrintQueueItem[]>(json, opts);
                if (arr == null) return;
                foreach (var it in arr) Items.Add(it);
            }
            catch
            {
                // ignore load errors
            }
        }

        private void SaveToFile()
        {
            try
            {
                lock (_sync)
                {
                    var arr = Items;
                    var opts = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(arr, opts);
                    File.WriteAllText(_filePath, json);
                }
            }
            catch
            {
                // ignore save errors
            }
        }
    }
}

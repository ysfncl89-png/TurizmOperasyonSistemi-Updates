using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace TurizmOperasyonSistemi.Services
{
    public class PrintQueueItem
    {
        public string SyncId { get; set; } = Guid.NewGuid().ToString("N");
        public string SyncDeviceId { get; set; } = string.Empty;
        public int? DesktopQueueId { get; set; }
        public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; }
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
            item.LastModifiedUtc = DateTime.UtcNow;
            item.SyncId = string.IsNullOrWhiteSpace(item.SyncId) ? Guid.NewGuid().ToString("N") : item.SyncId;
            item.IsDeleted = false;
            lock (_sync)
            {
                Items.Add(item);
            }
        }

        public PrintQueueItem ApplySyncItem(PrintQueueItem item)
        {
            lock (_sync)
            {
                var existing = Items.FirstOrDefault(x => x.SyncId == item.SyncId && x.SyncDeviceId == item.SyncDeviceId);
                if (item.IsDeleted)
                {
                    if (existing is not null) Items.Remove(existing);
                    return item;
                }
                if (existing is null)
                {
                    Items.Add(item);
                    return item;
                }
                if (existing.LastModifiedUtc > item.LastModifiedUtc) return existing;
                existing.RegistrationId = item.RegistrationId;
                existing.FullName = item.FullName;
                existing.Tc = item.Tc;
                existing.Phone = item.Phone;
                existing.Address = item.Address;
                existing.TourType = item.TourType;
                existing.Period = item.Period;
                existing.GroupType = item.GroupType;
                existing.DaysCount = item.DaysCount;
                existing.Quantity = item.Quantity;
                existing.AddedAt = item.AddedAt;
                existing.Printed = item.Printed;
                existing.DesktopQueueId = item.DesktopQueueId;
                existing.LastModifiedUtc = item.LastModifiedUtc;
                return existing;
            }
        }

        public void Remove(PrintQueueItem item)
        {
            if (item == null) return;
            lock (_sync)
            {
                item.IsDeleted = true;
                item.LastModifiedUtc = DateTime.UtcNow;
                Items.Remove(item);
                try { ItemRemoved?.Invoke(item); } catch { }
            }
        }

        // Decrease the Quantity for an item by the specified amount.
        // If resulting quantity is <= 0 the item will be removed from the queue.
        public void DecrementQuantity(PrintQueueItem item, int amount = 1)
        {
            if (item == null) return;
            if (amount <= 0) return;
            lock (_sync)
            {
                try
                {
                    // If the item exists in the collection operate on that instance
                    var existing = Items.FirstOrDefault(i => i == item);
                    if (existing == null)
                    {
                        // Try to locate by RegistrationId fallback
                        existing = Items.FirstOrDefault(i => i.RegistrationId == item.RegistrationId);
                    }

                    if (existing == null)
                    {
                        return;
                    }

                    try
                    {
                        existing.Quantity = Math.Max(0, existing.Quantity - amount);
                    }
                    catch { existing.Quantity = 0; }

                    if (existing.Quantity <= 0)
                    {
                        existing.IsDeleted = true;
                        existing.LastModifiedUtc = DateTime.UtcNow;
                        Items.Remove(existing);
                        try { ItemRemoved?.Invoke(existing); } catch { }
                    }

                    // Persist changes
                    SaveToFile();
                }
                catch { }
            }
        }

        // Increase the Quantity for an item by the specified amount.
        public void IncrementQuantity(PrintQueueItem item, int amount = 1)
        {
            if (item == null) return;
            if (amount <= 0) return;
            lock (_sync)
            {
                try
                {
                    var existing = Items.FirstOrDefault(i => i == item);
                    if (existing == null)
                    {
                        // Try to locate by RegistrationId fallback
                        existing = Items.FirstOrDefault(i => i.RegistrationId == item.RegistrationId);
                    }

                    if (existing == null)
                    {
                        return;
                    }

                    try
                    {
                        existing.Quantity = Math.Max(0, existing.Quantity + amount);
                        existing.LastModifiedUtc = DateTime.UtcNow;
                    }
                    catch { existing.Quantity = Math.Max(1, amount); }

                    // Persist changes
                    SaveToFile();
                }
                catch { }
            }
        }

        public void MarkPrinted(PrintQueueItem item)
        {
            if (item == null) return;
            item.Printed = true;
            item.LastModifiedUtc = DateTime.UtcNow;
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

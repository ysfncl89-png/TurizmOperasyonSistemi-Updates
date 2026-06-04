using System.Collections.Concurrent;
using System.Windows.Controls;

namespace TurizmOperasyonSistemi.Services
{
    public static class DataGridColumnMetadata
    {
        private static readonly ConcurrentDictionary<DataGridColumn, string> _map = new ConcurrentDictionary<DataGridColumn, string>();

        public static void SetPropertyKey(DataGridColumn col, string key)
        {
            if (col == null) return;
            _map.AddOrUpdate(col, key, (c, old) => key);
        }

        public static string GetPropertyKey(DataGridColumn col)
        {
            if (col == null) return null;
            if (_map.TryGetValue(col, out var v)) return v;
            return null;
        }
    }
}

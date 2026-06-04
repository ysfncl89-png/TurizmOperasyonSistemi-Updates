using System;
using System.Collections.Generic;
using System.Linq;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;
using System.Text.Json;

namespace TurizmOperasyonSistemi.Services
{
    public class ListTemplateStore
    {
        public class ColumnDto { public string Title { get; set; } public int Order { get; set; } public bool IsVisible { get; set; } }

        private const string SettingTypePrefix = "ListTemplate:";
        private static readonly string AppDataPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TurizmOperasyonSistemi");
        private static readonly string FallbackFile = System.IO.Path.Combine(AppDataPath, "list_templates.json");

        public void SaveTemplate(string modeName, List<ColumnDto> columns)
        {
            if (string.IsNullOrWhiteSpace(modeName)) throw new ArgumentNullException(nameof(modeName));
            var type = SettingTypePrefix + modeName;
            var json = JsonSerializer.Serialize(columns ?? new List<ColumnDto>());
            // Try DB first
            try
            {
                using var db = new AppDbContext();
                var existing = db.AppSettings.FirstOrDefault(x => x.Type == type);
                if (existing == null)
                {
                    existing = new AppSetting { Type = type, Value = json, IsActive = true, SortOrder = 0 };
                    db.AppSettings.Add(existing);
                }
                else
                {
                    existing.Value = json;
                    existing.IsActive = true;
                }
                db.SaveChanges();
                // continue to also write fallback file so template remains available if DB read later fails
            }
            catch
            {
                // fallback to file
            }

            try
            {
                if (!System.IO.Directory.Exists(AppDataPath)) System.IO.Directory.CreateDirectory(AppDataPath);
                var all = new Dictionary<string, List<ColumnDto>>();
                if (System.IO.File.Exists(FallbackFile))
                {
                    try { all = JsonSerializer.Deserialize<Dictionary<string, List<ColumnDto>>>(System.IO.File.ReadAllText(FallbackFile)) ?? new Dictionary<string, List<ColumnDto>>(); } catch { all = new Dictionary<string, List<ColumnDto>>(); }
                }
                all[type] = columns ?? new List<ColumnDto>();
                System.IO.File.WriteAllText(FallbackFile, JsonSerializer.Serialize(all));
            }
            catch { }
        }

        public List<ColumnDto> LoadTemplate(string modeName)
        {
            if (string.IsNullOrWhiteSpace(modeName)) return null;
            var type = SettingTypePrefix + modeName;
            try
            {
                using var db = new AppDbContext();
                var existing = db.AppSettings.FirstOrDefault(x => x.Type == type && x.IsActive);
                if (existing != null && !string.IsNullOrWhiteSpace(existing.Value))
                {
                    try { return JsonSerializer.Deserialize<List<ColumnDto>>(existing.Value); } catch { }
                }
            }
            catch
            {
                // ignore and try file fallback
            }

            try
            {
                if (!System.IO.File.Exists(FallbackFile)) return null;
                var all = JsonSerializer.Deserialize<Dictionary<string, List<ColumnDto>>>(System.IO.File.ReadAllText(FallbackFile));
                if (all != null && all.TryGetValue(type, out var list) && list != null) return list;
            }
            catch { }
            return null;
        }
    }
}

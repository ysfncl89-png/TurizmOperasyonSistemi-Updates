using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Services
{
    public static class NotificationCenter
    {
        public static event Action<string, object> NotificationSent;

        // Keep recent notifications so views that subscribe later can catch up
        private static readonly List<(string Key, object Payload, DateTime Timestamp)> _recent = new List<(string, object, DateTime)>();
        private static readonly object _recentLock = new object();
        private const int RecentMax = 200;

        // persistToDb: when false, do not save the notification to DB or recent in-memory list;
        // still invokes subscribers. Use for internal-only signals that should not be shown to users.
        public static void Publish(string key, object payload = null, bool persistToDb = true)
        {
            try
            {
                if (persistToDb)
                {
                    // record recent
                    try
                    {
                        lock (_recentLock)
                        {
                            _recent.Add((key, payload, DateTime.UtcNow));
                            if (_recent.Count > RecentMax) _recent.RemoveAt(0);
                        }
                    }
                    catch { }

                    // Persist to DB when possible
                    try
                    {
                        using (var db = new AppDbContext())
                        {
                            try { db.EnsureNotificationsTableExists(); } catch { }
                            var n = new Notification
                            {
                                Key = key,
                                Message = ExtractMessageFromPayload(payload),
                                PayloadJson = payload == null ? null : JsonSerializer.Serialize(payload),
                                IsRead = false,
                                CreatedAt = DateTime.UtcNow
                            };
                            db.Notifications.Add(n);
                            db.SaveChanges();
                        }
                    }
                    catch { }
                }

                NotificationSent?.Invoke(key, payload);
            }
            catch
            {
                // ignore errors from subscribers
            }
        }

        public static void Subscribe(Action<string, object> handler)
        {
            NotificationSent += handler;

            // Deliver recent notifications to new subscriber (so UI that wasn't open can catch up)
            try
            {
                // First try DB for unread notifications
                try
                {
                    using (var db = new AppDbContext())
                    {
                        try { db.EnsureNotificationsTableExists(); } catch { }
                        var unread = db.Notifications.Where(n => n.IsRead == false).OrderBy(n => n.CreatedAt).ToList();
                        foreach (var u in unread)
                        {
                            try { handler(u.Key, u); } catch { }
                        }
                    }
                }
                catch
                {
                    // Fallback to in-memory recent list
                    List<(string Key, object Payload)> copy;
                    lock (_recentLock)
                    {
                        copy = new List<(string, object)>(_recent.Count);
                        foreach (var item in _recent) copy.Add((item.Key, item.Payload));
                    }
                    foreach (var it in copy)
                    {
                        try { handler(it.Key, it.Payload); } catch { }
                    }
                }
            }
            catch { }
        }

        public class NotificationRecord
        {
            public string Key { get; set; }
            public object Payload { get; set; }
            public DateTime Timestamp { get; set; }
        }

        public static List<NotificationRecord> GetRecentNotifications()
        {
            try
            {
                // Prefer DB-backed notifications if available
                try
                {
                    using (var db = new AppDbContext())
                    {
                        try { db.EnsureNotificationsTableExists(); } catch { }
                        var rows = db.Notifications.OrderByDescending(x => x.CreatedAt).Take(RecentMax).ToList();
                        return rows.Select(x => new NotificationRecord { Key = x.Key, Payload = x, Timestamp = x.CreatedAt }).ToList();
                    }
                }
                catch
                {
                    // Fallback to in-memory
                    lock (_recentLock)
                    {
                        return _recent.Select(r => new NotificationRecord { Key = r.Key, Payload = r.Payload, Timestamp = r.Timestamp }).ToList();
                    }
                }
            }
            catch { return new List<NotificationRecord>(); }
        }

        public static void ClearRecent()
        {
            try
            {
                lock (_recentLock)
                {
                    _recent.Clear();
                }
            }
            catch { }
        }

        public static void MarkAsRead(int notificationId)
        {
            try
            {
                // mark in DB
                try
                {
                    using (var db = new AppDbContext())
                    {
                        try { db.EnsureNotificationsTableExists(); } catch { }
                        var n = db.Notifications.FirstOrDefault(x => x.Id == notificationId);
                        if (n != null)
                        {
                            n.IsRead = true;
                            db.SaveChanges();
                        }
                    }
                }
                catch { }

                // remove from in-memory recent list
                try
                {
                    lock (_recentLock)
                    {
                        _recent.RemoveAll(r =>
                        {
                            try
                            {
                                if (r.Payload is Models.Notification mn) return mn.Id == notificationId;
                                var pt = r.Payload?.GetType();
                                if (pt != null)
                                {
                                    var idprop = pt.GetProperty("Id");
                                    if (idprop != null)
                                    {
                                        var v = idprop.GetValue(r.Payload);
                                        if (v is int vi) return vi == notificationId;
                                    }
                                }
                            }
                            catch { }
                            return false;
                        });
                    }
                }
                catch { }
            }
            catch { }
        }

        public static void Unsubscribe(Action<string, object> handler)
        {
            NotificationSent -= handler;
        }

        private static string ExtractMessageFromPayload(object payload)
        {
            try
            {
                if (payload == null) return null;
                if (payload is string s) return s;
                var pt = payload.GetType();
                var msgProp = pt.GetProperty("Message");
                if (msgProp != null)
                {
                    var v = msgProp.GetValue(payload);
                    if (v != null) return v.ToString();
                }
                var nameProp = pt.GetProperty("Name");
                if (nameProp != null)
                {
                    var v = nameProp.GetValue(payload);
                    if (v != null) return v.ToString();
                }
                // fallback to ToString
                return payload.ToString();
            }
            catch { return null; }
        }
    }
}

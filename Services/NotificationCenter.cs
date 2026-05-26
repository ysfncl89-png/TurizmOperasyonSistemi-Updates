using System;

namespace TurizmOperasyonSistemi.Services
{
    public static class NotificationCenter
    {
        public static event Action<string, object> NotificationSent;

        public static void Publish(string key, object payload = null)
        {
            try
            {
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
        }

        public static void Unsubscribe(Action<string, object> handler)
        {
            NotificationSent -= handler;
        }
    }
}

using System;

namespace TurizmOperasyonSistemi.Services
{
    // Simple static bridge to signal when a card-remove button inside generated FixedDocument is clicked.
    public static class PrintPreviewInteraction
    {
        public static Action<int> RemoveRequested;

        public static void RequestRemove(int registrationId)
        {
            try { RemoveRequested?.Invoke(registrationId); } catch { }
        }
    }
}

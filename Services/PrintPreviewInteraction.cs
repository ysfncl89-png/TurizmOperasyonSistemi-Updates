using System;

namespace TurizmOperasyonSistemi.Services
{
    // Simple static bridge to signal when a card-remove button inside generated FixedDocument is clicked.
    public static class PrintPreviewInteraction
    {
        public static Action<int> RemoveRequested;
        // New: request to add another copy of a card for a registration
        public static Action<int> AddCopyRequested;

        public static void RequestRemove(int registrationId)
        {
            try { RemoveRequested?.Invoke(registrationId); } catch { }
        }

        public static void RequestAddCopy(int registrationId)
        {
            try { AddCopyRequested?.Invoke(registrationId); } catch { }
        }
    }
}

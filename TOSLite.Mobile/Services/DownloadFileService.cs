using CommunityToolkit.Maui.Storage;

namespace TOSLite.Mobile.Services;

public sealed class DownloadFileService
{
    public async Task SaveAsync(string fileName, string contentType, byte[] contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentNullException.ThrowIfNull(contents);
        if (contents.Length == 0)
            throw new InvalidDataException("Kaydedilecek dosya boş oluşturuldu.");

#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            await MainThread.InvokeOnMainThreadAsync(() => SaveToDownloads(fileName, contentType, contents));
            return;
        }
#endif
        using var source = new MemoryStream(contents);
        var result = await FileSaver.Default.SaveAsync(fileName, source);
        if (!result.IsSuccessful)
            throw result.Exception ?? new OperationCanceledException("Dosya kaydetme iptal edildi.");
    }

#if ANDROID
    [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
    private static void SaveToDownloads(string fileName, string contentType, byte[] contents)
    {
        var resolver = Android.App.Application.Context.ContentResolver
            ?? throw new InvalidOperationException("Dosya kaydetme servisi kullanılamıyor.");
        var values = new Android.Content.ContentValues();
        values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, contentType);
        values.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath, Android.OS.Environment.DirectoryDownloads);

        var uri = resolver.Insert(Android.Provider.MediaStore.Downloads.ExternalContentUri!, values)
            ?? throw new IOException("Dosya İndirilenler klasörüne eklenemedi.");

        try
        {
            using var output = resolver.OpenOutputStream(uri)
                ?? throw new IOException("İndirilen dosya açılamadı.");
            output.Write(contents, 0, contents.Length);
            output.Flush();
        }
        catch
        {
            resolver.Delete(uri, null, null);
            throw;
        }
    }
#endif
}

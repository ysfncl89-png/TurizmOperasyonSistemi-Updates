namespace TOSLite.Mobile.Services;

public sealed class DownloadFileService
{
    public async Task SaveAsync(string fileName, string contentType, byte[] contents)
    {
#if ANDROID
        await MainThread.InvokeOnMainThreadAsync(() =>
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
        });
#else
        var destination = await FilePicker.Default.SaveFileAsync(new PickOptions
        {
            PickerTitle = "Dışa aktarılan dosyayı kaydet",
            FileName = fileName
        });
        if (destination == null)
            throw new OperationCanceledException("Dosya kaydetme iptal edildi.");

        await using var outputStream = await destination.OpenWriteAsync();
        await outputStream.WriteAsync(contents);
#endif
    }
}

#if ANDROID
using Android.Gms.Extensions;
using Xamarin.Google.MLKit.Vision.Common;
using Xamarin.Google.MLKit.Vision.Text;
using Xamarin.Google.MLKit.Vision.Text.Latin;
#elif IOS || MACCATALYST
using Foundation;
using Vision;
#elif WINDOWS
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;
#endif

namespace TOSLite.Mobile.Services;

public interface IPassportTextRecognitionService
{
    bool IsSupported { get; }
    Task<string> RecognizeAsync(string imagePath);
}

public sealed class PassportTextRecognitionService : IPassportTextRecognitionService
{
    public bool IsSupported
    {
        get
        {
#if ANDROID || IOS || MACCATALYST || WINDOWS
            return true;
#else
            return false;
#endif
        }
    }

    public async Task<string> RecognizeAsync(string imagePath)
    {
#if ANDROID
        var image = InputImage.FromFilePath(Android.App.Application.Context, Android.Net.Uri.FromFile(new Java.IO.File(imagePath)));
        using var recognizer = Xamarin.Google.MLKit.Vision.Text.TextRecognition.GetClient(TextRecognizerOptions.DefaultOptions);
        var result = await recognizer.Process(image).AsAsync<Xamarin.Google.MLKit.Vision.Text.Text>();
        return string.Join(Environment.NewLine,
            result.TextBlocks.SelectMany(block => block.Lines).Select(line => line.Text));
#elif IOS || MACCATALYST
        using var request = new VNRecognizeTextRequest(null);
        request.RecognitionLevel = VNRequestTextRecognitionLevel.Accurate;
        request.UsesLanguageCorrection = false;
        using var handler = new VNImageRequestHandler(new NSUrl(imagePath), new VNImageOptions());
        if (!handler.Perform(new VNRequest[] { request }, out var error))
        {
            throw new InvalidOperationException(error?.LocalizedDescription ?? "Görüntü okunamadı.");
        }

        return string.Join(Environment.NewLine,
            request.Results?.Select(observation => observation.TopCandidates(1).FirstOrDefault()?.String)
                .Where(text => !string.IsNullOrWhiteSpace(text)) ?? []);
#elif WINDOWS
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException("Windows cihazında kullanılabilir bir OCR dili bulunamadı.");
        var file = await StorageFile.GetFileFromPathAsync(imagePath);
        using var stream = await file.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync();
        var result = await engine.RecognizeAsync(bitmap);
        return string.Join(Environment.NewLine, result.Lines.Select(line => line.Text));
#else
        throw new PlatformNotSupportedException("Bu cihazda yerel OCR desteklenmiyor.");
#endif
    }
}

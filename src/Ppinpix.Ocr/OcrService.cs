using Ppinpix.Core.Capture;
using Ppinpix.Core.Settings;
using Ppinpix.Ocr.Engines;
using Ppinpix.Ocr.Models;

namespace Ppinpix.Ocr;

/// <summary>对外统一的离线 OCR 与扫码识别服务。</summary>
public sealed class OcrService : IDisposable
{
    private readonly OcrWorker _worker;
    private Func<OcrSettings> _settingsAccessor;

    public OcrService(Func<OcrSettings> settingsAccessor)
    {
        _settingsAccessor = settingsAccessor;
        _worker = new OcrWorker(settingsAccessor());
    }

    public bool IsModelLoaded => _worker.IsAnyLoaded;

    public void NotifySettingsChanged(OcrSettings settings) => _worker.UpdateSettings(settings);

    public Task<OcrResult> RecognizeAsync(ScreenFrame frame, CancellationToken ct = default) =>
        _worker.ProcessAsync(frame, _settingsAccessor(), ct);

    public Task<OcrResult> RecognizeAsync(ScreenFrame frame, OcrSettings overrideSettings, CancellationToken ct = default) =>
        _worker.ProcessAsync(frame, overrideSettings, ct);

    public void UnloadModels() => _worker.UnloadAll();

    public static bool HasCustomRapidOcrModels() => RapidOcrEngine.HasCustomModelFiles();

    public static IReadOnlyList<(string Tag, string DisplayName)> GetSystemOcrLanguages() =>
        WindowsOcrEngine.GetAvailableLanguages();

    public void Dispose() => _worker.Dispose();
}

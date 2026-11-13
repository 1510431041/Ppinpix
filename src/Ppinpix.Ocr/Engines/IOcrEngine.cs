using Ppinpix.Core.Capture;
using Ppinpix.Ocr.Models;

namespace Ppinpix.Ocr.Engines;

public sealed record OcrOptions(IReadOnlyList<string> Languages, int MaxSidePx = 1600);

/// <summary>离线 OCR 识别引擎统一抽象。</summary>
public interface IOcrEngine : IDisposable
{
    string Name { get; }

    bool IsLoaded { get; }

    bool IsAvailable { get; }

    Task<IReadOnlyList<TextLine>> RecognizeAsync(ScreenFrame frame, OcrOptions options, CancellationToken ct);

    void Unload();
}

using System.Diagnostics;
using Ppinpix.Core;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Settings;
using Ppinpix.Ocr.Barcode;
using Ppinpix.Ocr.Engines;
using Ppinpix.Ocr.Models;
using Ppinpix.Ocr.Processing;

namespace Ppinpix.Ocr;

/// <summary>
/// 后台串行化的 OCR 推理调度与自动空闲卸载协调器。
/// 永不阻塞 UI 线程，也禁止在软件启动阶段预加载模型。
/// </summary>
public sealed class OcrWorker : IDisposable
{
    private readonly SemaphoreSlim _workerLock = new(1, 1);
    private readonly RapidOcrEngine _rapidEngine = new();
    private readonly WindowsOcrEngine _windowsEngine = new();
    private readonly System.Timers.Timer _idleTimer;

    private OcrSettings _settings;
    private bool _disposed;

    public OcrWorker(OcrSettings settings)
    {
        _settings = settings;
        _idleTimer = new System.Timers.Timer(60_000) { AutoReset = false };
        _idleTimer.Elapsed += (_, _) => UnloadAll();
    }

    public bool IsAnyLoaded => _rapidEngine.IsLoaded || _windowsEngine.IsLoaded;

    public void UpdateSettings(OcrSettings settings)
    {
        _settings = settings;
        ResetIdleTimer();
    }

    public async Task<OcrResult> ProcessAsync(ScreenFrame frame, OcrSettings? overrideSettings = null, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var settings = overrideSettings ?? _settings;

        var sw = Stopwatch.StartNew();

        // 二维码/条形码与文字识别并行执行（完全离线）
        Task<IReadOnlyList<Models.Barcode>> barcodeTask = settings.DetectBarcodes
            ? Task.Run(() => ZxingDecoder.Decode(frame), ct)
            : Task.FromResult<IReadOnlyList<Models.Barcode>>([]);

        await _workerLock.WaitAsync(ct).ConfigureAwait(false);
        IReadOnlyList<TextLine> lines = [];
        string usedEngine = string.Empty;
        try
        {
            _idleTimer.Stop();
            var primary = SelectEngine(settings.Engine);
            var fallback = ReferenceEquals(primary, _rapidEngine) ? (IOcrEngine)_windowsEngine : _rapidEngine;

            var opts = new OcrOptions(settings.Languages, ImagePreprocessor.DefaultMaxSidePx);
            try
            {
                if (primary.IsAvailable)
                {
                    lines = await primary.RecognizeAsync(frame, opts, ct).ConfigureAwait(false);
                    usedEngine = primary.Name;
                }
                else if (fallback.IsAvailable)
                {
                    lines = await fallback.RecognizeAsync(frame, opts, ct).ConfigureAwait(false);
                    usedEngine = fallback.Name;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (fallback.IsAvailable)
            {
                Log.Warn($"主 OCR 引擎 {primary.Name} 执行失败，回退至 {fallback.Name}：{ex.Message}");
                lines = await fallback.RecognizeAsync(frame, opts, ct).ConfigureAwait(false);
                usedEngine = fallback.Name;
            }
        }
        finally
        {
            _workerLock.Release();
            ResetIdleTimer();
        }

        IReadOnlyList<Models.Barcode> barcodes = await barcodeTask.ConfigureAwait(false);
        var paragraphs = ParagraphGrouper.Group(lines);
        sw.Stop();

        return new OcrResult(lines, paragraphs, barcodes, usedEngine, sw.ElapsedMilliseconds);
    }

    public void UnloadAll()
    {
        if (_disposed) return;
        _idleTimer.Stop();
        _rapidEngine.Unload();
        _windowsEngine.Unload();
    }

    private IOcrEngine SelectEngine(OcrEngineKind kind) => kind switch
    {
        OcrEngineKind.WindowsSystem => _windowsEngine,
        _ => _rapidEngine,
    };

    private void ResetIdleTimer()
    {
        _idleTimer.Stop();
        int minutes = _settings.UnloadAfterIdleMinutes;
        if (minutes > 0 && IsAnyLoaded && !_disposed)
        {
            _idleTimer.Interval = minutes * 60_000.0;
            _idleTimer.Start();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _idleTimer.Dispose();
        _rapidEngine.Dispose();
        _windowsEngine.Dispose();
        _workerLock.Dispose();
    }
}

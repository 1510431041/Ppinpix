using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Ppinpix.Core;
using Ppinpix.Core.Capture;
using Ppinpix.Ocr.Models;
using Ppinpix.Ocr.Processing;
using RapidOcrNet;
using SkiaSharp;

namespace Ppinpix.Ocr.Engines;

/// <summary>
/// 基于 RapidOcrNet (ONNX Runtime) 的离线 OCR 引擎。
/// 优先加载用户自定义或应用 models 目录下的多语种 ONNX 模型，未放置时回退至随包内置模型。
/// 支持懒加载与空闲卸载以降低常驻内存占用。
/// </summary>
public sealed class RapidOcrEngine : IOcrEngine
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private RapidOcr? _ocr;
    private bool _disposed;

    public string Name => HasCustomModelFiles() ? "RapidOCR (Custom ONNX)" : "RapidOCR (Built-in PP-OCRv5)";

    public bool IsLoaded => _ocr is not null;

    public bool IsAvailable
    {
        get
        {
            if (HasCustomModelFiles()) return true;
            string bundledDir = Path.Combine(AppContext.BaseDirectory, "models");
            return Directory.Exists(bundledDir);
        }
    }

    public static bool HasCustomModelFiles() => TryFindCustomModels() is not null;

    public async Task<IReadOnlyList<Models.TextLine>> RecognizeAsync(ScreenFrame frame, OcrOptions options, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Pixels.IsEmpty)
        {
            return [];
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            var ocr = EnsureInitialized();

            using SKBitmap fullBmp = ImagePreprocessor.ToSkBitmap(frame);
            var (ocrBmp, scaleX, scaleY, owns) = ImagePreprocessor.PrepareForOcr(fullBmp, options.MaxSidePx);
            try
            {
                var rapidOpts = RapidOcrOptions.Default with
                {
                    ReturnWordBox = true,
                    ReturnSingleCharBox = true,
                    DoAngle = true,
                };

                RapidOcrNet.OcrResult raw = await ocr.DetectAsync(ocrBmp, rapidOpts, ct).ConfigureAwait(false);
                if (raw.TextBlocks is null || raw.TextBlocks.Length == 0)
                {
                    return [];
                }

                var lines = new List<Models.TextLine>(raw.TextBlocks.Length);
                foreach (var block in raw.TextBlocks)
                {
                    ct.ThrowIfCancellationRequested();
                    string text = block.Text?.Trim() ?? string.Empty;
                    if (text.Length == 0) continue;

                    var lineBox = PointsToQuad(block.BoxPoints).Scale(scaleX);
                    if (Math.Abs(scaleX - scaleY) > 1e-6)
                    {
                        lineBox = ScaleQuadXY(PointsToQuad(block.BoxPoints), scaleX, scaleY);
                    }

                    var charBoxes = ExtractCharBoxes(block, lineBox, scaleX, scaleY);
                    float conf = block.CharScores is { Length: > 0 }
                        ? block.CharScores.Average()
                        : block.BoxScore;

                    lines.Add(new Models.TextLine(text, lineBox, charBoxes, conf));
                }

                return lines;
            }
            finally
            {
                if (owns) ocrBmp.Dispose();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Unload()
    {
        _gate.Wait();
        try
        {
            if (_ocr is not null)
            {
                _ocr.Dispose();
                _ocr = null;
                Log.Info("RapidOCR 模型已按空闲策略释放。");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private RapidOcr EnsureInitialized()
    {
        if (_ocr is not null) return _ocr;

        var sw = Stopwatch.StartNew();
        var ocr = new RapidOcr();
        using var sessionOptions = RapidOcr.GetDefaultSessionOptions(numThread: Math.Clamp(Environment.ProcessorCount / 2, 1, 4));

        var custom = TryFindCustomModels();
        if (custom is not null)
        {
            ocr.InitModels(custom.Value.Det, custom.Value.Cls, custom.Value.Rec, custom.Value.Keys, sessionOptions);
            Log.Info($"RapidOCR 已加载自定义 ONNX 模型（耗时 {sw.ElapsedMilliseconds}ms）：{Path.GetDirectoryName(custom.Value.Det)}");
        }
        else
        {
            ocr.InitModels(sessionOptions);
            Log.Info($"RapidOCR 已加载内置模型（耗时 {sw.ElapsedMilliseconds}ms）。");
        }

        _ocr = ocr;
        return _ocr;
    }

    private static (string Det, string Cls, string Rec, string Keys)? TryFindCustomModels()
    {
        string[] searchDirs =
        [
            Paths.UserModelsDirectory,
            Paths.ModelsDirectory,
            Path.Combine(AppContext.BaseDirectory, "custom_models"),
        ];

        foreach (string dir in searchDirs)
        {
            if (!Directory.Exists(dir)) continue;

            string? det = FindFirst(dir, ["det.onnx", "*det*.onnx"]);
            string? cls = FindFirst(dir, ["cls.onnx", "*cls*.onnx"]);
            string? rec = FindFirst(dir, ["rec.onnx", "*rec*.onnx"]);
            string? keys = FindFirst(dir, ["keys.txt", "*keys*.txt", "*.txt"]);

            if (det is not null && cls is not null && rec is not null && keys is not null)
            {
                return (det, cls, rec, keys);
            }
        }

        return null;
    }

    private static string? FindFirst(string dir, string[] patterns)
    {
        foreach (string p in patterns)
        {
            string? match = Directory.EnumerateFiles(dir, p, SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (match is not null) return match;
        }
        return null;
    }

    private static List<CharBox> ExtractCharBoxes(TextBlock block, Quad scaledLineBox, double scaleX, double scaleY)
    {
        if (block.WordResults is { Length: > 0 } words)
        {
            var chars = new List<CharBox>();
            foreach (var w in words)
            {
                if (string.IsNullOrEmpty(w.Text)) continue;
                var wb = ScaleQuadXY(PointsToQuad(w.BoxPoints), scaleX, scaleY);
                if (w.Text.Length == 1)
                {
                    chars.Add(new CharBox(w.Text, wb, w.Score));
                }
                else
                {
                    double cw = wb.Width / w.Text.Length;
                    for (int i = 0; i < w.Text.Length; i++)
                    {
                        var cb = Quad.FromRect(wb.MinX + i * cw, wb.MinY, cw, wb.Height);
                        chars.Add(new CharBox(w.Text[i].ToString(), cb, w.Score));
                    }
                }
            }
            if (chars.Count > 0) return chars;
        }

        string text = block.Text?.Trim() ?? string.Empty;
        if (text.Length == 0) return [];
        var fallback = new List<CharBox>(text.Length);
        double avgW = scaledLineBox.Width / text.Length;
        for (int i = 0; i < text.Length; i++)
        {
            var box = Quad.FromRect(scaledLineBox.MinX + i * avgW, scaledLineBox.MinY, avgW, scaledLineBox.Height);
            fallback.Add(new CharBox(text[i].ToString(), box, block.BoxScore));
        }
        return fallback;
    }

    private static Quad PointsToQuad(SKPointI[]? pts)
    {
        if (pts is null || pts.Length < 4) return default;
        return new Quad(
            pts[0].X, pts[0].Y,
            pts[1].X, pts[1].Y,
            pts[2].X, pts[2].Y,
            pts[3].X, pts[3].Y);
    }

    private static Quad ScaleQuadXY(Quad q, double sx, double sy) =>
        new(q.X1 * sx, q.Y1 * sy, q.X2 * sx, q.Y2 * sy,
            q.X3 * sx, q.Y3 * sy, q.X4 * sx, q.Y4 * sy);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unload();
        _gate.Dispose();
    }
}

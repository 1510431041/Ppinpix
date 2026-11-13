using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using Ppinpix.Capture.Output;
using Ppinpix.Capture.Overlay;
using Ppinpix.Core;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Display;
using Ppinpix.Core.Geometry;
using Ppinpix.Core.Settings;
using Ppinpix.Ocr;
using Ppinpix.Ocr.Models;
using SkiaSharp;

namespace Ppinpix.Pin;

/// <summary>贴图窗口管理器：创建贴图（区域/剪贴板图文文件）、数量上限控制、显示/隐藏切换、Ctrl+Alt 穿透钩子与重启会话恢复。</summary>
public sealed class PinManager : IDisposable
{
    private readonly Func<AppSettings> _settingsAccessor;
    private readonly DisplayService _displays;
    private readonly OcrService? _ocrService;
    private readonly LowLevelKeyboardHook _hook = new();
    private readonly List<PinWindow> _pins = [];

    private Func<PinWindow, ScreenFrame, OcrResult?, Task>? _onOpenOcr;
    private Func<PinWindow, ScreenFrame, Task>? _onOpenTranslate;
    private Action<PinWindow>? _onCancelTranslate;
    private Func<PinWindow, ScreenFrame, OcrResult?, Task>? _onOpenCompareTranslate;
    private Func<IReadOnlyList<(string Code, string DisplayName)>>? _getTranslateLanguages;
    private Func<PinWindow, ScreenFrame, string, Task>? _onRetranslateWithLang;
    private Func<string>? _getActiveTargetLanguage;
    private bool _allHidden;
    private bool _disposed;

    public PinManager(Func<AppSettings> settingsAccessor, DisplayService displays, OcrService? ocrService)
    {
        _settingsAccessor = settingsAccessor;
        _displays = displays;
        _ocrService = ocrService;
        _hook.CtrlAltChanged += OnCtrlAltChanged;
    }

    public IReadOnlyList<PinWindow> Pins => _pins;

    public bool IsHookInstalled => _hook.IsInstalled;

    public void ConfigureCallbacks(
        Func<PinWindow, ScreenFrame, OcrResult?, Task>? onOpenOcr,
        Func<PinWindow, ScreenFrame, Task>? onOpenTranslate,
        Action<PinWindow>? onCancelTranslate = null,
        Func<PinWindow, ScreenFrame, OcrResult?, Task>? onOpenCompareTranslate = null,
        Func<IReadOnlyList<(string Code, string DisplayName)>>? getTranslateLanguages = null,
        Func<PinWindow, ScreenFrame, string, Task>? onRetranslateWithLang = null,
        Func<string>? getActiveTargetLanguage = null)
    {
        _onOpenOcr = onOpenOcr;
        _onOpenTranslate = onOpenTranslate;
        _onCancelTranslate = onCancelTranslate;
        _onOpenCompareTranslate = onOpenCompareTranslate;
        _getTranslateLanguages = getTranslateLanguages;
        _onRetranslateWithLang = onRetranslateWithLang;
        _getActiveTargetLanguage = getActiveTargetLanguage;
    }

    public PinWindow CreateFromFrame(
        ScreenFrame frame,
        PinKind kind = PinKind.Image,
        string? textContent = null,
        bool deferSilentOcr = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var settings = _settingsAccessor();
        EnforceMaxCount(settings.Pin.MaxPinCount);

        var pin = CreatePinWindow(frame, kind, textContent, settings, restored: null, deferSilentOcr);

        RegisterAndShow(pin);
        return pin;
    }

    public PinWindow? CreateFromClipboard()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            if (Clipboard.ContainsImage())
            {
                BitmapSource? bmp = Clipboard.GetImage();
                if (bmp is not null && bmp.PixelWidth > 0 && bmp.PixelHeight > 0)
                {
                    var frame = CaptureOutput.FromBitmapSource(bmp);
                    var placed = PlaceNearCursor(frame);
                    return CreateFromFrame(placed, PinKind.Image);
                }
            }

            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                foreach (string? file in files)
                {
                    if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) continue;
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp")
                    {
                        var frame = CaptureOutput.LoadFromFile(file);
                        if (frame is not null && frame.Width > 0 && frame.Height > 0)
                        {
                            var placed = PlaceNearCursor(frame);
                            return CreateFromFrame(placed, PinKind.Image);
                        }
                    }
                }
            }

            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var frame = RenderTextCard(text);
                    var placed = PlaceNearCursor(frame);
                    return CreateFromFrame(placed, PinKind.Text, textContent: text);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("从剪贴板创建贴图失败。", ex);
        }

        ToastWindow.Show("剪贴板中没有可贴图的内容（图片、图片文件或文本）。", ToastKind.Info, _displays);
        return null;
    }

    public void ToggleShowHideAll()
    {
        if (_pins.Count == 0) return;

        _allHidden = !_allHidden;
        foreach (var p in _pins)
        {
            if (_allHidden) p.Hide();
            else if (!p.IsHiddenByUser) p.Show();
        }
    }

    public void ShowAll()
    {
        _allHidden = false;
        foreach (var p in _pins)
        {
            p.SetHiddenByUser(false);
        }
    }

    public void CloseAll()
    {
        foreach (var p in _pins.ToList())
        {
            p.Close();
        }
        _pins.Clear();
        SyncHookState();
    }

    public void SaveSession()
    {
        var settings = _settingsAccessor();
        if (!settings.Pin.RestoreOnStartup)
        {
            PinStorage.Clear();
            return;
        }

        var items = new List<(SavedPinRecord Record, ScreenFrame Frame)>(_pins.Count);
        foreach (var pin in _pins)
        {
            var frame = pin.ExportRecord(out var rec);
            items.Add((rec, frame));
        }
        PinStorage.SaveAll(items);
    }

    public void RestoreSession()
    {
        var settings = _settingsAccessor();
        if (!settings.Pin.RestoreOnStartup) return;

        var saved = PinStorage.LoadAll();
        foreach (var (rec, frame) in saved.Take(settings.Pin.MaxPinCount))
        {
            var pin = CreatePinWindow(frame, rec.Kind, rec.TextContent, settings, restored: rec, deferSilentOcr: false);
            RegisterAndShow(pin);
        }
    }

    private PinWindow CreatePinWindow(
        ScreenFrame frame,
        PinKind kind,
        string? textContent,
        AppSettings settings,
        SavedPinRecord? restored,
        bool deferSilentOcr) =>
        new(
            frame,
            kind,
            textContent,
            settings,
            _displays,
            _ocrService,
            onOpenOcr: (w, f, ocr) => _ = _onOpenOcr?.Invoke(w, f, ocr),
            onOpenTranslate: (w, f) => _ = _onOpenTranslate?.Invoke(w, f),
            restored: restored,
            deferSilentOcr: deferSilentOcr,
            onCancelTranslate: _onCancelTranslate,
            onOpenCompareTranslate: (w, f, ocr) => _ = _onOpenCompareTranslate?.Invoke(w, f, ocr),
            getTranslateLanguages: _getTranslateLanguages,
            onRetranslateWithLang: (w, f, lang) => _ = _onRetranslateWithLang?.Invoke(w, f, lang),
            getActiveTargetLanguage: _getActiveTargetLanguage);

    private void RegisterAndShow(PinWindow pin)
    {
        _pins.Add(pin);
        pin.PinStateChanged += SyncHookState;
        pin.Closed += (_, _) =>
        {
            _pins.Remove(pin);
            SyncHookState();
        };
        pin.Show();
        SyncHookState();
    }

    private void EnforceMaxCount(int maxCount)
    {
        while (_pins.Count >= maxCount && _pins.Count > 0)
        {
            var oldest = _pins[0];
            oldest.Close();
        }
    }

    private void SyncHookState()
    {
        bool anyClickThrough = _pins.Any(p => p.IsClickThrough);
        if (anyClickThrough) _hook.Install();
        else _hook.Uninstall();
    }

    private void OnCtrlAltChanged(bool ctrlAltDown)
    {
        foreach (var p in _pins)
        {
            if (p.IsClickThrough)
            {
                p.SetTemporaryInteractive(ctrlAltDown);
            }
        }
    }

    private ScreenFrame PlaceNearCursor(ScreenFrame frame)
    {
        if (!Ppinpix.Core.Input.GlobalPointer.TryGetPosition(out var cur))
        {
            cur = _displays.VirtualBounds.Center;
        }
        var monitor = _displays.FromPoint(cur);
        var work = monitor.WorkArea;
        int x = Math.Clamp(cur.X + 16, work.Left + 8, Math.Max(work.Left + 8, work.Right - frame.Width - 8));
        int y = Math.Clamp(cur.Y + 16, work.Top + 8, Math.Max(work.Top + 8, work.Bottom - frame.Height - 8));
        return new ScreenFrame(new PxRect(x, y, frame.Width, frame.Height), frame.Stride, frame.Pixels.ToArray());
    }

    /// <summary>将剪贴板纯文本按高对比度深色卡片渲染为 ScreenFrame，使其天然享受缩放/旋转/穿透/持久化能力。</summary>
    public static ScreenFrame RenderTextCard(string text)
    {
        string normalized = text.Replace("\r\n", "\n").TrimEnd('\n');
        const float fontSize = 15f;
        const float lineHeight = 22f;
        const float padX = 16f;
        const float padY = 12f;
        const int maxCardWidth = 640;

        using var typeface = SKTypeface.FromFamilyName(
            "Consolas",
            SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        using var font = new SKFont(typeface, fontSize) { Edging = SKFontEdging.Antialias };

        var wrapped = WrapLines(normalized, font, maxCardWidth - padX * 2);
        if (wrapped.Count == 0) wrapped.Add(" ");
        if (wrapped.Count > 45)
        {
            wrapped = wrapped.Take(44).Append("…（内容已截断）").ToList();
        }

        float maxW = 60f;
        foreach (string line in wrapped)
        {
            float lw = font.MeasureText(line);
            if (lw > maxW) maxW = lw;
        }

        int width = Math.Clamp((int)Math.Ceiling(maxW + padX * 2), 96, maxCardWidth);
        int height = Math.Max(48, (int)Math.Ceiling(wrapped.Count * lineHeight + padY * 2));

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0x1F, 0x23, 0x29));

        using var paint = new SKPaint
        {
            Color = new SKColor(0xF2, 0xF4, 0xF7),
            IsAntialias = true,
        };
        var metrics = font.Metrics;
        float y = padY - metrics.Ascent;
        foreach (string line in wrapped)
        {
            if (line.Length > 0)
            {
                canvas.DrawText(line, padX, y, SKTextAlign.Left, font, paint);
            }
            y += lineHeight;
        }
        canvas.Flush();

        using var img = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(img);
        int stride = width * 4;
        byte[] bgra = new byte[stride * height];
        IntPtr src = bmp.GetPixels();
        for (int row = 0; row < height; row++)
        {
            Marshal.Copy(src + row * bmp.RowBytes, bgra, row * stride, stride);
        }

        return new ScreenFrame(new PxRect(0, 0, width, height), stride, bgra);
    }

    private static List<string> WrapLines(string text, SKFont font, float maxWidth)
    {
        var result = new List<string>();
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Replace("\t", "    ");
            if (line.Length == 0 || font.MeasureText(line) <= maxWidth)
            {
                result.Add(line);
                continue;
            }

            int start = 0;
            while (start < line.Length)
            {
                int len = 1;
                while (start + len <= line.Length && font.MeasureText(line.AsSpan(start, len)) <= maxWidth)
                {
                    len++;
                }
                int fit = Math.Max(1, len - 1);
                result.Add(line.Substring(start, fit));
                start += fit;
            }
        }
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hook.CtrlAltChanged -= OnCtrlAltChanged;
        _hook.Dispose();
        CloseAll();
    }
}

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ppinpix.Capture.Annotation;
using Ppinpix.Capture.Interop;
using Ppinpix.Capture.Output;
using Ppinpix.Capture.Overlay;
using Ppinpix.Core;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Display;
using Ppinpix.Core.Geometry;
using Ppinpix.Core.Input;
using Ppinpix.Core.Interop;
using Ppinpix.Core.Settings;
using Ppinpix.Ocr;
using Ppinpix.Ocr.Models;
using Ppinpix.Pin.TextSelection;
using Ppinpix.Theme;

namespace Ppinpix.Pin;

public partial class PinWindow : Window
{
    private const double ShadowPadDip = 10;

    private readonly AppSettings _settings;
    private readonly DisplayService _displays;
    private readonly OcrService? _ocrService;
    private readonly Action<PinWindow, ScreenFrame, OcrResult?>? _onOpenOcr;
    private readonly Action<PinWindow, ScreenFrame>? _onOpenTranslate;
    private readonly Action<PinWindow>? _onCancelTranslate;
    private readonly Action<PinWindow, ScreenFrame, OcrResult?>? _onOpenCompareTranslate;
    private readonly Func<IReadOnlyList<(string Code, string DisplayName)>>? _getTranslateLanguages;
    private readonly Action<PinWindow, ScreenFrame, string>? _onRetranslateWithLang;
    private readonly Func<string>? _getActiveTargetLanguage;
    private readonly bool _suppressInitialSilentOcr;
    private readonly TextSelectionLayer _textLayer = new();
    private readonly DispatcherTimer _badgeTimer;
    private readonly DispatcherTimer _translateBadgeTimer;

    private ScreenFrame _sourceFrame;
    private ScreenFrame? _originalOverlaySource;
    private OcrResult? _originalOcr;
    private bool _hasOverlayTranslation;
    private bool _isTranslating;
    private bool _showingOriginalAlt;
    private bool _showingOriginalLocked;
    private BitmapSource? _displayBitmap;
    private CancellationTokenSource? _ocrCts;
    private OcrResult? _lastOcr;

    private double _dpiScale = 1.0;
    private double _zoom = 1.0;
    private double _targetOpacity = 1.0;
    private int _rotationQuarterTurns;
    private bool _flipH;
    private bool _flipV;
    private bool _isTopmost = true;
    private bool _isClickThrough;
    private bool _tempInteractive;
    private bool _isCollapsed;
    private bool _isHiddenByUser;
    private PxPoint _windowPosPx;
    private double _shadowPadDip;

    private bool _draggingWindow;
    private PxPoint _dragStartCursorPx;
    private PxPoint _dragStartWindowPx;

    public PinWindow(
        ScreenFrame sourceFrame,
        PinKind kind,
        string? textContent,
        AppSettings settings,
        DisplayService displays,
        OcrService? ocrService,
        Action<PinWindow, ScreenFrame, OcrResult?>? onOpenOcr,
        Action<PinWindow, ScreenFrame>? onOpenTranslate,
        SavedPinRecord? restored = null,
        bool deferSilentOcr = false,
        Action<PinWindow>? onCancelTranslate = null,
        Action<PinWindow, ScreenFrame, OcrResult?>? onOpenCompareTranslate = null,
        Func<IReadOnlyList<(string Code, string DisplayName)>>? getTranslateLanguages = null,
        Action<PinWindow, ScreenFrame, string>? onRetranslateWithLang = null,
        Func<string>? getActiveTargetLanguage = null)
    {
        InitializeComponent();

        Id = restored?.Id ?? Guid.NewGuid().ToString("n");
        Kind = kind;
        TextContent = textContent;
        Alias = restored?.Alias;

        _sourceFrame = CloneFrameOriginZero(sourceFrame);

        _settings = settings;
        _displays = displays;
        _ocrService = ocrService;
        _onOpenOcr = onOpenOcr;
        _onOpenTranslate = onOpenTranslate;
        _onCancelTranslate = onCancelTranslate;
        _onOpenCompareTranslate = onOpenCompareTranslate;
        _getTranslateLanguages = getTranslateLanguages;
        _onRetranslateWithLang = onRetranslateWithLang;
        _getActiveTargetLanguage = getActiveTargetLanguage;
        _suppressInitialSilentOcr = deferSilentOcr && restored is null;

        _shadowPadDip = settings.Pin.ShowShadow ? ShadowPadDip : 0;
        if (_shadowPadDip > 0)
        {
            OuterGrid.Margin = new Thickness(_shadowPadDip);
            UiFactory.ApplyShadow(CardBorder, blur: 18, depth: 4, opacity: 0.45);
            UiFactory.ApplyShadow(CollapsedBar, blur: 14, depth: 3, opacity: 0.42);
        }

        _zoom = restored is not null ? Math.Clamp(restored.Zoom, 0.1, 10.0) : 1.0;
        _targetOpacity = restored is not null ? Math.Clamp(restored.Opacity, 0.1, 1.0) : Math.Clamp(settings.Pin.DefaultOpacity, 0.1, 1.0);
        _rotationQuarterTurns = ((restored?.RotationQuarterTurns ?? 0) % 4 + 4) % 4;
        _flipH = restored?.FlipH ?? false;
        _flipV = restored?.FlipV ?? false;
        _isTopmost = restored?.Topmost ?? true;
        _isClickThrough = restored?.ClickThrough ?? false;
        _isCollapsed = restored?.Collapsed ?? false;
        _isHiddenByUser = restored?.Hidden ?? false;

        _windowPosPx = restored is not null
            ? new PxPoint(restored.X, restored.Y)
            : new PxPoint(sourceFrame.Bounds.X, sourceFrame.Bounds.Y);

        Topmost = _isTopmost;
        Opacity = _targetOpacity;

        TextLayerHost.Content = _textLayer;
        _badgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _badgeTimer.Tick += (_, _) =>
        {
            _badgeTimer.Stop();
            var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
            StatusBadge.BeginAnimation(OpacityProperty, anim);
        };

        _translateBadgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
        _translateBadgeTimer.Tick += (_, _) =>
        {
            _translateBadgeTimer.Stop();
            var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
            anim.Completed += (_, _) =>
            {
                if (TranslateBadge.Opacity <= 0.01)
                {
                    TranslateBadge.Visibility = Visibility.Collapsed;
                }
            };
            TranslateBadge.BeginAnimation(OpacityProperty, anim);
        };

        HookInputEvents();
        RefreshDisplaySurface();
        UpdateTitleUi();

        Loaded += OnLoaded;
        Closed += OnClosedCleanup;
    }

    public string Id { get; }

    public PinKind Kind { get; }

    public string? TextContent { get; }

    public string? Alias { get; private set; }

    public bool IsClickThrough => _isClickThrough;

    public bool IsHiddenByUser => _isHiddenByUser;

    public bool IsCollapsed => _isCollapsed;

    public OcrResult? LastOcr => _lastOcr;

    public bool HasOverlayTranslation => _hasOverlayTranslation;

    public bool IsTranslating => _isTranslating;

    public OcrResult? OriginalOcr => _originalOcr;

    public event Action? PinStateChanged;

    public void SetOcrCache(OcrResult ocr)
    {
        var safe = ocr ?? OcrResult.Empty;
        _lastOcr = safe;
        if (!_hasOverlayTranslation)
        {
            _originalOcr = safe;
        }
        _textLayer.SetOcrResult(safe, _sourceFrame.Width, _sourceFrame.Height);
    }

    /// <summary>进入原图原位翻译流式流程前锁定原始基底图像并返回一份只读快照。</summary>
    public ScreenFrame BeginOverlayTranslation()
    {
        _ocrCts?.Cancel();
        _ocrCts?.Dispose();
        _ocrCts = null;

        if (!_hasOverlayTranslation)
        {
            if (_rotationQuarterTurns != 0 || _flipH || _flipV)
            {
                _sourceFrame = BuildEffectiveFrame();
                _rotationQuarterTurns = 0;
                _flipH = false;
                _flipV = false;
                _lastOcr = null;
                _originalOcr = null;
            }
            else if (_lastOcr is not null)
            {
                _originalOcr ??= _lastOcr;
            }

            _originalOverlaySource = CloneFrameOriginZero(_sourceFrame);
        }
        else if (_originalOverlaySource is null)
        {
            _originalOverlaySource = CloneFrameOriginZero(_sourceFrame);
        }

        _isTranslating = true;
        _showingOriginalAlt = false;
        _showingOriginalLocked = false;
        _textLayer.ClearSelection();
        RefreshDisplaySurface();
        return CloneFrameOriginZero(_originalOverlaySource);
    }

    public void SetTranslationStatus(string text, int autoHideMs = 0) =>
        ShowTranslateBadge(text, showStop: _isTranslating, autoHideMs: autoHideMs);

    public void ApplyOverlayTranslationProgress(ScreenFrame renderedFrame, string? statusText = null)
    {
        _sourceFrame = CloneFrameOriginZero(renderedFrame);
        _hasOverlayTranslation = true;
        RefreshDisplaySurface();
        if (!string.IsNullOrEmpty(statusText))
        {
            ShowTranslateBadge(statusText, showStop: _isTranslating, autoHideMs: 0);
        }
    }

    public void CompleteOverlayTranslation(
        ScreenFrame finalFrame,
        string? completedBadgeText = "翻译完成 · 按住 Alt 对比原图")
    {
        _sourceFrame = CloneFrameOriginZero(finalFrame);
        _hasOverlayTranslation = true;
        _isTranslating = false;
        _lastOcr = null;
        _textLayer.SetOcrResult(OcrResult.Empty, _sourceFrame.Width, _sourceFrame.Height);

        RefreshDisplaySurface();
        StartSilentOcr();

        if (!string.IsNullOrEmpty(completedBadgeText))
        {
            ShowTranslateBadge(completedBadgeText, showStop: false, autoHideMs: 2200);
        }
        else
        {
            HideTranslateBadge();
        }
        PinStateChanged?.Invoke();
    }

    public void FinishOverlayWithoutChanges(string? statusBadgeText = null, int autoHideMs = 2400)
    {
        _isTranslating = false;
        if (!_hasOverlayTranslation)
        {
            _originalOverlaySource = null;
            if (_lastOcr is null)
            {
                StartSilentOcr();
            }
        }

        RefreshDisplaySurface();
        if (!string.IsNullOrEmpty(statusBadgeText))
        {
            ShowTranslateBadge(statusBadgeText, showStop: false, autoHideMs: autoHideMs);
        }
        else
        {
            HideTranslateBadge();
        }
    }

    public ScreenFrame GetCompareSourceFrame() =>
        _hasOverlayTranslation && _originalOverlaySource is not null
            ? BuildTransformedFrame(_originalOverlaySource)
            : BuildEffectiveFrame();

    public ScreenFrame ExportRecord(out SavedPinRecord record)
    {
        var frameCopy = new ScreenFrame(
            new PxRect(0, 0, _sourceFrame.Width, _sourceFrame.Height),
            _sourceFrame.Stride,
            _sourceFrame.Pixels.ToArray());

        record = new SavedPinRecord
        {
            Id = Id,
            Kind = Kind,
            TextContent = TextContent,
            Alias = Alias,
            X = _windowPosPx.X,
            Y = _windowPosPx.Y,
            Zoom = _zoom,
            Opacity = _targetOpacity,
            RotationQuarterTurns = _rotationQuarterTurns,
            FlipH = _flipH,
            FlipV = _flipV,
            Topmost = _isTopmost,
            ClickThrough = _isClickThrough,
            Collapsed = _isCollapsed,
            Hidden = _isHiddenByUser,
        };
        return frameCopy;
    }

    public void SetTemporaryInteractive(bool interactive)
    {
        if (_tempInteractive == interactive) return;
        _tempInteractive = interactive;
        Dispatcher.InvokeAsync(ApplyWindowExStyles);
    }

    public void SetHiddenByUser(bool hide)
    {
        _isHiddenByUser = hide;
        if (hide) Hide();
        else Show();
        PinStateChanged?.Invoke();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WndProcHook);
        }
        ApplyWindowExStyles();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        _dpiScale = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;

        RefreshDisplaySurface();
        ApplyCollapsedUi();

        int padPx = (int)Math.Round(_shadowPadDip * _dpiScale);
        MovePhysical(_windowPosPx.X - padPx, _windowPosPx.Y - padPx);

        if (_isHiddenByUser) Hide();
        if (!_suppressInitialSilentOcr) StartSilentOcr();
    }

    private nint WndProcHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == (int)WindowApi.WM_DPICHANGED)
        {
            uint dpiX = (uint)(wParam.ToInt64() & 0xFFFF);
            if (dpiX > 0)
            {
                _dpiScale = dpiX / 96.0;
                RefreshDisplaySurface();
            }
        }
        return 0;
    }

    private void HookInputEvents()
    {
        MouseMove += (_, e) =>
        {
            if (_draggingWindow)
            {
                if (GlobalPointer.TryGetPosition(out var now))
                {
                    int dx = now.X - _dragStartCursorPx.X;
                    int dy = now.Y - _dragStartCursorPx.Y;
                    _windowPosPx = new PxPoint(_dragStartWindowPx.X + dx, _dragStartWindowPx.Y + dy);
                    int padPx = (int)Math.Round(_shadowPadDip * _dpiScale);
                    MovePhysical(_windowPosPx.X - padPx, _windowPosPx.Y - padPx);
                }
                return;
            }

            if (_isCollapsed)
            {
                Cursor = Cursors.SizeAll;
                return;
            }

            var pt = e.GetPosition(ImageHost);
            if (!IsShowingOriginal && !_isTranslating && _textLayer.TryUpdateDragSelection(pt))
            {
                Cursor = Cursors.IBeam;
                return;
            }

            Cursor = !IsShowingOriginal && !_isTranslating && _textLayer.IsPointOverText(pt)
                ? Cursors.IBeam
                : Cursors.Arrow;
        };

        MouseLeftButtonDown += (_, e) =>
        {
            if (RenameBox.Visibility == Visibility.Visible) return;

            var pt = e.GetPosition(ImageHost);

            if (e.ClickCount >= 2 && (_isCollapsed || Keyboard.IsKeyDown(Key.Space) || IsShowingOriginal || _isTranslating || !_textLayer.IsPointOverText(pt)))
            {
                ToggleCollapse();
                e.Handled = true;
                return;
            }

            if (!_isCollapsed && !IsShowingOriginal && !_isTranslating && _textLayer.TryBeginDragSelection(pt, e.ClickCount))
            {
                e.Handled = true;
                return;
            }

            if (GlobalPointer.TryGetPosition(out var cur))
            {
                _draggingWindow = true;
                _dragStartCursorPx = cur;
                _dragStartWindowPx = _windowPosPx;
                CaptureMouse();
                e.Handled = true;
            }
        };

        MouseLeftButtonUp += (_, e) =>
        {
            if (_draggingWindow)
            {
                _draggingWindow = false;
                if (IsMouseCaptured) ReleaseMouseCapture();
                PinStateChanged?.Invoke();
                e.Handled = true;
                return;
            }

            if (_textLayer.EndDragSelection())
            {
                e.Handled = true;
            }
        };

        MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                ClosePin();
                e.Handled = true;
            }
        };

        MouseRightButtonUp += (_, e) =>
        {
            ShowDarkContextMenu();
            e.Handled = true;
        };

        MouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                double step = e.Delta > 0 ? 0.1 : -0.1;
                SetOpacity(_targetOpacity + step);
                e.Handled = true;
                return;
            }

            if (_isCollapsed) return;
            AdjustZoomAroundCursor(e.Delta > 0 ? 1.1 : 1.0 / 1.1, e.GetPosition(ImageHost));
            e.Handled = true;
        };

        PreviewKeyDown += OnPinKeyDown;
        PreviewKeyUp += OnPinKeyUp;
        LostKeyboardFocus += (_, _) => ClearAltOriginalPeek();
        Deactivated += (_, _) => ClearAltOriginalPeek();

        RenameBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitRename();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                RenameBox.Visibility = Visibility.Collapsed;
                Focus();
                e.Handled = true;
            }
        };
        RenameBox.LostKeyboardFocus += (_, _) =>
        {
            if (RenameBox.Visibility == Visibility.Visible) CommitRename();
        };
    }

    private void OnPinKeyDown(object sender, KeyEventArgs e)
    {
        if (RenameBox.Visibility == Visibility.Visible) return;

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        Key actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_hasOverlayTranslation && !_isTranslating && !ctrl && actualKey is Key.LeftAlt or Key.RightAlt)
        {
            if (!_showingOriginalAlt)
            {
                _showingOriginalAlt = true;
                RefreshDisplaySurface();
                ShowTranslateBadge(
                    IsShowingOriginal ? "正在查看原图（松开恢复译图）" : "正在预览译图（松开恢复原图）",
                    showStop: false,
                    autoHideMs: 0);
            }
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                ClosePin();
                e.Handled = true;
                break;
            case Key.H when !ctrl:
                SetHiddenByUser(true);
                e.Handled = true;
                break;
            case Key.F2:
                BeginRename();
                e.Handled = true;
                break;
            case Key.D0 when ctrl:
            case Key.NumPad0 when ctrl:
                ResetZoom();
                e.Handled = true;
                break;
            case Key.A when ctrl:
                _textLayer.SelectAll();
                e.Handled = true;
                break;
            case Key.C when ctrl:
                CopySmart();
                e.Handled = true;
                break;
            case Key.S when ctrl:
                SaveToFile();
                e.Handled = true;
                break;
            case Key.O when ctrl && Features.Ocr:
                RequestOcr();
                e.Handled = true;
                break;
            case Key.T when ctrl && Features.Translate:
                RequestTranslate();
                e.Handled = true;
                break;
            case Key.R when !ctrl:
                Rotate90();
                e.Handled = true;
                break;
            case Key.Space:
                Cursor = Cursors.SizeAll;
                break;
        }
    }

    private void OnPinKeyUp(object sender, KeyEventArgs e)
    {
        Key actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_showingOriginalAlt && actualKey is Key.LeftAlt or Key.RightAlt)
        {
            ClearAltOriginalPeek(showRestoredHint: true);
            e.Handled = true;
        }
    }

    private void ClearAltOriginalPeek(bool showRestoredHint = false)
    {
        if (!_showingOriginalAlt) return;
        _showingOriginalAlt = false;
        RefreshDisplaySurface();

        if (_showingOriginalLocked)
        {
            ShowTranslateBadge("原图预览（Alt 或右键切回译图）", showStop: false, autoHideMs: 0);
        }
        else if (showRestoredHint)
        {
            ShowTranslateBadge("已恢复译图", showStop: false, autoHideMs: 700);
        }
        else
        {
            HideTranslateBadge();
        }
    }

    private void ToggleOriginalLocked()
    {
        if (!_hasOverlayTranslation || _originalOverlaySource is null) return;
        _showingOriginalLocked = !_showingOriginalLocked;
        RefreshDisplaySurface();
        if (_showingOriginalLocked)
        {
            ShowTranslateBadge("原图预览（Alt 或右键切回译图）", showStop: false, autoHideMs: 0);
        }
        else
        {
            ShowTranslateBadge("已切换回译图", showStop: false, autoHideMs: 900);
        }
    }

    private bool IsShowingOriginal =>
        _hasOverlayTranslation && (_showingOriginalAlt ^ _showingOriginalLocked) && _originalOverlaySource is not null;

    private (int Width, int Height) OrientedPixelSize =>
        _rotationQuarterTurns % 2 == 1
            ? (_sourceFrame.Height, _sourceFrame.Width)
            : (_sourceFrame.Width, _sourceFrame.Height);

    private BitmapSource BuildTransformedBitmap(ScreenFrame frame)
    {
        var bmp = CaptureOutput.ToBitmapSource(frame);
        if (_rotationQuarterTurns == 0 && !_flipH && !_flipV) return bmp;

        var group = new TransformGroup();
        if (_rotationQuarterTurns != 0)
        {
            group.Children.Add(new RotateTransform(_rotationQuarterTurns * 90.0));
        }
        if (_flipH || _flipV)
        {
            group.Children.Add(new ScaleTransform(_flipH ? -1 : 1, _flipV ? -1 : 1));
        }
        var tb = new TransformedBitmap(bmp, group);
        tb.Freeze();
        return tb;
    }

    private ScreenFrame BuildTransformedFrame(ScreenFrame frame)
    {
        if (_rotationQuarterTurns == 0 && !_flipH && !_flipV) return CloneFrameOriginZero(frame);
        return CaptureOutput.FromBitmapSource(BuildTransformedBitmap(frame));
    }

    private static ScreenFrame CloneFrameOriginZero(ScreenFrame frame) =>
        new(new PxRect(0, 0, frame.Width, frame.Height), frame.Stride, frame.Pixels.ToArray());

    private void RefreshDisplaySurface()
    {
        bool showOriginal = IsShowingOriginal;
        var activeSource = showOriginal ? _originalOverlaySource! : _sourceFrame;
        _displayBitmap = BuildTransformedBitmap(activeSource);

        PinImage.Source = _displayBitmap;
        RenderOptions.SetBitmapScalingMode(
            PinImage,
            _zoom >= 1.0 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);

        var (ow, oh) = OrientedPixelSize;
        double scale = _dpiScale <= 0 ? 1.0 : _dpiScale;
        double wDip = Math.Max(16, (ow * _zoom) / scale);
        double hDip = Math.Max(16, (oh * _zoom) / scale);

        ImageHost.Width = wDip;
        ImageHost.Height = hDip;
        _textLayer.Width = wDip;
        _textLayer.Height = hDip;
        TextLayerHost.Visibility = (showOriginal || _isTranslating) ? Visibility.Collapsed : Visibility.Visible;
        _textLayer.UpdateTransform(_sourceFrame.Width, _sourceFrame.Height, _rotationQuarterTurns, _flipH, _flipV);
        TranslateBadge.MaxWidth = Math.Max(48, wDip - 12);
    }

    private void AdjustZoomAroundCursor(double factor, Point cursorInImageDip)
    {
        double oldZoom = _zoom;
        double nextZoom = Math.Clamp(oldZoom * factor, 0.1, 10.0);
        if (Math.Abs(nextZoom - 1.0) < 0.04) nextZoom = 1.0;
        if (Math.Abs(nextZoom - oldZoom) < 1e-4) return;

        double oldRatio = nextZoom / oldZoom;
        double dxDip = cursorInImageDip.X * (1.0 - oldRatio);
        double dyDip = cursorInImageDip.Y * (1.0 - oldRatio);

        _zoom = nextZoom;
        RefreshDisplaySurface();

        _windowPosPx = new PxPoint(
            _windowPosPx.X + (int)Math.Round(dxDip * _dpiScale),
            _windowPosPx.Y + (int)Math.Round(dyDip * _dpiScale));
        int padPx = (int)Math.Round(_shadowPadDip * _dpiScale);
        MovePhysical(_windowPosPx.X - padPx, _windowPosPx.Y - padPx);

        ShowStatusBadge($"{(int)Math.Round(_zoom * 100)}%");
        PinStateChanged?.Invoke();
    }

    private void ResetZoom()
    {
        _zoom = 1.0;
        RefreshDisplaySurface();
        ShowStatusBadge("100%");
        PinStateChanged?.Invoke();
    }

    private void SetOpacity(double val)
    {
        _targetOpacity = Math.Clamp(val, 0.1, 1.0);
        Opacity = _targetOpacity;
        ShowStatusBadge($"透明度 {(int)Math.Round(_targetOpacity * 100)}%");
        PinStateChanged?.Invoke();
    }

    private void Rotate90()
    {
        _rotationQuarterTurns = (_rotationQuarterTurns + 1) % 4;
        RefreshDisplaySurface();
        ShowStatusBadge($"{_rotationQuarterTurns * 90}°");
        PinStateChanged?.Invoke();
    }

    private void FlipHorizontal()
    {
        _flipH = !_flipH;
        RefreshDisplaySurface();
        PinStateChanged?.Invoke();
    }

    private void FlipVertical()
    {
        _flipV = !_flipV;
        RefreshDisplaySurface();
        PinStateChanged?.Invoke();
    }

    private void ToggleCollapse()
    {
        _isCollapsed = !_isCollapsed;
        ApplyCollapsedUi();
        PinStateChanged?.Invoke();
    }

    private void ApplyCollapsedUi()
    {
        CardBorder.Visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        CollapsedBar.Visibility = _isCollapsed ? Visibility.Visible : Visibility.Collapsed;
        UpdateTitleUi();
    }

    private void UpdateTitleUi()
    {
        string defaultTitle = Kind == PinKind.Text && !string.IsNullOrWhiteSpace(TextContent)
            ? TextContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "文本贴图"
            : $"{_sourceFrame.Width} × {_sourceFrame.Height}";
        string display = string.IsNullOrWhiteSpace(Alias) ? defaultTitle : Alias!;
        Title = $"Ppinpix 贴图 - {display}";
        CollapsedTitleText.Text = display;
    }

    private void BeginRename()
    {
        if (_isCollapsed)
        {
            _isCollapsed = false;
            ApplyCollapsedUi();
        }
        RenameBox.Text = Alias ?? string.Empty;
        RenameBox.Visibility = Visibility.Visible;
        RenameBox.Focus();
        RenameBox.SelectAll();
    }

    private void CommitRename()
    {
        string text = RenameBox.Text.Trim();
        Alias = string.IsNullOrEmpty(text) ? null : text;
        RenameBox.Visibility = Visibility.Collapsed;
        UpdateTitleUi();
        Focus();
        PinStateChanged?.Invoke();
    }

    private void ShowStatusBadge(string text)
    {
        StatusBadgeText.Text = text;
        StatusBadge.BeginAnimation(OpacityProperty, null);
        StatusBadge.Opacity = 1.0;
        _badgeTimer.Stop();
        _badgeTimer.Start();
    }

    private void ShowTranslateBadge(string text, bool showStop, int autoHideMs)
    {
        TranslateBadgeText.Text = text;
        TranslateBadgeText.ToolTip = text;
        TranslateStopBtn.Visibility = showStop ? Visibility.Visible : Visibility.Collapsed;
        TranslateBadge.IsHitTestVisible = showStop;
        TranslateBadge.Visibility = Visibility.Visible;
        TranslateBadge.BeginAnimation(OpacityProperty, null);
        TranslateBadge.Opacity = 1.0;
        _translateBadgeTimer.Stop();
        if (autoHideMs > 0)
        {
            _translateBadgeTimer.Interval = TimeSpan.FromMilliseconds(autoHideMs);
            _translateBadgeTimer.Start();
        }
    }

    private void HideTranslateBadge()
    {
        _translateBadgeTimer.Stop();
        TranslateBadge.BeginAnimation(OpacityProperty, null);
        TranslateBadge.Opacity = 0;
        TranslateBadge.Visibility = Visibility.Collapsed;
        TranslateBadge.IsHitTestVisible = false;
        TranslateStopBtn.Visibility = Visibility.Collapsed;
    }

    private void OnTranslateStopClick(object sender, RoutedEventArgs e)
    {
        CancelTranslation();
        e.Handled = true;
    }

    private void CancelTranslation() => _onCancelTranslate?.Invoke(this);

    private void ToggleTopmost()
    {
        _isTopmost = !_isTopmost;
        Topmost = _isTopmost;
        PinStateChanged?.Invoke();
    }

    private void ToggleClickThrough()
    {
        _isClickThrough = !_isClickThrough;
        ApplyWindowExStyles();
        PinStateChanged?.Invoke();
    }

    private void ApplyWindowExStyles()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;

        long exStyle = WindowApi.GetWindowLongPtr(hwnd, WindowApi.GWL_EXSTYLE).ToInt64();
        exStyle |= WindowApi.WS_EX_TOOLWINDOW;

        bool enableTransparent = _isClickThrough && !_tempInteractive;
        if (enableTransparent) exStyle |= WindowApi.WS_EX_TRANSPARENT;
        else exStyle &= ~WindowApi.WS_EX_TRANSPARENT;

        WindowApi.SetWindowLongPtr(hwnd, WindowApi.GWL_EXSTYLE, unchecked((nint)exStyle));

        ClickThroughBadge.Visibility = _isClickThrough ? Visibility.Visible : Visibility.Collapsed;
        if (_isClickThrough)
        {
            ClickThroughBadgeText.Text = _tempInteractive ? "已临时解锁可拖拽" : "鼠标穿透 · Ctrl+Alt 可移动";
            CardBorder.BorderBrush = new SolidColorBrush(_tempInteractive ? Tokens.Accent : Color.FromArgb(140, Tokens.Accent.R, Tokens.Accent.G, Tokens.Accent.B));
        }
        else
        {
            CardBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255));
        }
    }

    private ScreenFrame BuildEffectiveFrame() => BuildTransformedFrame(_sourceFrame);

    private void CopySmart()
    {
        if (_textLayer.HasSelection)
        {
            string selText = _textLayer.GetSelectedText();
            if (!string.IsNullOrEmpty(selText))
            {
                Clipboard.SetText(selText);
                ToastWindow.Show($"已复制 {selText.Length} 个字符", ToastKind.Success, _displays);
                return;
            }
        }

        if (Kind == PinKind.Text && !string.IsNullOrEmpty(TextContent) && _rotationQuarterTurns == 0 && !_flipH && !_flipV)
        {
            Clipboard.SetText(TextContent);
            ToastWindow.Show("已复制文本内容到剪贴板", ToastKind.Success, _displays);
            return;
        }

        var frame = BuildEffectiveFrame();
        if (CaptureOutput.CopyToClipboard(frame))
        {
            ToastWindow.Show($"已复制贴图 {frame.Width} × {frame.Height}", ToastKind.Success, _displays);
        }
    }

    private void CopyOriginalFrame()
    {
        if (_originalOverlaySource is null) return;
        var frame = BuildTransformedFrame(_originalOverlaySource);
        if (CaptureOutput.CopyToClipboard(frame))
        {
            ToastWindow.Show($"已复制原图 {frame.Width} × {frame.Height}", ToastKind.Success, _displays);
        }
    }

    private void SaveToFile()
    {
        try
        {
            string path = CaptureOutput.Save(BuildEffectiveFrame(), _settings.Save);
            ToastWindow.Show($"已保存贴图至 {path}", ToastKind.Success, _displays);
        }
        catch (Exception ex)
        {
            Log.Error("保存贴图失败。", ex);
            ToastWindow.Show($"保存失败：{ex.Message}", ToastKind.Error, _displays);
        }
    }

    private void OpenAnnotationEditor()
    {
        if (_isCollapsed) ToggleCollapse();
        var effective = BuildEffectiveFrame();
        var editorWin = new PinAnnotationWindow(effective, _settings.Annotation, _displays);
        bool? ok = editorWin.ShowDialog();
        if (ok == true && editorWin.ResultFrame is { } edited)
        {
            _sourceFrame = CloneFrameOriginZero(edited);
            _originalOverlaySource = null;
            _originalOcr = null;
            _hasOverlayTranslation = false;
            _showingOriginalAlt = false;
            _showingOriginalLocked = false;
            HideTranslateBadge();
            _rotationQuarterTurns = 0;
            _flipH = false;
            _flipV = false;
            RefreshDisplaySurface();
            StartSilentOcr();
            PinStateChanged?.Invoke();
        }
    }

    private void RequestOcr() => _onOpenOcr?.Invoke(this, BuildEffectiveFrame(), _lastOcr);

    private void RequestTranslate() => _onOpenTranslate?.Invoke(this, GetCompareSourceFrame());

    private void ClosePin() => Close();

    private void StartSilentOcr()
    {
        if (!Features.Ocr || _ocrService is null || !_settings.Pin.EnableTextSelection) return;

        _ocrCts?.Cancel();
        _ocrCts?.Dispose();
        _ocrCts = new CancellationTokenSource();
        var token = _ocrCts.Token;

        var frameSnapshot = new ScreenFrame(
            new PxRect(0, 0, _sourceFrame.Width, _sourceFrame.Height),
            _sourceFrame.Stride,
            _sourceFrame.Pixels.ToArray());

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _ocrService.RecognizeAsync(frameSnapshot, token).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                await Dispatcher.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested) return;
                    _lastOcr = result;
                    _textLayer.SetOcrResult(result, frameSnapshot.Width, frameSnapshot.Height);
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log.Warn($"贴图静默 OCR 失败：{ex.Message}");
            }
        }, token);
    }

    private void ShowDarkContextMenu()
    {
        var menu = new ContextMenu
        {
            Style = ThemeLoader.Find<Style>("Pp.ContextMenu"),
        };

        // 如果检测到二维码，顶部优先展示一键复制/打开链接
        if (_lastOcr?.Barcodes is { Count: > 0 } barcodes)
        {
            foreach (var bc in barcodes.Take(2))
            {
                string preview = bc.Content.Length > 28 ? bc.Content[..28] + "…" : bc.Content;
                menu.Items.Add(MakeItem($"复制{bc.Kind}: {preview}", Icons.Zoom, null, () =>
                {
                    Clipboard.SetText(bc.Content);
                    ToastWindow.Show("已复制码内容到剪贴板", ToastKind.Success, _displays);
                }));
                if (Uri.TryCreate(bc.Content, UriKind.Absolute, out var uri) &&
                    uri.Scheme is "http" or "https")
                {
                    menu.Items.Add(MakeItem($"在浏览器中打开链接", Icons.Globe, null, () =>
                    {
                        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                    }));
                }
            }
            menu.Items.Add(MakeSep());
        }

        if (_isTranslating)
        {
            menu.Items.Add(MakeItem("停止翻译", Icons.Close, null, CancelTranslation));
            menu.Items.Add(MakeSep());
        }

        menu.Items.Add(MakeItem(_textLayer.HasSelection ? "复制选中文字" : "复制", Icons.Copy, "Ctrl+C", CopySmart));
        if (_hasOverlayTranslation && _originalOverlaySource is not null)
        {
            menu.Items.Add(MakeItem("复制原图", Icons.Copy, null, CopyOriginalFrame));
        }
        menu.Items.Add(MakeItem("另存为图片", Icons.Save, "Ctrl+S", SaveToFile));

        if (Features.Annotate)
        {
            menu.Items.Add(MakeItem("编辑标注…", Icons.Edit, null, OpenAnnotationEditor));
        }
        if (Features.Ocr)
        {
            menu.Items.Add(MakeItem("提取文字 (OCR)…", Icons.Document, "Ctrl+O", RequestOcr));
        }
        if (Features.Translate)
        {
            menu.Items.Add(MakeItem(_hasOverlayTranslation ? "重新翻译此图" : "翻译此图", Icons.Globe, "Ctrl+T", RequestTranslate));
            if (_hasOverlayTranslation && _originalOverlaySource is not null)
            {
                menu.Items.Add(MakeItem("对比：显示原图 (按住 Alt)", Icons.View, "Alt", ToggleOriginalLocked, isChecked: _showingOriginalLocked));
            }
            if (_getTranslateLanguages is not null && _onRetranslateWithLang is not null)
            {
                var langMenu = MakeItem("翻译目标语言", Icons.Globe, null, null);
                string currentTarget = _getActiveTargetLanguage?.Invoke() ?? "auto";
                foreach (var (code, displayName) in _getTranslateLanguages())
                {
                    string langCode = code;
                    langMenu.Items.Add(MakeItem(
                        displayName,
                        null,
                        null,
                        () => _onRetranslateWithLang(this, GetCompareSourceFrame(), langCode),
                        isChecked: string.Equals(currentTarget, langCode, StringComparison.OrdinalIgnoreCase)));
                }
                menu.Items.Add(langMenu);
            }
            if (_onOpenCompareTranslate is not null)
            {
                menu.Items.Add(MakeItem(
                    "在对照窗口打开…",
                    Icons.Document,
                    null,
                    () => _onOpenCompareTranslate(this, GetCompareSourceFrame(), _hasOverlayTranslation ? _originalOcr : _lastOcr)));
            }
        }

        menu.Items.Add(MakeSep());
        menu.Items.Add(MakeItem("顺时针旋转 90°", Icons.Rotate, "R", Rotate90));
        menu.Items.Add(MakeItem("水平翻转", Icons.FlipH, null, FlipHorizontal));
        menu.Items.Add(MakeItem("垂直翻转", Icons.FlipV, null, FlipVertical));
        menu.Items.Add(MakeItem("重置缩放 (100%)", Icons.Refresh, "Ctrl+0", ResetZoom));
        menu.Items.Add(MakeItem(_isCollapsed ? "展开贴图" : "折叠贴图", Icons.Crop, "双击", ToggleCollapse));
        menu.Items.Add(MakeItem("重命名…", Icons.Edit, "F2", BeginRename));

        var opacityMenu = MakeItem("不透明度", Icons.View, null, null);
        foreach (double pct in new[] { 1.0, 0.8, 0.6, 0.4, 0.2 })
        {
            double val = pct;
            var sub = MakeItem($"{(int)(val * 100)}%", null, null, () => SetOpacity(val), isChecked: Math.Abs(_targetOpacity - val) < 0.05);
            opacityMenu.Items.Add(sub);
        }
        menu.Items.Add(opacityMenu);

        menu.Items.Add(MakeSep());
        menu.Items.Add(MakeItem("置顶显示", Icons.Pin, null, ToggleTopmost, isChecked: _isTopmost));
        menu.Items.Add(MakeItem("鼠标穿透 (Ctrl+Alt 可控)", Icons.Shield, null, ToggleClickThrough, isChecked: _isClickThrough));
        menu.Items.Add(MakeItem("隐藏", Icons.Hide, "H", () => SetHiddenByUser(true)));
        menu.Items.Add(MakeItem("关闭", Icons.Close, "Esc", ClosePin));

        menu.IsOpen = true;
    }

    private static MenuItem MakeItem(string header, string? iconGlyph, string? shortcut, Action? onClick, bool isChecked = false)
    {
        var item = new MenuItem
        {
            Header = header,
            Style = ThemeLoader.Find<Style>("Pp.MenuItem"),
            IsChecked = isChecked,
            IsCheckable = false,
            InputGestureText = shortcut ?? string.Empty,
        };
        if (!string.IsNullOrEmpty(iconGlyph))
        {
            item.Icon = new TextBlock
            {
                Text = iconGlyph,
                FontFamily = ThemeLoader.Find<FontFamily>("Pp.IconFont"),
                FontSize = 13,
                Foreground = ThemeLoader.Brush("Pp.TextSecondary"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        if (onClick is not null)
        {
            item.Click += (_, _) => onClick();
        }
        return item;
    }

    private static Separator MakeSep() => new() { Style = ThemeLoader.Find<Style>("Pp.MenuSeparator") };

    private void MovePhysical(int x, int y)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;
        WindowApi.SetWindowPos(
            hwnd,
            0,
            x,
            y,
            0,
            0,
            WindowApi.SWP_NOSIZE | WindowApi.SWP_NOZORDER | WindowApi.SWP_NOACTIVATE);
    }

    private void OnClosedCleanup(object? sender, EventArgs e)
    {
        _badgeTimer.Stop();
        _translateBadgeTimer.Stop();
        if (_isTranslating)
        {
            _isTranslating = false;
            _onCancelTranslate?.Invoke(this);
        }
        _ocrCts?.Cancel();
        _ocrCts?.Dispose();
        _ocrCts = null;
    }
}

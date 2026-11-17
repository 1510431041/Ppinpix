using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ppinpix.Capture.Output;
using Ppinpix.Capture.Overlay;
using Ppinpix.Core;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Display;
using Ppinpix.Core.Settings;
using Ppinpix.LlmGateway.Models;
using Ppinpix.Ocr;
using Ppinpix.Ocr.Models;
using Ppinpix.Theme;
using Ppinpix.Translate;

namespace Ppinpix.App.Translate;

public partial class TranslateWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly OcrService _ocrService;
    private readonly TranslateService _translateService;
    private readonly DisplayService _displays;
    private readonly Action<ScreenFrame> _onPinFrame;
    private readonly Action _onOpenSettings;

    private readonly ScreenFrame? _sourceFrame;
    private readonly bool _forceCompareView;
    private OcrResult? _lastOcr;
    private ScreenFrame? _renderedOverlayFrame;
    private BitmapSource? _originalBitmap;
    private BitmapSource? _overlayBitmap;

    private readonly DispatcherTimer _cursorTimer;
    private CancellationTokenSource? _cts;
    private string _cleanTranslation = string.Empty;
    private bool _streaming;
    private bool _cursorVisible;
    private bool _initializing = true;
    private bool _showingOriginalAlt;

    public TranslateWindow(
        ScreenFrame? sourceFrame,
        string? initialText,
        AppSettings settings,
        SettingsStore store,
        OcrService ocrService,
        TranslateService translateService,
        DisplayService displays,
        Action<ScreenFrame> onPinFrame,
        Action onOpenSettings,
        UserInitiatedToken userToken,
        bool forceCompareView = false,
        OcrResult? precomputedOcr = null)
    {
        InitializeComponent();
        ThemeLoader.ApplySettingsTheme(Resources, isDark: true);

        _sourceFrame = sourceFrame;
        _forceCompareView = forceCompareView;
        _lastOcr = precomputedOcr;
        _settings = settings;
        _store = store;
        _ocrService = ocrService;
        _translateService = translateService;
        _displays = displays;
        _onPinFrame = onPinFrame;
        _onOpenSettings = onOpenSettings;

        if (sourceFrame is not null)
        {
            _originalBitmap = CaptureOutput.ToBitmapSource(sourceFrame);
        }

        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _cursorTimer.Tick += (_, _) =>
        {
            if (!_streaming) return;
            _cursorVisible = !_cursorVisible;
            UpdateTargetDisplay();
        };

        BindCombos();
        _initializing = false;

        if (!string.IsNullOrWhiteSpace(initialText))
        {
            SourceTextBox.Text = initialText;
        }
        else if (precomputedOcr is { IsEmpty: false })
        {
            SourceTextBox.Text = precomputedOcr.FullText;
        }

        PreviewKeyDown += OnWindowKeyDown;
        PreviewKeyUp += OnWindowKeyUp;
        Closed += (_, _) =>
        {
            _cursorTimer.Stop();
            CancelCurrent();
        };

        Loaded += async (_, _) => await StartInitialFlowAsync(userToken);
    }

    private void BindCombos()
    {
        ProfileCombo.Items.Clear();
        foreach (var p in _settings.Translate.Profiles)
        {
            ProfileCombo.Items.Add(new ComboBoxItem
            {
                Content = string.IsNullOrWhiteSpace(p.Model) ? p.Name : $"{p.Name} · {p.Model}",
                Tag = p.Id,
            });
        }
        SelectComboByTag(ProfileCombo, _settings.Translate.ActiveProfileId);

        SourceLangCombo.Items.Clear();
        TargetLangCombo.Items.Clear();
        foreach (var lang in LanguageDetector.SupportedLanguages)
        {
            SourceLangCombo.Items.Add(new ComboBoxItem { Content = lang.DisplayName, Tag = lang.Code });
            TargetLangCombo.Items.Add(new ComboBoxItem { Content = lang.DisplayName, Tag = lang.Code });
        }
        SelectComboByTag(SourceLangCombo, _settings.Translate.SourceLanguage);
        SelectComboByTag(TargetLangCombo, _settings.Translate.TargetLanguage);

        RefreshModeButtons();
    }

    private LlmProfile ActiveProfile =>
        _settings.Translate.Profiles.FirstOrDefault(p => p.Id == (GetSelectedTag(ProfileCombo) ?? _settings.Translate.ActiveProfileId))
        ?? _settings.Translate.Profiles[0];

    private void RefreshModeButtons()
    {
        var profile = ActiveProfile;
        bool useImage = profile.InputMode == TranslationInputMode.ImageDirectly && profile.SupportsVision && _sourceFrame is not null;
        InputModeBtn.Content = useImage ? "直接发图" : "OCR 文字";
        InputModeBtn.IsEnabled = profile.SupportsVision && _sourceFrame is not null;
        OverlayModeBtn.IsEnabled = _sourceFrame is not null;
    }

    private async Task StartInitialFlowAsync(UserInitiatedToken userToken)
    {
        var profile = ActiveProfile;

        if (string.IsNullOrWhiteSpace(SourceTextBox.Text) && _sourceFrame is not null)
        {
            bool imageMode = profile.InputMode == TranslationInputMode.ImageDirectly && profile.SupportsVision;
            if (imageMode)
            {
                await RunImageTranslateAsync(userToken);
                return;
            }

            StatusText.Text = "正在离线提取文字 (OCR)…";
            try
            {
                _lastOcr = await _ocrService.RecognizeAsync(_sourceFrame);
                SourceTextBox.Text = _lastOcr.FullText;
                if (string.IsNullOrWhiteSpace(SourceTextBox.Text))
                {
                    StatusText.Text = "未从截图识别出文字，可手动输入或切换多模态「直接发图」模式。";
                    return;
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"OCR 识别失败：{ex.Message}";
                return;
            }
        }

        if (!_forceCompareView &&
            _settings.Translate.Presentation == TranslationPresentation.Overlay &&
            _sourceFrame is not null &&
            _lastOcr is { Paragraphs.Count: > 0 })
        {
            await RunOverlayTranslateAsync(userToken);
        }
        else
        {
            await RunTextTranslateAsync(userToken);
        }
    }

    private bool EnsurePrivacyConfirmed(LlmProfile profile)
    {
        if (_settings.Translate.DiscloseRemoteConfirmed) return true;

        var dlg = new PrivacyDisclosureDialog(profile) { Owner = this };
        if (dlg.ShowDialog() != true)
        {
            StatusText.Text = "已取消发送（未确认外发隐私提示，零数据传出）。";
            return false;
        }

        if (dlg.RememberChoice)
        {
            _settings.Translate.DiscloseRemoteConfirmed = true;
            _store.Save(_settings);
        }
        return true;
    }

    private async Task RunTextTranslateAsync(UserInitiatedToken userToken)
    {
        string text = SourceTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            StatusText.Text = "请先输入或识别需要翻译的文本。";
            return;
        }

        var profile = ActiveProfile;
        if (!EnsurePrivacyConfirmed(profile)) return;

        CancelCurrent();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var sw = Stopwatch.StartNew();

        string srcLang = GetSelectedTag(SourceLangCombo) ?? "auto";
        string tgtLang = GetSelectedTag(TargetLangCombo) ?? "auto";

        BeginStreamingUi($"正在调用 {profile.Name} ({profile.Model})…");

        try
        {
            await foreach (var prog in _translateService.TranslateTextStreamAsync(
                userToken, text, profile, srcLang, tgtLang, ct))
            {
                _cleanTranslation = prog.AccumulatedText;
                UpdateTargetDisplay();
                SourceLabel.Text = $"原文（{LanguageDetector.GetDisplayName(prog.SourceLangCode)}）";
                TargetLabel.Text = $"译文（{LanguageDetector.GetDisplayName(prog.TargetLangCode)}）";
            }

            sw.Stop();
            EndStreamingUi($"翻译完成 · 耗时 {sw.ElapsedMilliseconds} ms · {profile.Model}");
        }
        catch (OperationCanceledException)
        {
            EndStreamingUi("已停止生成");
        }
        catch (LlmException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async Task RunImageTranslateAsync(UserInitiatedToken userToken)
    {
        if (_sourceFrame is null) return;
        var profile = ActiveProfile;
        if (!EnsurePrivacyConfirmed(profile)) return;

        CancelCurrent();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var sw = Stopwatch.StartNew();

        string tgtLang = GetSelectedTag(TargetLangCombo) ?? "auto";
        SourceTextBox.Text = "[已切换到多模态直接发送图片模式]";
        BeginStreamingUi($"正在发送截图至 {profile.Model}…");

        try
        {
            await foreach (var prog in _translateService.TranslateImageDirectlyStreamAsync(
                userToken, _sourceFrame, profile, tgtLang, ct))
            {
                _cleanTranslation = prog.AccumulatedText;
                UpdateTargetDisplay();
            }
            sw.Stop();
            EndStreamingUi($"图片翻译完成 · 耗时 {sw.ElapsedMilliseconds} ms");
        }
        catch (OperationCanceledException)
        {
            EndStreamingUi("已停止生成");
        }
        catch (LlmException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async Task RunOverlayTranslateAsync(UserInitiatedToken userToken)
    {
        if (_sourceFrame is null) return;
        var profile = ActiveProfile;
        if (!EnsurePrivacyConfirmed(profile)) return;

        CompareView.Visibility = Visibility.Collapsed;
        OverlayPreviewView.Visibility = Visibility.Visible;
        OverlayPreviewImage.Source = _originalBitmap;

        if (_lastOcr is null)
        {
            StatusText.Text = "正在离线分析图片段落排版…";
            _lastOcr = await _ocrService.RecognizeAsync(_sourceFrame);
        }

        if (_lastOcr.Paragraphs.Count == 0)
        {
            StatusText.Text = "未能检测到可原位覆盖的段落文本。";
            return;
        }

        CancelCurrent();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var sw = Stopwatch.StartNew();

        string srcLang = GetSelectedTag(SourceLangCombo) ?? "auto";
        string tgtLang = GetSelectedTag(TargetLangCombo) ?? "auto";

        BeginStreamingUi($"正在流式生成原位覆盖译图（{_lastOcr.Paragraphs.Count} 个段落）…");

        try
        {
            await foreach (var prog in _translateService.TranslateOverlayStreamAsync(
                userToken, _sourceFrame, _lastOcr, profile, srcLang, tgtLang, ct))
            {
                _renderedOverlayFrame = prog.RenderedFrame;
                _overlayBitmap = CaptureOutput.ToBitmapSource(prog.RenderedFrame);
                if (!_showingOriginalAlt)
                {
                    OverlayPreviewImage.Source = _overlayBitmap;
                }
            }
            sw.Stop();
            EndStreamingUi($"原位覆盖完成 · 耗时 {sw.ElapsedMilliseconds} ms（按住 Alt 查看原图对比）");
        }
        catch (OperationCanceledException)
        {
            EndStreamingUi("已停止原位生成");
        }
        catch (LlmException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void BeginStreamingUi(string status)
    {
        _streaming = true;
        _cursorVisible = true;
        _cleanTranslation = string.Empty;
        RetryBtn.Visibility = Visibility.Collapsed;
        StopBtn.Visibility = Visibility.Visible;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, "Pp.Accent");
        StatusText.Text = status;
        _cursorTimer.Start();
        UpdateTargetDisplay();
    }

    private void EndStreamingUi(string status)
    {
        _streaming = false;
        _cursorVisible = false;
        _cursorTimer.Stop();
        StopBtn.Visibility = Visibility.Collapsed;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, "Pp.Settings.TextSecondary");
        StatusText.Text = status;
        UpdateTargetDisplay();
    }

    private void ShowError(string message)
    {
        _streaming = false;
        _cursorVisible = false;
        _cursorTimer.Stop();
        StopBtn.Visibility = Visibility.Collapsed;
        RetryBtn.Visibility = Visibility.Visible;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, "Pp.Danger");
        StatusText.Text = message;
        UpdateTargetDisplay();
    }

    private void UpdateTargetDisplay()
    {
        TargetTextBox.Text = _streaming && _cursorVisible
            ? _cleanTranslation + "▍"
            : _cleanTranslation;
    }

    private void CancelCurrent()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async void OnProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        if (GetSelectedTag(ProfileCombo) is string id)
        {
            _settings.Translate.ActiveProfileId = id;
            _store.Save(_settings);
            RefreshModeButtons();
            await RetranslateFromUserAsync();
        }
    }

    private async void OnTargetLangChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        _settings.Translate.TargetLanguage = GetSelectedTag(TargetLangCombo) ?? "auto";
        _store.Save(_settings);
        await RetranslateFromUserAsync();
    }

    private async void OnToggleInputModeClick(object sender, RoutedEventArgs e)
    {
        var profile = ActiveProfile;
        if (!profile.SupportsVision || _sourceFrame is null) return;
        profile.InputMode = profile.InputMode == TranslationInputMode.ImageDirectly
            ? TranslationInputMode.OcrThenTranslate
            : TranslationInputMode.ImageDirectly;
        _store.Save(_settings);
        RefreshModeButtons();
        await RetranslateFromUserAsync();
    }

    private async void OnOverlayTranslateClick(object sender, RoutedEventArgs e) =>
        await RunOverlayTranslateAsync(UserInitiatedToken.IssueFromUserAction());

    private async void OnRetranslateClick(object sender, RoutedEventArgs e) =>
        await RetranslateFromUserAsync();

    private async Task RetranslateFromUserAsync()
    {
        var token = UserInitiatedToken.IssueFromUserAction();
        var profile = ActiveProfile;
        if (OverlayPreviewView.Visibility == Visibility.Visible && _sourceFrame is not null)
        {
            await RunOverlayTranslateAsync(token);
        }
        else if (profile.InputMode == TranslationInputMode.ImageDirectly && profile.SupportsVision && _sourceFrame is not null)
        {
            await RunImageTranslateAsync(token);
        }
        else
        {
            await RunTextTranslateAsync(token);
        }
    }

    private void OnStopClick(object sender, RoutedEventArgs e) => CancelCurrent();

    private void OnSourcePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            e.Handled = true;
            _ = RetranslateFromUserAsync();
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (OverlayPreviewView.Visibility == Visibility.Visible &&
            (e.Key is Key.LeftAlt or Key.RightAlt || e.SystemKey is Key.LeftAlt or Key.RightAlt))
        {
            _showingOriginalAlt = true;
            if (_originalBitmap is not null) OverlayPreviewImage.Source = _originalBitmap;
            OverlayHintText.Text = "正在查看原图（松开 Alt 恢复译图）";
        }
    }

    private void OnWindowKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftAlt or Key.RightAlt || e.SystemKey is Key.LeftAlt or Key.RightAlt)
        {
            _showingOriginalAlt = false;
            if (_overlayBitmap is not null) OverlayPreviewImage.Source = _overlayBitmap;
            OverlayHintText.Text = "按住 Alt 查看原图对比";
        }
    }

    private void OnBackToCompareClick(object sender, RoutedEventArgs e)
    {
        OverlayPreviewView.Visibility = Visibility.Collapsed;
        CompareView.Visibility = Visibility.Visible;
    }

    private void OnCopySourceClick(object sender, RoutedEventArgs e)
    {
        string text = SourceTextBox.Text ?? string.Empty;
        if (!string.IsNullOrEmpty(text))
        {
            Clipboard.SetText(text);
            ToastWindow.Show("已复制原文到剪贴板", ToastKind.Success, _displays);
        }
    }

    private void OnCopyTargetClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_cleanTranslation))
        {
            Clipboard.SetText(_cleanTranslation);
            ToastWindow.Show("已复制译文到剪贴板", ToastKind.Success, _displays);
        }
    }

    private void OnCopyOverlayImageClick(object sender, RoutedEventArgs e)
    {
        var frame = _renderedOverlayFrame ?? _sourceFrame;
        if (frame is not null && CaptureOutput.CopyToClipboard(frame))
        {
            ToastWindow.Show("已复制原位覆盖译图到剪贴板", ToastKind.Success, _displays);
        }
    }

    private void OnPinOverlayImageClick(object sender, RoutedEventArgs e)
    {
        var frame = _renderedOverlayFrame ?? _sourceFrame;
        if (frame is not null)
        {
            _onPinFrame(frame);
        }
    }

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e) => _onOpenSettings();

    private static string? GetSelectedTag(ComboBox combo) =>
        (combo.SelectedItem as ComboBoxItem)?.Tag as string;

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }
}

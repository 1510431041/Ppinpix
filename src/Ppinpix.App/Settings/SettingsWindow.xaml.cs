using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Microsoft.Win32;
using Ppinpix.App.Startup;
using Ppinpix.Core;
using Ppinpix.Core.Hotkeys;
using Ppinpix.Core.Settings;
using Ppinpix.Ocr;
using Ppinpix.Theme;

namespace Ppinpix.App.Settings;

public partial class SettingsWindow : Window
{
    /// <summary>最后一次修改后多久自动保存（去抖，避免输入文字时频繁落盘 / 重注册热键）。</summary>
    private static readonly TimeSpan AutoSaveDelay = TimeSpan.FromMilliseconds(600);

    private readonly Func<AppSettings, IReadOnlyList<HotkeyRegistrationResult>> _onApply;
    private readonly OcrService _ocrService;
    private readonly DispatcherTimer _saveTimer;
    private AppSettings _working;
    private bool _loading = true;
    private bool _dirty;

    public SettingsWindow(
        AppSettings currentSettings,
        OcrService ocrService,
        Func<AppSettings, IReadOnlyList<HotkeyRegistrationResult>> onApply,
        string initialSection = "General")
    {
        InitializeComponent();

        _working = currentSettings.Clone();
        _ocrService = ocrService;
        _onApply = onApply;
        _saveTimer = new DispatcherTimer { Interval = AutoSaveDelay };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            if (_dirty) ApplyCurrent();
        };

        ApplyWindowTheme(_working.Ui.Theme);
        WireHotkeys();
        LoadFromSettings(_working);
        SelectSection(initialSection);

        _loading = false;
    }

    public void SelectSection(string sectionTag)
    {
        // 「关于与隐私」已并入「常规」页
        if (string.Equals(sectionTag, "About", StringComparison.OrdinalIgnoreCase))
        {
            sectionTag = "General";
        }

        foreach (var item in NavList.Items.OfType<ListBoxItem>())
        {
            if (string.Equals(item.Tag as string, sectionTag, StringComparison.OrdinalIgnoreCase))
            {
                item.IsSelected = true;
                break;
            }
        }
    }

    private void ApplyWindowTheme(AppTheme theme)
    {
        bool isDark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => ThemeLoader.IsSystemDarkTheme(),
        };
        ThemeLoader.ApplySettingsTheme(Resources, isDark);
        if (Application.Current is not null)
        {
            ThemeLoader.ApplySettingsTheme(Application.Current.Resources, isDark);
        }
    }

    private void WireHotkeys()
    {
        HkCapture.GestureChanged += g => { _working.Hotkeys.Capture = g; OnHotkeyEdited(); };
        HkPinClipboard.GestureChanged += g => { _working.Hotkeys.PinClipboard = g; OnHotkeyEdited(); };
        HkCaptureOcr.GestureChanged += g => { _working.Hotkeys.CaptureOcr = g; OnHotkeyEdited(); };
        HkCaptureTranslate.GestureChanged += g => { _working.Hotkeys.CaptureTranslate = g; OnHotkeyEdited(); };
        HkTogglePins.GestureChanged += g => { _working.Hotkeys.TogglePins = g; OnHotkeyEdited(); };
        HkSuspend.GestureChanged += g => { _working.Hotkeys.SuspendHotkeys = g; OnHotkeyEdited(); };
    }

    private void LoadFromSettings(AppSettings s)
    {
        _loading = true;

        // 常规
        AutoStartCheck.IsChecked = s.General.AutoStart || AutoStartService.IsEnabled();
        SelectComboByTag(ThemeCombo, s.Ui.Theme.ToString());
        AnimationsCheck.IsChecked = s.Ui.AnimationsEnabled;
        SoundCheck.IsChecked = s.Ui.PlaySoundOnCapture;

        // 快捷键
        HkCapture.GestureText = s.Hotkeys.Capture;
        HkPinClipboard.GestureText = s.Hotkeys.PinClipboard;
        HkCaptureOcr.GestureText = s.Hotkeys.CaptureOcr;
        HkCaptureTranslate.GestureText = s.Hotkeys.CaptureTranslate;
        HkTogglePins.GestureText = s.Hotkeys.TogglePins;
        HkSuspend.GestureText = s.Hotkeys.SuspendHotkeys;
        CheckLocalHotkeyDuplicates();

        // 截图与保存
        SelectComboByTag(DelayCombo, s.Capture.DelayMs.ToString());
        IncludeCursorCheck.IsChecked = s.Capture.IncludeCursor;
        SnapEnabledCheck.IsChecked = s.Capture.SnapEnabled;
        SnapControlsCheck.IsChecked = s.Capture.SnapToControls;
        SnapThresholdSlider.Value = s.Capture.SnapThresholdDip;
        SnapThresholdVal.Text = $"{s.Capture.SnapThresholdDip} px";
        ShowMagnifierCheck.IsChecked = s.Capture.ShowMagnifier;
        HideMagnifierDragCheck.IsChecked = s.Capture.HideMagnifierWhileDragging;
        MagZoomSlider.Value = s.Capture.MagnifierZoom;
        MagZoomVal.Text = $"{s.Capture.MagnifierZoom}x";
        ColorRgbCheck.IsChecked = s.Capture.DefaultColorFormatRgb;
        CloseOnCopyCheck.IsChecked = s.Capture.CloseOnCopy;
        ApplyToolbarCheckboxes(s.Capture.Toolbar);

        SelectComboByTag(SaveFormatCombo, s.Save.Format.ToString());
        QualitySlider.Value = s.Save.JpegQuality;
        QualityVal.Text = $"{s.Save.JpegQuality}%";
        CopyAfterSaveCheck.IsChecked = s.Save.CopyAfterSave;
        SaveDirBox.Text = s.Save.Directory;
        FileNameTplBox.Text = s.Save.FileNameTemplate;

        // 贴图
        PinOpacitySlider.Value = Math.Round(s.Pin.DefaultOpacity * 100);
        PinOpacityVal.Text = $"{(int)PinOpacitySlider.Value}%";
        PinMaxSlider.Value = s.Pin.MaxPinCount;
        PinMaxVal.Text = $"{s.Pin.MaxPinCount} 张";
        PinShadowCheck.IsChecked = s.Pin.ShowShadow;
        PinPixelCheck.IsChecked = s.Pin.KeepPhysicalPixels;
        PinRestoreCheck.IsChecked = s.Pin.RestoreOnStartup;
        PinTextSelectCheck.IsChecked = s.Pin.EnableTextSelection;

        // 离线 OCR
        SelectComboByTag(OcrEngineCombo, s.Ocr.Engine.ToString());
        OcrLangsBox.Text = string.Join(", ", s.Ocr.Languages);
        DetectBarcodesCheck.IsChecked = s.Ocr.DetectBarcodes;
        OcrIdleSlider.Value = s.Ocr.UnloadAfterIdleMinutes;
        OcrIdleVal.Text = s.Ocr.UnloadAfterIdleMinutes == 0 ? "不卸载" : $"{s.Ocr.UnloadAfterIdleMinutes} 分钟";
        RefreshOcrDiagnostics();

        // 翻译与大模型
        TranslatePanel.Bind(s, MarkDirty);

        // 关于
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        AppVersionText.Text = $"Ppinpix v{ver?.Major ?? 1}.{ver?.Minor ?? 0}.{ver?.Build ?? 0}（Windows 桌面端）";

        _loading = false;
    }

    private void RefreshOcrDiagnostics()
    {
        bool customModels = OcrService.HasCustomRapidOcrModels();
        bool loaded = _ocrService.IsModelLoaded;
        OcrModelStatusText.Text =
            $"自定义中文 ONNX 模型：{(customModels ? "已安装就绪" : "未发现（将使用内置拉丁模型或回退 Windows 系统 OCR）")}  ·  当前内存状态：{(loaded ? "已加载" : "未加载")}";

        var langs = OcrService.GetSystemOcrLanguages();
        SystemOcrLangsText.Text = langs.Count > 0
            ? "Windows 系统已安装 OCR 语言：" + string.Join("、", langs.Select(l => $"{l.DisplayName} ({l.Tag})"))
            : "Windows 系统尚未安装任何系统 OCR 语言包。";
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || PageGeneral is null) return;
        if (NavList.SelectedItem is not ListBoxItem item || item.Tag is not string tag) return;

        PageGeneral.Visibility = tag == "General" ? Visibility.Visible : Visibility.Collapsed;
        PageHotkeys.Visibility = tag == "Hotkeys" ? Visibility.Visible : Visibility.Collapsed;
        PageCapture.Visibility = tag == "Capture" ? Visibility.Visible : Visibility.Collapsed;
        PageToolbar.Visibility = tag == "Toolbar" ? Visibility.Visible : Visibility.Collapsed;
        PagePin.Visibility = tag == "Pin" ? Visibility.Visible : Visibility.Collapsed;
        PageOcr.Visibility = tag == "Ocr" ? Visibility.Visible : Visibility.Collapsed;
        PageTranslate.Visibility = tag == "Translate" ? Visibility.Visible : Visibility.Collapsed;

        if (tag == "Ocr")
        {
            RefreshOcrDiagnostics();
        }
    }

    /// <summary>标记有改动并（去抖）自动保存，不再需要手动点「应用」。</summary>
    private void MarkDirty()
    {
        if (_loading) return;
        _dirty = true;
        FooterStatusText.Text = "正在保存…";
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnGeneralChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _working.General.AutoStart = AutoStartCheck.IsChecked == true;
        _working.Ui.AnimationsEnabled = AnimationsCheck.IsChecked == true;
        _working.Ui.PlaySoundOnCapture = SoundCheck.IsChecked == true;
        MarkDirty();
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (ThemeCombo.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<AppTheme>(item.Tag as string, out var theme))
        {
            _working.Ui.Theme = theme;
            ApplyWindowTheme(theme);
            MarkDirty();
        }
    }

    private void OnHotkeyEdited()
    {
        CheckLocalHotkeyDuplicates();
        MarkDirty();
    }

    private void CheckLocalHotkeyDuplicates()
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dups = new List<string>();
        foreach ((string name, string val) in _working.Hotkeys.Enumerate())
        {
            if (string.IsNullOrWhiteSpace(val)) continue;
            if (!seen.TryAdd(val.Trim(), name))
            {
                dups.Add(val.Trim());
            }
        }

        if (dups.Count > 0)
        {
            HotkeyWarningText.Text = $"发现重复快捷键：{string.Join("、", dups.Distinct())}，保存后可能无法同时生效。";
            HotkeyWarningText.Visibility = Visibility.Visible;
        }
        else
        {
            HotkeyWarningText.Visibility = Visibility.Collapsed;
        }
    }

    private void OnResetHotkeysClick(object sender, RoutedEventArgs e)
    {
        var def = new HotkeySettings();
        _working.Hotkeys = def;
        HkCapture.GestureText = def.Capture;
        HkPinClipboard.GestureText = def.PinClipboard;
        HkCaptureOcr.GestureText = def.CaptureOcr;
        HkCaptureTranslate.GestureText = def.CaptureTranslate;
        HkTogglePins.GestureText = def.TogglePins;
        HkSuspend.GestureText = def.SuspendHotkeys;
        CheckLocalHotkeyDuplicates();
        MarkDirty();
    }

    private void OnCaptureChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (DelayCombo.SelectedItem is ComboBoxItem d && int.TryParse(d.Tag as string, out int delay))
        {
            _working.Capture.DelayMs = delay;
        }
        _working.Capture.IncludeCursor = IncludeCursorCheck.IsChecked == true;
        _working.Capture.SnapEnabled = SnapEnabledCheck.IsChecked == true;
        _working.Capture.SnapToControls = SnapControlsCheck.IsChecked == true;
        _working.Capture.ShowMagnifier = ShowMagnifierCheck.IsChecked == true;
        _working.Capture.HideMagnifierWhileDragging = HideMagnifierDragCheck.IsChecked == true;
        _working.Capture.DefaultColorFormatRgb = ColorRgbCheck.IsChecked == true;
        _working.Capture.CloseOnCopy = CloseOnCopyCheck.IsChecked == true;

        if (SaveFormatCombo.SelectedItem is ComboBoxItem f &&
            Enum.TryParse<ImageFormat>(f.Tag as string, out var fmt))
        {
            _working.Save.Format = fmt;
        }
        _working.Save.CopyAfterSave = CopyAfterSaveCheck.IsChecked == true;
        _working.Save.Directory = SaveDirBox.Text.Trim();
        _working.Save.FileNameTemplate = FileNameTplBox.Text.Trim();
        MarkDirty();
    }

    private (CheckBox Box, Func<ToolbarVisibilitySettings, bool> Get, Action<ToolbarVisibilitySettings, bool> Set)[] GetToolbarVisibilityBindings() =>
    [
        (TbShowSelectCheck, t => t.ShowSelect, (t, v) => t.ShowSelect = v),
        (TbShowRectangleCheck, t => t.ShowRectangle, (t, v) => t.ShowRectangle = v),
        (TbShowEllipseCheck, t => t.ShowEllipse, (t, v) => t.ShowEllipse = v),
        (TbShowArrowCheck, t => t.ShowArrow, (t, v) => t.ShowArrow = v),
        (TbShowLineCheck, t => t.ShowLine, (t, v) => t.ShowLine = v),
        (TbShowFreehandCheck, t => t.ShowFreehand, (t, v) => t.ShowFreehand = v),
        (TbShowNumberBadgeCheck, t => t.ShowNumberBadge, (t, v) => t.ShowNumberBadge = v),
        (TbShowTextCheck, t => t.ShowText, (t, v) => t.ShowText = v),
        (TbShowMosaicCheck, t => t.ShowMosaic, (t, v) => t.ShowMosaic = v),
        (TbShowSpotlightCheck, t => t.ShowSpotlight, (t, v) => t.ShowSpotlight = v),
        (TbShowHighlightCheck, t => t.ShowHighlight, (t, v) => t.ShowHighlight = v),
        (TbShowEraserCheck, t => t.ShowEraser, (t, v) => t.ShowEraser = v),
        (TbShowUndoRedoCheck, t => t.ShowUndoRedo, (t, v) => t.ShowUndoRedo = v),
        (TbShowLongshotCheck, t => t.ShowLongshot, (t, v) => t.ShowLongshot = v),
        (TbShowCopyCheck, t => t.ShowCopy, (t, v) => t.ShowCopy = v),
        (TbShowSaveCheck, t => t.ShowSave, (t, v) => t.ShowSave = v),
        (TbShowPinCheck, t => t.ShowPin, (t, v) => t.ShowPin = v),
        (TbShowOcrCheck, t => t.ShowOcr, (t, v) => t.ShowOcr = v),
        (TbShowTranslateCheck, t => t.ShowTranslate, (t, v) => t.ShowTranslate = v),
    ];

    private void ApplyToolbarCheckboxes(ToolbarVisibilitySettings toolbar)
    {
        foreach (var (box, get, _) in GetToolbarVisibilityBindings())
        {
            box.IsChecked = get(toolbar);
        }
    }

    private void OnToolbarVisibilityChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        foreach (var (box, _, set) in GetToolbarVisibilityBindings())
        {
            set(_working.Capture.Toolbar, box.IsChecked == true);
        }
        MarkDirty();
    }

    private void OnShowAllToolbarButtonsClick(object sender, RoutedEventArgs e)
    {
        _working.Capture.Toolbar.ShowAll();
        var prevLoading = _loading;
        _loading = true;
        try
        {
            ApplyToolbarCheckboxes(_working.Capture.Toolbar);
        }
        finally
        {
            _loading = prevLoading;
        }
        MarkDirty();
    }

    private void OnCaptureSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _working.Capture.SnapThresholdDip = (int)Math.Round(SnapThresholdSlider.Value);
        SnapThresholdVal.Text = $"{_working.Capture.SnapThresholdDip} px";

        _working.Capture.MagnifierZoom = (int)Math.Round(MagZoomSlider.Value);
        MagZoomVal.Text = $"{_working.Capture.MagnifierZoom}x";

        _working.Save.JpegQuality = (int)Math.Round(QualitySlider.Value);
        QualityVal.Text = $"{_working.Save.JpegQuality}%";
        MarkDirty();
    }

    private void OnPinChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _working.Pin.ShowShadow = PinShadowCheck.IsChecked == true;
        _working.Pin.KeepPhysicalPixels = PinPixelCheck.IsChecked == true;
        _working.Pin.RestoreOnStartup = PinRestoreCheck.IsChecked == true;
        _working.Pin.EnableTextSelection = PinTextSelectCheck.IsChecked == true;
        MarkDirty();
    }

    private void OnPinSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        int pct = (int)Math.Round(PinOpacitySlider.Value);
        _working.Pin.DefaultOpacity = pct / 100.0;
        PinOpacityVal.Text = $"{pct}%";

        _working.Pin.MaxPinCount = (int)Math.Round(PinMaxSlider.Value);
        PinMaxVal.Text = $"{_working.Pin.MaxPinCount} 张";
        MarkDirty();
    }

    private void OnOcrChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (OcrEngineCombo.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<OcrEngineKind>(item.Tag as string, out var engine))
        {
            _working.Ocr.Engine = engine;
        }

        _working.Ocr.Languages = OcrLangsBox.Text
            .Split([',', '，', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _working.Ocr.DetectBarcodes = DetectBarcodesCheck.IsChecked == true;
        MarkDirty();
    }

    private void OnOcrSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        int mins = (int)Math.Round(OcrIdleSlider.Value);
        _working.Ocr.UnloadAfterIdleMinutes = mins;
        OcrIdleVal.Text = mins == 0 ? "不卸载" : $"{mins} 分钟";
        MarkDirty();
    }

    private void OnBrowseSaveDirClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择截图默认保存文件夹",
            SelectedPath = Directory.Exists(SaveDirBox.Text) ? SaveDirBox.Text : Paths.ScreenshotsDirectory,
            UseDescriptionForTitle = true,
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            SaveDirBox.Text = dialog.SelectedPath;
        }
    }

    private void OnOpenSaveDirClick(object sender, RoutedEventArgs e) =>
        OpenFolder(string.IsNullOrWhiteSpace(SaveDirBox.Text) ? Paths.ScreenshotsDirectory : SaveDirBox.Text);

    private void OnOpenModelsDirClick(object sender, RoutedEventArgs e)
    {
        OpenFolder(Paths.UserModelsDirectory);
        RefreshOcrDiagnostics();
    }

    private void OnUnloadOcrClick(object sender, RoutedEventArgs e)
    {
        _ocrService.UnloadModels();
        RefreshOcrDiagnostics();
        FooterStatusText.Text = "已释放内存中的 OCR 识别模型";
    }

    private void OnOpenAppDataClick(object sender, RoutedEventArgs e) => OpenFolder(Paths.AppDataRoot);

    private void OnOpenLogsClick(object sender, RoutedEventArgs e) => OpenFolder(Log.LogDirectory);

    private void OnExportBackupClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "ZIP 压缩包 (*.zip)|*.zip",
            FileName = $"Ppinpix-backup-{DateTime.Now:yyyyMMdd-HHmm}.zip",
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            // 复制设置并在导出副本中剥离所有 DPAPI 密文凭据，保证备份零秘密泄露
            var sanitized = _working.Clone();
            foreach (var p in sanitized.Translate.Profiles)
            {
                p.EncryptedApiKey = string.Empty;
                p.Proxy.EncryptedPassword = string.Empty;
            }
            string sanitizedJson = JsonSerializer.Serialize(sanitized, SettingsStore.JsonOptions);

            using var archive = ZipFile.Open(dialog.FileName, ZipArchiveMode.Create);
            var settingsEntry = archive.CreateEntry("settings.sanitized.json");
            using (var writer = new StreamWriter(settingsEntry.Open()))
            {
                writer.Write(sanitizedJson);
            }

            FooterStatusText.Text = $"已导出脱敏配置备份：{dialog.FileName}";
        }
        catch (Exception ex)
        {
            Log.Error("导出配置备份失败。", ex);
            MessageBox.Show(this, $"导出失败：{ex.Message}", "Ppinpix", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnFactoryResetClick(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            this,
            "确定要将全部设置恢复为出厂默认值吗？（自定义大模型配置将被清空，已保存的截图文件不受影响。）",
            "确认重置",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        _working = new AppSettings();
        ApplyWindowTheme(_working.Ui.Theme);
        LoadFromSettings(_working);
        MarkDirty();
        ApplyCurrent();
        FooterStatusText.Text = "已恢复出厂默认设置并保存";
    }

    private void ApplyCurrent()
    {
        _saveTimer.Stop();
        var results = _onApply(_working.Clone());
        _dirty = false;

        var failed = results.Where(r => !r.IsRegistered && !r.Gesture.IsEmpty).ToList();
        if (failed.Count > 0)
        {
            string desc = string.Join("；", failed.Select(f => $"{f.Name}={f.Gesture}"));
            HotkeyWarningText.Text = $"以下全局热键被其他软件占用，注册失败：{desc}";
            HotkeyWarningText.Visibility = Visibility.Visible;
            FooterStatusText.Text = $"已保存，但部分热键冲突：{desc}";
        }
        else
        {
            CheckLocalHotkeyDuplicates();
            FooterStatusText.Text = "已自动保存";
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // 自动保存：关闭时若还有未落盘的改动，直接保存，不再弹确认框
        if (_dirty)
        {
            ApplyCurrent();
        }
        _saveTimer.Stop();
        base.OnClosing(e);
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"打开目录失败：{path}", ex);
        }
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
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

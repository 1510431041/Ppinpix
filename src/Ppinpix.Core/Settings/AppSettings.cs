using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ppinpix.Core.Settings;

/// <summary>应用全部可持久化设置。字段一律带默认值，新增字段不需要迁移即可兼容旧文件。</summary>
public sealed class AppSettings
{
    /// <summary>配置结构版本，用于将来做迁移。</summary>
    public int SchemaVersion { get; set; } = 2;

    public GeneralSettings General { get; set; } = new();

    public HotkeySettings Hotkeys { get; set; } = new();

    public CaptureSettings Capture { get; set; } = new();

    public AnnotationSettings Annotation { get; set; } = new();

    public SaveSettings Save { get; set; } = new();

    public PinSettings Pin { get; set; } = new();

    public OcrSettings Ocr { get; set; } = new();

    public TranslateSettings Translate { get; set; } = new();

    public UiSettings Ui { get; set; } = new();

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, SettingsStore.JsonOptions), SettingsStore.JsonOptions)
        ?? new AppSettings();
}

public sealed class GeneralSettings
{
    /// <summary>开机自动启动。</summary>
    public bool AutoStart { get; set; }

    /// <summary>界面语言（默认简体中文）。</summary>
    public string Language { get; set; } = "zh-CN";
}

public sealed class HotkeySettings
{
    /// <summary>区域截图。</summary>
    public string Capture { get; set; } = "F1";

    /// <summary>把剪贴板内容贴到屏幕。</summary>
    public string PinClipboard { get; set; } = "F3";

    /// <summary>截图后直接翻译。</summary>
    public string CaptureTranslate { get; set; } = "Ctrl+Shift+T";

    /// <summary>截图后直接 OCR。</summary>
    public string CaptureOcr { get; set; } = "Ctrl+Shift+O";

    /// <summary>显示/隐藏所有贴图。</summary>
    public string TogglePins { get; set; } = "Ctrl+Shift+H";

    /// <summary>全局暂停所有热键（避免与其他软件冲突）。</summary>
    public string SuspendHotkeys { get; set; } = "Ctrl+Alt+F12";

    /// <summary>按名称取用，便于遍历注册。</summary>
    public IReadOnlyList<(string Name, string Value)> Enumerate() =>
    [
        (nameof(Capture), Capture),
        (nameof(PinClipboard), PinClipboard),
        (nameof(CaptureTranslate), CaptureTranslate),
        (nameof(CaptureOcr), CaptureOcr),
        (nameof(TogglePins), TogglePins),
        (nameof(SuspendHotkeys), SuspendHotkeys),
    ];
}

public sealed class CaptureSettings
{
    /// <summary>热键按下后延迟多少毫秒再抓屏，用于抓取菜单等瞬时界面。</summary>
    public int DelayMs { get; set; }

    /// <summary>是否把鼠标指针画进截图。</summary>
    public bool IncludeCursor { get; set; }

    /// <summary>是否吸附窗口与控件边缘。</summary>
    public bool SnapEnabled { get; set; } = true;

    /// <summary>吸附阈值（DIP）。</summary>
    public int SnapThresholdDip { get; set; } = 8;

    /// <summary>是否吸附子窗口/控件（关闭后只吸附顶层窗口）。</summary>
    public bool SnapToControls { get; set; } = true;

    /// <summary>截图时是否显示放大镜与色值。</summary>
    public bool ShowMagnifier { get; set; } = true;

    /// <summary>移动或调整已选区域时是否隐藏放大镜（拉新选区时始终显示）。默认隐藏，避免遮挡。</summary>
    public bool HideMagnifierWhileDragging { get; set; } = true;

    /// <summary>放大镜倍率。</summary>
    public int MagnifierZoom { get; set; } = 8;

    /// <summary>取色默认格式是否为 RGB（false = HEX，true = RGB）。</summary>
    public bool DefaultColorFormatRgb { get; set; }

    /// <summary>点击工具栏复制按钮或 Ctrl+C 后是否自动关闭覆盖层。</summary>
    public bool CloseOnCopy { get; set; }
}

public sealed class AnnotationSettings
{
    /// <summary>默认标注颜色（HEX）。</summary>
    public string DefaultColorHex { get; set; } = "#F04438";

    /// <summary>默认线宽（2 / 3 / 5）。</summary>
    public double DefaultLineWidth { get; set; } = 3;

    /// <summary>文字标注默认字号。</summary>
    public double DefaultFontSize { get; set; } = 18;

    /// <summary>序号标注默认直径（20 / 24 / 32）。</summary>
    public int DefaultBadgeSize { get; set; } = 24;

    /// <summary>马赛克块大小（8 / 12 / 16 物理像素）。</summary>
    public int DefaultMosaicBlockPx { get; set; } = 12;

    /// <summary>高斯模糊半径。</summary>
    public int DefaultBlurRadius { get; set; } = 12;

    /// <summary>删除序号后是否自动重排剩余序号。</summary>
    public bool AutoRenumberBadges { get; set; } = true;
}

public sealed class SaveSettings
{
    public string Directory { get; set; } = Paths.ScreenshotsDirectory;

    public ImageFormat Format { get; set; } = ImageFormat.Png;

    public int JpegQuality { get; set; } = 92;

    /// <summary>文件名模板，支持 {yyyy} {MM} {dd} {HH} {mm} {ss} {w} {h}。</summary>
    public string FileNameTemplate { get; set; } = "Ppinpix_{yyyy}{MM}{dd}_{HH}{mm}{ss}";

    /// <summary>保存后是否复制到剪贴板。</summary>
    public bool CopyAfterSave { get; set; } = true;
}

public enum ImageFormat
{
    Png = 0,
    Jpeg = 1,
    Webp = 2,
}

public sealed class PinSettings
{
    /// <summary>新贴图的默认透明度（1.0 = 不透明）。</summary>
    public double DefaultOpacity { get; set; } = 1.0;

    /// <summary>贴到屏幕时是否保留原缩放（保持物理像素 1:1）。</summary>
    public bool KeepPhysicalPixels { get; set; } = true;

    /// <summary>贴图是否显示阴影。</summary>
    public bool ShowShadow { get; set; } = true;

    /// <summary>退出时保存贴图，下次启动恢复。</summary>
    public bool RestoreOnStartup { get; set; }

    /// <summary>贴图出现后立即在后台做 OCR，供鼠标划选复制。</summary>
    public bool EnableTextSelection { get; set; } = true;

    /// <summary>同时存在的最大贴图数量。</summary>
    public int MaxPinCount { get; set; } = 30;
}

public sealed class OcrSettings
{
    public OcrEngineKind Engine { get; set; } = OcrEngineKind.RapidOcr;

    /// <summary>启用的语言包标识，例如 ["ch","en"]。</summary>
    public List<string> Languages { get; set; } = ["ch", "en"];

    /// <summary>是否同时识别二维码/条码。</summary>
    public bool DetectBarcodes { get; set; } = true;

    /// <summary>空闲多少分钟后释放模型内存，0 表示不释放。</summary>
    public int UnloadAfterIdleMinutes { get; set; } = 5;
}

public enum OcrEngineKind
{
    RapidOcr = 0,
    WindowsSystem = 1,
}

public sealed class TranslateSettings
{
    /// <summary>当前默认使用的接口配置 Id。</summary>
    public string ActiveProfileId { get; set; } = string.Empty;

    /// <summary>源语言，"auto" 表示自动判断（中→英、非中→中）。</summary>
    public string SourceLanguage { get; set; } = "auto";

    public string TargetLanguage { get; set; } = "auto";

    /// <summary>默认呈现方式（默认直接在原图贴图上进行原位译文覆盖）。</summary>
    public TranslationPresentation Presentation { get; set; } = TranslationPresentation.Overlay;

    /// <summary>首次发起翻译前的隐私提示是否已确认。</summary>
    public bool DiscloseRemoteConfirmed { get; set; }

    public List<LlmProfile> Profiles { get; set; } = [LlmProfile.CreateDefault()];
}

public enum TranslationPresentation
{
    /// <summary>原文/译文上下对照浮窗。</summary>
    Compare = 0,

    /// <summary>原图原位排版覆盖。</summary>
    Overlay = 1,
}

public enum LlmProtocol
{
    /// <summary>OpenAI 兼容（OpenAI、DeepSeek、通义、Moonshot、Ollama、各类网关）。</summary>
    OpenAiCompatible = 0,

    AnthropicMessages = 1,

    Gemini = 2,
}

public enum TranslationInputMode
{
    /// <summary>先本地 OCR 识字，只把文字发给模型。</summary>
    OcrThenTranslate = 0,

    /// <summary>直接把图片发给多模态模型。</summary>
    ImageDirectly = 1,
}

public sealed class ProxySettings
{
    public ProxyMode Mode { get; set; } = ProxyMode.System;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public string UserName { get; set; } = string.Empty;

    /// <summary>DPAPI 密文。</summary>
    public string EncryptedPassword { get; set; } = string.Empty;
}

public enum ProxyMode
{
    /// <summary>不使用代理，直连。</summary>
    None = 0,

    /// <summary>跟随系统（WinINet/环境变量）。</summary>
    System = 1,

    Http = 2,

    Socks5 = 3,
}

/// <summary>一套第三方大模型接口配置。</summary>
public sealed class LlmProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    public string Name { get; set; } = "新配置";

    public LlmProtocol Protocol { get; set; } = LlmProtocol.OpenAiCompatible;

    /// <summary>接口地址，例如 https://api.2020111.xyz/v1 。</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>是否自动在 BaseUrl 后补 /chat/completions；关闭表示 BaseUrl 已是完整端点。</summary>
    public bool AutoAppendPath { get; set; } = true;

    public string Model { get; set; } = string.Empty;

    /// <summary>DPAPI 密文，绝不落明文。</summary>
    public string EncryptedApiKey { get; set; } = string.Empty;

    /// <summary>自定义翻译提示词；为空时使用内置模板。支持 {source_lang} {target_lang} {text}。</summary>
    public string Prompt { get; set; } = string.Empty;

    public TranslationInputMode InputMode { get; set; } = TranslationInputMode.OcrThenTranslate;

    /// <summary>该模型是否支持图片输入（决定能否选“直接发图翻译”）。</summary>
    public bool SupportsVision { get; set; }

    public ProxySettings Proxy { get; set; } = new();

    public double Temperature { get; set; } = 0.3;

    public int? MaxTokens { get; set; }

    public int TimeoutSeconds { get; set; } = 60;

    public bool Stream { get; set; } = true;

    public Dictionary<string, string> ExtraHeaders { get; set; } = [];

    /// <summary>内置的默认配置：公司内部网关。</summary>
    public static LlmProfile CreateDefault() => new()
    {
        Id = "default",
        Name = "内部网关（默认）",
        BaseUrl = "https://api.2020111.xyz/v1",
        AutoAppendPath = true,
        Model = "翻译",
        InputMode = TranslationInputMode.OcrThenTranslate,
        Temperature = 0.3,
        Stream = true,
    };
}

public sealed class UiSettings
{
    /// <summary>界面主题。</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>是否启用界面动效。</summary>
    public bool AnimationsEnabled { get; set; } = true;

    /// <summary>截图完成后是否播放提示音。</summary>
    public bool PlaySoundOnCapture { get; set; } = true;
}

public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>读取与写入 settings.json。写入采用「临时文件 + 原子替换」，避免断电/崩溃写坏配置。</summary>
public sealed class SettingsStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly Lock _gate = new();

    public SettingsStore(string? path = null) => _path = path ?? Paths.SettingsFile;

    public string Path => _path;

    public AppSettings Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path)) return new AppSettings();

                string json = File.ReadAllText(_path);
                if (string.IsNullOrWhiteSpace(json)) return new AppSettings();

                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings is null) return new AppSettings();

                Normalize(settings);
                return settings;
            }
            catch (Exception ex)
            {
                Log.Error($"读取设置失败，将使用默认设置：{_path}", ex);
                BackupCorrupted();
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                string json = JsonSerializer.Serialize(settings, JsonOptions);

                string temp = _path + ".tmp";
                File.WriteAllText(temp, json, System.Text.Encoding.UTF8);

                if (File.Exists(_path))
                {
                    File.Replace(temp, _path, null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temp, _path);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"写入设置失败：{_path}", ex);
                throw;
            }
        }
    }

    /// <summary>修正非法值，保证运行期不出现 0 尺寸、越界缩放之类的问题。</summary>
    private static void Normalize(AppSettings s)
    {
        if (s.SchemaVersion < 2)
        {
            s.Translate.Presentation = TranslationPresentation.Overlay;
            s.SchemaVersion = 2;
        }

        s.Capture.SnapThresholdDip = Math.Clamp(s.Capture.SnapThresholdDip, 0, 40);
        s.Capture.DelayMs = Math.Clamp(s.Capture.DelayMs, 0, 10_000);
        s.Capture.MagnifierZoom = Math.Clamp(s.Capture.MagnifierZoom, 2, 16);

        s.Pin.DefaultOpacity = Math.Clamp(s.Pin.DefaultOpacity, 0.1, 1.0);
        s.Pin.MaxPinCount = Math.Clamp(s.Pin.MaxPinCount, 1, 200);
        s.Annotation.DefaultLineWidth = Math.Clamp(s.Annotation.DefaultLineWidth, 1, 24);
        s.Annotation.DefaultFontSize = Math.Clamp(s.Annotation.DefaultFontSize, 8, 96);
        s.Annotation.DefaultBadgeSize = Math.Clamp(s.Annotation.DefaultBadgeSize, 16, 48);
        s.Annotation.DefaultMosaicBlockPx = Math.Clamp(s.Annotation.DefaultMosaicBlockPx, 4, 48);
        s.Annotation.DefaultBlurRadius = Math.Clamp(s.Annotation.DefaultBlurRadius, 2, 48);
        s.Ocr.UnloadAfterIdleMinutes = Math.Clamp(s.Ocr.UnloadAfterIdleMinutes, 0, 240);

        s.Save.JpegQuality = Math.Clamp(s.Save.JpegQuality, 10, 100);
        if (string.IsNullOrWhiteSpace(s.Save.Directory)) s.Save.Directory = Paths.ScreenshotsDirectory;
        if (string.IsNullOrWhiteSpace(s.Save.FileNameTemplate)) s.Save.FileNameTemplate = "Ppinpix_{yyyy}{MM}{dd}_{HH}{mm}{ss}";

        if (s.Translate.Profiles.Count == 0) s.Translate.Profiles.Add(LlmProfile.CreateDefault());
        foreach (var p in s.Translate.Profiles)
        {
            if (string.IsNullOrWhiteSpace(p.Id)) p.Id = Guid.NewGuid().ToString("n");
            if (string.IsNullOrWhiteSpace(p.Name)) p.Name = "未命名配置";
        }

        if (string.IsNullOrEmpty(s.Translate.ActiveProfileId) ||
            !s.Translate.Profiles.Any(p => p.Id == s.Translate.ActiveProfileId))
        {
            s.Translate.ActiveProfileId = s.Translate.Profiles[0].Id;
        }
    }

    private void BackupCorrupted()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Copy(_path, _path + ".corrupted", overwrite: true);
            }
        }
        catch
        {
            // 忽略
        }
    }
}

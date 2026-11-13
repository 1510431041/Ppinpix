using System.Diagnostics;
using System.Windows;
using Ppinpix.Capture.Overlay;
using Ppinpix.Core;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Display;
using Ppinpix.Core.Settings;
using Ppinpix.LlmGateway.Models;
using Ppinpix.Ocr;
using Ppinpix.Ocr.Models;
using Ppinpix.Pin;
using Ppinpix.Translate;

namespace Ppinpix.App.Translate;

/// <summary>
/// 贴图原位翻译协调器：
/// 当呈现方式为「原位覆盖贴图（默认）」时，不打开独立翻译窗口，
/// 直接在捕获选区对应位置生成（或复用当前）贴图并流式原位绘制译文，
/// 支持按住 Alt 对比原图、停止/重试、切换目标语言以及按需转入对照窗口。
/// </summary>
internal sealed class PinTranslationCoordinator
{
    private readonly Func<AppSettings> _getSettings;
    private readonly SettingsStore _store;
    private readonly OcrService _ocrService;
    private readonly TranslateService _translateService;
    private readonly DisplayService _displays;
    private readonly PinManager _pinManager;
    private readonly Func<Window?> _getDialogOwner;
    private readonly Func<ScreenFrame?, string?, bool, OcrResult?, Task> _openTranslateWindow;
    private readonly Dictionary<string, CancellationTokenSource> _activeByPinId = new(StringComparer.OrdinalIgnoreCase);

    public PinTranslationCoordinator(
        Func<AppSettings> getSettings,
        SettingsStore store,
        OcrService ocrService,
        TranslateService translateService,
        DisplayService displays,
        PinManager pinManager,
        Func<Window?> getDialogOwner,
        Func<ScreenFrame?, string?, bool, OcrResult?, Task> openTranslateWindow)
    {
        _getSettings = getSettings;
        _store = store;
        _ocrService = ocrService;
        _translateService = translateService;
        _displays = displays;
        _pinManager = pinManager;
        _getDialogOwner = getDialogOwner;
        _openTranslateWindow = openTranslateWindow;
    }

    public async Task TranslateCaptureAsync(ScreenFrame capturedFrame)
    {
        if (!Features.Translate) return;

        var settings = _getSettings();
        if (settings.Translate.Presentation == TranslationPresentation.Compare)
        {
            await _openTranslateWindow(capturedFrame, null, true, null);
            return;
        }

        var profile = ResolveActiveProfile(settings);
        if (!EnsurePrivacyConfirmed(settings, profile))
        {
            ToastWindow.Show("已取消翻译（未确认外发隐私提示）", ToastKind.Info, _displays);
            return;
        }

        var token = UserInitiatedToken.IssueFromUserAction();
        var pin = _pinManager.CreateFromFrame(capturedFrame, PinKind.Image, deferSilentOcr: true);
        await RunOverlayOnPinAsync(pin, token, targetLangOverride: null);
    }

    public async Task TranslateExistingPinAsync(PinWindow pin, string? targetLangOverride = null)
    {
        if (!Features.Translate) return;

        var settings = _getSettings();
        if (settings.Translate.Presentation == TranslationPresentation.Compare &&
            string.IsNullOrWhiteSpace(targetLangOverride) &&
            !pin.HasOverlayTranslation)
        {
            await _openTranslateWindow(pin.GetCompareSourceFrame(), null, true, pin.LastOcr);
            return;
        }

        var profile = ResolveActiveProfile(settings);
        if (!EnsurePrivacyConfirmed(settings, profile))
        {
            ToastWindow.Show("已取消翻译（未确认外发隐私提示）", ToastKind.Info, _displays);
            return;
        }

        var token = UserInitiatedToken.IssueFromUserAction();
        await RunOverlayOnPinAsync(pin, token, targetLangOverride);
    }

    public void CancelPinTranslation(PinWindow pin)
    {
        if (_activeByPinId.TryGetValue(pin.Id, out var cts))
        {
            cts.Cancel();
        }
    }

    private async Task RunOverlayOnPinAsync(
        PinWindow pin,
        UserInitiatedToken userToken,
        string? targetLangOverride)
    {
        var settings = _getSettings();
        if (!string.IsNullOrWhiteSpace(targetLangOverride))
        {
            settings.Translate.TargetLanguage = targetLangOverride;
            _store.Save(settings);
        }

        var profile = ResolveActiveProfile(settings);

        if (_activeByPinId.TryGetValue(pin.Id, out var previous))
        {
            previous.Cancel();
            previous.Dispose();
        }

        var cts = new CancellationTokenSource();
        _activeByPinId[pin.Id] = cts;

        EventHandler onClosed = (_, _) => cts.Cancel();
        pin.Closed += onClosed;

        var baseFrame = pin.BeginOverlayTranslation();

        try
        {
            var ocr = pin.OriginalOcr;
            if (ocr is null)
            {
                pin.SetTranslationStatus("正在离线识别文字…", autoHideMs: 0);
                ocr = await _ocrService.RecognizeAsync(baseFrame, cts.Token);
                cts.Token.ThrowIfCancellationRequested();
                pin.SetOcrCache(ocr);
            }

            if (ocr.Paragraphs.Count == 0)
            {
                pin.FinishOverlayWithoutChanges("未检测到文字 · 已保留原图", autoHideMs: 2600);
                ToastWindow.Show("未从截图识别出文字，已保留原图贴图", ToastKind.Info, _displays);
                return;
            }

            int total = ocr.Paragraphs.Count;
            pin.SetTranslationStatus($"正在翻译 ({total} 段)…", autoHideMs: 0);

            var sw = Stopwatch.StartNew();
            OverlayProgress? lastProgress = null;

            await foreach (var prog in _translateService.TranslateOverlayStreamAsync(
                userToken,
                baseFrame,
                ocr,
                profile,
                settings.Translate.SourceLanguage,
                settings.Translate.TargetLanguage,
                cts.Token))
            {
                lastProgress = prog;
                int done = prog.TranslatedParagraphs.Count;
                if (done > 0)
                {
                    string status = prog.IsCompleted
                        ? $"正在完成排版 ({done}/{total})…"
                        : $"正在翻译 {done}/{total}…";
                    pin.ApplyOverlayTranslationProgress(prog.RenderedFrame, status);
                }
            }

            sw.Stop();
            if (lastProgress is { TranslatedParagraphs.Count: > 0 } final)
            {
                pin.CompleteOverlayTranslation(
                    final.RenderedFrame,
                    $"已译 {final.TranslatedParagraphs.Count}/{total} 段 · {sw.ElapsedMilliseconds} ms · 按住 Alt 对比原图");
            }
            else
            {
                pin.FinishOverlayWithoutChanges("未解析出译文 · 右键重试或打开对照窗口", autoHideMs: 3000);
                ToastWindow.Show("模型未返回可原位覆盖的段落译文，已保留原图，可右键重试或在对照窗口查看", ToastKind.Warning, _displays);
            }
        }
        catch (OperationCanceledException)
        {
            pin.FinishOverlayWithoutChanges(
                pin.HasOverlayTranslation ? "已停止继续翻译" : "已取消翻译",
                autoHideMs: 1600);
        }
        catch (LlmException ex)
        {
            Log.Warn("贴图原位翻译失败。", ex);
            pin.FinishOverlayWithoutChanges("翻译失败 · 右键重试或检查接口设置", autoHideMs: 3200);
            ToastWindow.Show($"翻译失败：{ex.Message}", ToastKind.Error, _displays);
        }
        catch (Exception ex)
        {
            Log.Warn("贴图原位翻译发生异常。", ex);
            pin.FinishOverlayWithoutChanges("翻译失败 · 右键重试或打开对照窗口", autoHideMs: 3200);
            ToastWindow.Show($"翻译失败：{ex.Message}", ToastKind.Error, _displays);
        }
        finally
        {
            pin.Closed -= onClosed;
            if (_activeByPinId.TryGetValue(pin.Id, out var registered) && ReferenceEquals(registered, cts))
            {
                _activeByPinId.Remove(pin.Id);
            }
            cts.Dispose();
        }
    }

    private bool EnsurePrivacyConfirmed(AppSettings settings, LlmProfile profile)
    {
        if (settings.Translate.DiscloseRemoteConfirmed) return true;

        var dialog = new PrivacyDisclosureDialog(profile);
        if (_getDialogOwner() is { IsVisible: true } owner)
        {
            dialog.Owner = owner;
        }
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        if (dialog.RememberChoice)
        {
            settings.Translate.DiscloseRemoteConfirmed = true;
            _store.Save(settings);
        }
        return true;
    }

    private static LlmProfile ResolveActiveProfile(AppSettings settings) =>
        settings.Translate.Profiles.FirstOrDefault(p => p.Id == settings.Translate.ActiveProfileId)
        ?? settings.Translate.Profiles[0];
}

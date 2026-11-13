using System.IO;
using System.IO.Pipes;
using System.Media;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Ppinpix.App.CaptureFlow;
using Ppinpix.App.Ocr;
using Ppinpix.App.Settings;
using Ppinpix.App.Startup;
using Ppinpix.App.Translate;
using Ppinpix.App.Tray;
using Ppinpix.Capture.Overlay;
using Ppinpix.Capture.Session;
using Ppinpix.Core;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Display;
using Ppinpix.Core.Hotkeys;
using Ppinpix.Core.Interop;
using Ppinpix.Core.Settings;
using Ppinpix.Ocr;
using Ppinpix.Ocr.Models;
using Ppinpix.Pin;
using Ppinpix.Theme;
using Ppinpix.Translate;

namespace Ppinpix.App;

public partial class App : Application
{
    private const string MutexName = @"Local\Ppinpix.SingleInstance.Mutex";
    private const string PipeName = "Ppinpix.SingleInstance.Pipe";

    private Mutex? _singleInstanceMutex;
    private CancellationTokenSource? _pipeCts;

    private SettingsStore _store = null!;
    private AppSettings _settings = null!;
    private DisplayService _displays = null!;
    private HotkeyManager _hotkeys = null!;
    private OcrService _ocrService = null!;
    private TranslateService _translateService = null!;
    private PinManager _pinManager = null!;
    private TrayIcon _trayIcon = null!;

    private CaptureSession? _activeCapture;
    private DelayCountdownWindow? _delayWindow;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DisplayApi.SetProcessDpiAwarenessContext(DisplayApi.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            string cmd = e.Args.Any(a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(a, "/settings", StringComparison.OrdinalIgnoreCase))
                ? "SETTINGS"
                : "CAPTURE";
            NotifyPrimaryInstance(cmd);
            Shutdown();
            return;
        }

        HookGlobalExceptions();

        Paths.EnsureCreated();
        ThemeLoader.EnsureLoaded(this);

        _store = new SettingsStore();
        _settings = _store.Load();
        ApplySettingsTheme(_settings.Ui.Theme);
        if (AutoStartService.IsEnabled() != _settings.General.AutoStart)
        {
            AutoStartService.SetEnabled(_settings.General.AutoStart);
        }

        _displays = new DisplayService();
        _ocrService = new OcrService(() => _settings.Ocr);
        _translateService = new TranslateService(_ocrService);
        _pinManager = new PinManager(() => _settings, _displays, _ocrService);
        _pinManager.ConfigureCallbacks(
            onOpenOcr: (_, frame, pre) =>
            {
                OpenOcrWindow(frame, pre);
                return Task.CompletedTask;
            },
            onOpenTranslate: (pin, _) => GetPinTranslator().TranslateExistingPinAsync(pin),
            onCancelTranslate: pin => GetPinTranslator().CancelPinTranslation(pin),
            onOpenCompareTranslate: (_, frame, pre) => OpenTranslateWindowAsync(frame, initialText: null, forceCompareView: true, precomputedOcr: pre),
            getTranslateLanguages: () => LanguageDetector.SupportedLanguages.Select(l => (l.Code, l.DisplayName)).ToList(),
            onRetranslateWithLang: (pin, _, langCode) => GetPinTranslator().TranslateExistingPinAsync(pin, langCode),
            getActiveTargetLanguage: () => _settings.Translate.TargetLanguage);

        _hotkeys = new HotkeyManager();
        _hotkeys.Pressed += OnHotkeyPressed;

        _trayIcon = new TrayIcon(
            _settings.Hotkeys,
            onCapture: () => StartCaptureFlow(_settings.Capture.DelayMs),
            onDelayedCapture: ms => StartCaptureFlow(ms),
            onPinClipboard: PinFromClipboard,
            onTogglePins: () => _pinManager.ToggleShowHideAll(),
            onToggleSuspend: ToggleSuspendHotkeys,
            onOpenSettings: () => OpenSettings("General"),
            onExit: Shutdown);

        RegisterHotkeysAndWarnOnStartup();

        if (_settings.Pin.RestoreOnStartup)
        {
            _pinManager.RestoreSession();
        }

        StartPipeServer();
        Log.Info("Ppinpix 启动完成，已驻留系统托盘。");

        if (e.Args.Any(a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase)))
        {
            OpenSettings("General");
        }
    }

    private void HookGlobalExceptions()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("UI 线程未捕获异常。", args.Exception);
            args.Handled = true;
            _trayIcon?.ShowBalloon("Ppinpix 发生错误", args.Exception.Message, error: true);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Error("AppDomain 未捕获异常。", args.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("后台任务未捕获异常。", args.Exception);
            args.SetObserved();
        };
    }

    private void RegisterHotkeysAndWarnOnStartup()
    {
        var results = _hotkeys.RegisterAll(_settings.Hotkeys.Enumerate());
        _trayIcon.UpdateHotkeys(_settings.Hotkeys, _hotkeys.IsSuspended);

        var failed = results.Where(r => !r.IsRegistered && !r.Gesture.IsEmpty).ToList();
        if (failed.Count > 0)
        {
            string desc = string.Join("、", failed.Select(f => $"{f.Name}({f.Gesture})"));
            _trayIcon.ShowBalloon("部分全局快捷键被占用", $"未能注册：{desc}，可在设置中调整。", error: false);
        }
    }

    private void OnHotkeyPressed(string name)
    {
        switch (name)
        {
            case nameof(HotkeySettings.Capture):
                StartCaptureFlow(_settings.Capture.DelayMs);
                break;

            case nameof(HotkeySettings.PinClipboard):
                PinFromClipboard();
                break;

            case nameof(HotkeySettings.CaptureOcr):
                StartCaptureFlow(delayMs: 0, CaptureAction.Ocr);
                break;

            case nameof(HotkeySettings.CaptureTranslate):
                StartCaptureFlow(delayMs: 0, CaptureAction.Translate);
                break;

            case nameof(HotkeySettings.TogglePins):
                _pinManager.ToggleShowHideAll();
                break;

            case nameof(HotkeySettings.SuspendHotkeys):
                ToggleSuspendHotkeys();
                break;
        }
    }

    private void StartCaptureFlow(int delayMs, CaptureAction? directAction = null)
    {
        if (_activeCapture is { IsFinished: false } || _delayWindow is not null)
        {
            return;
        }

        if (delayMs > 0)
        {
            _delayWindow = new DelayCountdownWindow(
                delayMs,
                _displays,
                onComplete: () =>
                {
                    _delayWindow = null;
                    BeginCaptureSession(directAction);
                },
                onCancel: () =>
                {
                    _delayWindow = null;
                });
            _delayWindow.Show();
            return;
        }

        BeginCaptureSession(directAction);
    }

    private void BeginCaptureSession(CaptureAction? directAction)
    {
        if (_activeCapture is { IsFinished: false }) return;

        try
        {
            _displays.Refresh();
            var request = new CaptureRequest(CaptureMode.Region, directAction);
            _activeCapture = CaptureSession.Start(_settings, _displays, request, OnCaptureFinished);
        }
        catch (Exception ex)
        {
            Log.Error("启动截图会话失败。", ex);
            _activeCapture = null;
            ToastWindow.Show($"截图启动失败：{ex.Message}", ToastKind.Error, _displays);
        }
    }

    private void OnCaptureFinished(CaptureResult result)
    {
        _activeCapture = null;
        if (result.Action == CaptureAction.Cancel || result.Frame is null)
        {
            return;
        }

        if (_settings.Ui.PlaySoundOnCapture)
        {
            try
            {
                SystemSounds.Asterisk.Play();
            }
            catch
            {
            }
        }

        switch (result.Action)
        {
            case CaptureAction.Pin:
                if (Features.Pin)
                {
                    _pinManager.CreateFromFrame(result.Frame, PinKind.Image);
                }
                break;

            case CaptureAction.Ocr:
                if (Features.Ocr)
                {
                    OpenOcrWindow(result.Frame, precomputed: null);
                }
                break;

            case CaptureAction.Translate:
                if (Features.Translate)
                {
                    _ = GetPinTranslator().TranslateCaptureAsync(result.Frame);
                }
                break;
        }
    }

    private void PinFromClipboard()
    {
        if (!Features.Pin) return;
        _pinManager.CreateFromClipboard();
    }

    private void ToggleSuspendHotkeys()
    {
        if (_hotkeys.IsSuspended)
        {
            _hotkeys.Resume();
            _trayIcon.ShowBalloon("全局热键已恢复", $"按 {_settings.Hotkeys.Capture} 可继续截图。");
        }
        else
        {
            _hotkeys.Suspend();
            _trayIcon.ShowBalloon("全局热键已暂停", $"再次按 {_settings.Hotkeys.SuspendHotkeys} 或在托盘中恢复。");
        }

        _trayIcon.UpdateHotkeys(_settings.Hotkeys, _hotkeys.IsSuspended);
    }

    private void OpenOcrWindow(ScreenFrame frame, OcrResult? precomputed)
    {
        var window = new OcrResultWindow(
            frame,
            _ocrService,
            _displays,
            precomputed,
            onSendToTranslate: Features.Translate
                ? text => _ = OpenTranslateWindowAsync(frame, text, forceCompareView: true)
                : null);
        window.Show();
        window.Activate();
    }

    private PinTranslationCoordinator? _pinTranslator;

    private PinTranslationCoordinator GetPinTranslator() =>
        _pinTranslator ??= new PinTranslationCoordinator(
            () => _settings,
            _store,
            _ocrService,
            _translateService,
            _displays,
            _pinManager,
            () => _settingsWindow,
            OpenTranslateWindowAsync);

    private Task OpenTranslateWindowAsync(
        ScreenFrame? sourceFrame,
        string? initialText,
        bool forceCompareView = false,
        OcrResult? precomputedOcr = null)
    {
        if (!Features.Translate) return Task.CompletedTask;

        var profile = _settings.Translate.Profiles.FirstOrDefault(p => p.Id == _settings.Translate.ActiveProfileId)
                      ?? _settings.Translate.Profiles[0];
        if (!_settings.Translate.DiscloseRemoteConfirmed)
        {
            var dialog = new PrivacyDisclosureDialog(profile);
            if (_settingsWindow is { IsVisible: true })
            {
                dialog.Owner = _settingsWindow;
            }
            if (dialog.ShowDialog() != true)
            {
                return Task.CompletedTask;
            }

            if (dialog.RememberChoice)
            {
                _settings.Translate.DiscloseRemoteConfirmed = true;
                _store.Save(_settings);
            }
        }

        var token = UserInitiatedToken.IssueFromUserAction();
        var window = new TranslateWindow(
            sourceFrame,
            initialText,
            _settings,
            _store,
            _ocrService,
            _translateService,
            _displays,
            onPinFrame: frame => _pinManager.CreateFromFrame(frame, PinKind.Image),
            onOpenSettings: () => OpenSettings("Translate"),
            userToken: token,
            forceCompareView: forceCompareView,
            precomputedOcr: precomputedOcr);
        window.Show();
        window.Activate();
        return Task.CompletedTask;
    }

    private void OpenSettings(string sectionTag)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.SelectSection(sectionTag);
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(
            _settings,
            _ocrService,
            SaveSettingsAndApply,
            initialSection: sectionTag);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private IReadOnlyList<HotkeyRegistrationResult> SaveSettingsAndApply(AppSettings updated)
    {
        _settings = updated;
        _store.Save(_settings);
        AutoStartService.SetEnabled(_settings.General.AutoStart);
        ApplySettingsTheme(_settings.Ui.Theme);
        _ocrService.NotifySettingsChanged(_settings.Ocr);

        bool wasSuspended = _hotkeys.IsSuspended;
        var results = _hotkeys.RegisterAll(_settings.Hotkeys.Enumerate());
        if (wasSuspended)
        {
            _hotkeys.Suspend();
        }
        _trayIcon.UpdateHotkeys(_settings.Hotkeys, _hotkeys.IsSuspended);
        return results;
    }

    private void ApplySettingsTheme(AppTheme theme)
    {
        bool isDark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => ThemeLoader.IsSystemDarkTheme(),
        };
        ThemeLoader.ApplySettingsTheme(Resources, isDark);
    }

    private void StartPipeServer()
    {
        _pipeCts = new CancellationTokenSource();
        var ct = _pipeCts.Token;

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    string? cmd = await reader.ReadLineAsync(ct).ConfigureAwait(false);

                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (string.Equals(cmd, "SETTINGS", StringComparison.OrdinalIgnoreCase))
                        {
                            OpenSettings("General");
                        }
                        else
                        {
                            StartCaptureFlow(_settings.Capture.DelayMs);
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Warn("单实例命名管道监听异常。", ex);
                    await Task.Delay(250, ct).ConfigureAwait(false);
                }
            }
        }, ct);
    }

    private static void NotifyPrimaryInstance(string command)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(800);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(command);
        }
        catch (Exception ex)
        {
            Log.Warn("通知主实例激活失败。", ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeCts?.Cancel();
        _pipeCts?.Dispose();

        _activeCapture?.Dispose();
        _delayWindow?.Close();

        try
        {
            _pinManager?.SaveSession();
        }
        catch (Exception ex)
        {
            Log.Warn("退出保存贴图状态失败。", ex);
        }

        _pinManager?.Dispose();
        _trayIcon?.Dispose();
        _hotkeys?.Dispose();
        _ocrService?.Dispose();
        _displays?.Dispose();

        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch
        {
        }
        _singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }
}

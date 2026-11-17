using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ppinpix.Capture.Overlay;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Display;
using Ppinpix.Ocr;
using Ppinpix.Ocr.Models;
using Ppinpix.Theme;

namespace Ppinpix.App.Ocr;

public partial class OcrResultWindow : Window
{
    private readonly OcrService _ocrService;
    private readonly DisplayService _displays;
    private readonly ScreenFrame _frame;
    private readonly Action<string>? _onSendToTranslate;
    private CancellationTokenSource? _cts;

    public OcrResultWindow(
        ScreenFrame frame,
        OcrService ocrService,
        DisplayService displays,
        OcrResult? precomputed = null,
        Action<string>? onSendToTranslate = null)
    {
        InitializeComponent();
        ThemeLoader.ApplySettingsTheme(Resources, isDark: true);

        _frame = frame;
        _ocrService = ocrService;
        _displays = displays;
        _onSendToTranslate = onSendToTranslate;

        TranslateBtn.Visibility = _onSendToTranslate is not null ? Visibility.Visible : Visibility.Collapsed;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };

        Closed += (_, _) =>
        {
            _cts?.Cancel();
            _cts?.Dispose();
        };

        if (precomputed is not null && !precomputed.IsEmpty)
        {
            ApplyResult(precomputed);
        }
        else
        {
            Loaded += async (_, _) => await RunOcrAsync();
        }
    }

    private async Task RunOcrAsync()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        StatusText.Text = "正在离线识别文字…";
        ResultTextBox.IsReadOnly = true;

        try
        {
            var result = await _ocrService.RecognizeAsync(_frame, token);
            if (!token.IsCancellationRequested)
            {
                ApplyResult(result);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusText.Text = $"识别出错：{ex.Message}";
        }
        finally
        {
            ResultTextBox.IsReadOnly = false;
        }
    }

    private void ApplyResult(OcrResult result)
    {
        if (result.Barcodes.Count > 0)
        {
            BarcodePanel.Visibility = Visibility.Visible;
            BarcodeList.Children.Clear();
            var barcodeTitle = new TextBlock
            {
                Text = $"检测到 {result.Barcodes.Count} 个二维码 / 条码：",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8),
            };
            barcodeTitle.SetResourceReference(TextBlock.ForegroundProperty, "Pp.Accent");
            BarcodeList.Children.Add(barcodeTitle);

            foreach (var bc in result.Barcodes)
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), LastChildFill = true };
                var copyBtn = new Button
                {
                    Content = "复制",
                    MinHeight = 28,
                    Padding = new Thickness(12, 0, 12, 0),
                    Margin = new Thickness(6, 0, 0, 0),
                };
                copyBtn.Click += (_, _) =>
                {
                    Clipboard.SetText(bc.Content);
                    ToastWindow.Show("已复制码内容", ToastKind.Success, _displays);
                };
                DockPanel.SetDock(copyBtn, Dock.Right);
                row.Children.Add(copyBtn);

                if (Uri.TryCreate(bc.Content, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                {
                    var openBtn = new Button
                    {
                        Content = "打开链接",
                        MinHeight = 28,
                        Padding = new Thickness(12, 0, 12, 0),
                        Margin = new Thickness(6, 0, 0, 0),
                        Style = (Style)FindResource("Pp.Button.Primary"),
                    };
                    openBtn.Click += (_, _) => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                    DockPanel.SetDock(openBtn, Dock.Right);
                    row.Children.Add(openBtn);
                }

                row.Children.Add(new TextBlock
                {
                    Text = $"[{bc.RawFormat}] {bc.Content}",
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12.5,
                    ToolTip = bc.Content,
                });

                BarcodeList.Children.Add(row);
            }
        }
        else
        {
            BarcodePanel.Visibility = Visibility.Collapsed;
        }

        ResultTextBox.Text = result.FullText;
        StatusText.Text = result.IsEmpty
            ? "未检测到文字或二维码"
            : $"{result.EngineName} · {result.ElapsedMs} ms";
        UpdateStats();
    }

    private void OnResultTextChanged(object sender, TextChangedEventArgs e) => UpdateStats();

    private void UpdateStats()
    {
        string t = ResultTextBox.Text ?? string.Empty;
        int chars = t.Count(c => !char.IsWhiteSpace(c));
        int lines = string.IsNullOrEmpty(t) ? 0 : t.Split('\n').Length;
        StatsText.Text = $"{chars} 字 · {lines} 行";
    }

    private void OnCopyAllClick(object sender, RoutedEventArgs e)
    {
        string text = ResultTextBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(text)) return;
        Clipboard.SetText(text);
        ToastWindow.Show($"已复制 {text.Length} 个字符到剪贴板", ToastKind.Success, _displays);
    }

    private void OnTranslateClick(object sender, RoutedEventArgs e)
    {
        string text = ResultTextBox.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return;
        _onSendToTranslate?.Invoke(text);
    }
}

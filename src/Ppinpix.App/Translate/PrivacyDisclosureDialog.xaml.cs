using System.Windows;
using Ppinpix.Core.Settings;

namespace Ppinpix.App.Translate;

public partial class PrivacyDisclosureDialog : Window
{
    public PrivacyDisclosureDialog(LlmProfile profile)
    {
        InitializeComponent();

        string modeText = profile.InputMode == TranslationInputMode.ImageDirectly && profile.SupportsVision
            ? "当前配置为「直接发送图片」模式：本次将上传您框选区域的裁剪图片。"
            : "当前配置为「先本地 OCR 后翻译」模式：图像仅在本地识别文字，仅向接口发送纯文本。";

        EndpointSummary.Text = $"目标配置：{profile.Name} · 模型：{profile.Model}\n接口地址：{profile.BaseUrl}";
        PayloadScope.Text = modeText;
    }

    public bool RememberChoice => DontShowAgainCheck.IsChecked == true;

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

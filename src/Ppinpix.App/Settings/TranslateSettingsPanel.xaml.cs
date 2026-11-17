using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Ppinpix.Core.Security;
using Ppinpix.Core.Settings;
using Ppinpix.LlmGateway;
using Ppinpix.Translate;

namespace Ppinpix.App.Settings;

public partial class TranslateSettingsPanel : UserControl
{
    private AppSettings? _settings;
    private Action? _onDirty;
    private LlmProfile? _selected;
    private bool _loadingFields;
    private DispatcherTimer? _revealTimer;
    private bool _keyRevealed;

    public TranslateSettingsPanel()
    {
        InitializeComponent();
        Unloaded += (_, _) => HideRevealedKey();
    }

    public void Bind(AppSettings settings, Action onDirty)
    {
        _settings = settings;
        _onDirty = onDirty;
        RefreshProfileList(settings.Translate.ActiveProfileId);
        SelectTag(PresentationCombo, settings.Translate.Presentation.ToString());
    }

    private void RefreshProfileList(string? selectId = null)
    {
        if (_settings is null) return;
        _loadingFields = true;
        ProfileListBox.Items.Clear();

        foreach (var p in _settings.Translate.Profiles)
        {
            bool isDefault = p.Id == _settings.Translate.ActiveProfileId;
            ProfileListBox.Items.Add(new ListBoxItem
            {
                Content = isDefault ? $"★ {p.Name}" : p.Name,
                Tag = p.Id,
            });
        }

        string targetId = selectId ?? _settings.Translate.ActiveProfileId;
        for (int i = 0; i < ProfileListBox.Items.Count; i++)
        {
            if (((ListBoxItem)ProfileListBox.Items[i]).Tag as string == targetId)
            {
                ProfileListBox.SelectedIndex = i;
                break;
            }
        }
        if (ProfileListBox.SelectedIndex < 0 && ProfileListBox.Items.Count > 0)
        {
            ProfileListBox.SelectedIndex = 0;
        }
        _loadingFields = false;

        LoadSelectedProfileIntoEditor();
    }

    private void OnProfileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingFields) return;
        HideRevealedKey();
        LoadSelectedProfileIntoEditor();
    }

    private void LoadSelectedProfileIntoEditor()
    {
        if (_settings is null) return;
        string? id = (ProfileListBox.SelectedItem as ListBoxItem)?.Tag as string;
        _selected = _settings.Translate.Profiles.FirstOrDefault(p => p.Id == id)
            ?? _settings.Translate.Profiles.FirstOrDefault();
        if (_selected is null) return;

        _loadingFields = true;
        NameBox.Text = _selected.Name;
        SelectTag(ProtocolCombo, _selected.Protocol.ToString());
        BaseUrlBox.Text = _selected.BaseUrl;
        AutoAppendCheck.IsChecked = _selected.AutoAppendPath;
        ModelCombo.Text = _selected.Model;

        string decryptedKey = string.Empty;
        try
        {
            decryptedKey = SecretStore.Unprotect(_selected.EncryptedApiKey);
        }
        catch
        {
        }
        ApiKeyPwdBox.Password = decryptedKey;
        ApiKeyPlainBox.Text = string.Empty;

        SelectTag(InputModeCombo, _selected.InputMode.ToString());
        SupportsVisionCheck.IsChecked = _selected.SupportsVision;
        SelectTag(ProxyModeCombo, _selected.Proxy.Mode.ToString());
        ProxyHostBox.Text = _selected.Proxy.Host;
        ProxyPortBox.Text = _selected.Proxy.Port > 0 ? _selected.Proxy.Port.ToString(CultureInfo.InvariantCulture) : string.Empty;
        ProxyUserBox.Text = _selected.Proxy.UserName;
        try
        {
            ProxyPwdBox.Password = SecretStore.Unprotect(_selected.Proxy.EncryptedPassword);
        }
        catch
        {
            ProxyPwdBox.Password = string.Empty;
        }

        TempBox.Text = _selected.Temperature.ToString("0.##", CultureInfo.InvariantCulture);
        TimeoutBox.Text = _selected.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        StreamCheck.IsChecked = _selected.Stream;
        PromptBox.Text = _selected.Prompt;

        SetDefaultBtn.IsEnabled = _selected.Id != _settings.Translate.ActiveProfileId;
        TestStatusText.Text = string.Empty;
        _loadingFields = false;
    }

    private void OnFieldChanged(object? sender, EventArgs e)
    {
        if (_loadingFields || _selected is null || _settings is null) return;

        _selected.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "未命名配置" : NameBox.Text.Trim();
        if (Enum.TryParse<LlmProtocol>((ProtocolCombo.SelectedItem as ComboBoxItem)?.Tag as string, out var proto))
            _selected.Protocol = proto;
        _selected.BaseUrl = BaseUrlBox.Text.Trim();
        _selected.AutoAppendPath = AutoAppendCheck.IsChecked == true;
        _selected.Model = ModelCombo.Text.Trim();
        if (Enum.TryParse<TranslationInputMode>((InputModeCombo.SelectedItem as ComboBoxItem)?.Tag as string, out var mode))
            _selected.InputMode = mode;
        _selected.SupportsVision = SupportsVisionCheck.IsChecked == true;

        if (Enum.TryParse<ProxyMode>((ProxyModeCombo.SelectedItem as ComboBoxItem)?.Tag as string, out var pMode))
            _selected.Proxy.Mode = pMode;
        _selected.Proxy.Host = ProxyHostBox.Text.Trim();
        if (int.TryParse(ProxyPortBox.Text.Trim(), out int port) && port >= 0 && port <= 65535)
            _selected.Proxy.Port = port;
        _selected.Proxy.UserName = ProxyUserBox.Text.Trim();

        if (double.TryParse(TempBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double temp))
            _selected.Temperature = Math.Clamp(temp, 0, 2);
        if (int.TryParse(TimeoutBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int to))
            _selected.TimeoutSeconds = Math.Clamp(to, 5, 300);
        _selected.Stream = StreamCheck.IsChecked == true;
        _selected.Prompt = PromptBox.Text ?? string.Empty;

        // 同步更新左侧显示名
        if (ProfileListBox.SelectedItem is ListBoxItem li)
        {
            li.Content = _selected.Id == _settings.Translate.ActiveProfileId ? $"★ {_selected.Name}" : _selected.Name;
        }

        _onDirty?.Invoke();
    }

    private void OnGlobalTranslateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingFields || _settings is null) return;
        if (Enum.TryParse<TranslationPresentation>((PresentationCombo.SelectedItem as ComboBoxItem)?.Tag as string, out var pres))
        {
            _settings.Translate.Presentation = pres;
            _onDirty?.Invoke();
        }
    }

    private void OnApiKeyChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingFields || _selected is null) return;
        _selected.EncryptedApiKey = SecretStore.Protect(ApiKeyPwdBox.Password);
        _onDirty?.Invoke();
    }

    private void OnProxyPwdChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingFields || _selected is null) return;
        _selected.Proxy.EncryptedPassword = SecretStore.Protect(ProxyPwdBox.Password);
        _onDirty?.Invoke();
    }

    private void OnToggleRevealKeyClick(object sender, RoutedEventArgs e)
    {
        if (_keyRevealed)
        {
            HideRevealedKey();
            return;
        }

        var confirm = MessageBox.Show(
            "确定要明文显示当前 API 密钥吗？（5 秒后将自动重新遮蔽）",
            "安全确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        _keyRevealed = true;
        ApiKeyPlainBox.Text = ApiKeyPwdBox.Password;
        ApiKeyPlainBox.Visibility = Visibility.Visible;
        ApiKeyPwdBox.Visibility = Visibility.Collapsed;
        RevealKeyBtn.Content = "隐藏 (5s)";

        _revealTimer?.Stop();
        _revealTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _revealTimer.Tick += (_, _) => HideRevealedKey();
        _revealTimer.Start();
    }

    private void HideRevealedKey()
    {
        _revealTimer?.Stop();
        _revealTimer = null;
        if (!_keyRevealed) return;
        _keyRevealed = false;
        ApiKeyPlainBox.Text = string.Empty;
        ApiKeyPlainBox.Visibility = Visibility.Collapsed;
        ApiKeyPwdBox.Visibility = Visibility.Visible;
        RevealKeyBtn.Content = "显示密钥";
    }

    private async void OnTestConnectionClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        TestConnBtn.IsEnabled = false;
        TestStatusText.Foreground = Ppinpix.Theme.ThemeLoader.Brush("Pp.Accent");
        TestStatusText.Text = "正在发起实时流式连通性测试…";

        try
        {
            var tester = new ConnectionTester();
            var res = await tester.TestAsync(_selected);
            TestStatusText.Foreground = Ppinpix.Theme.ThemeLoader.Brush(res.Success ? "Pp.Success" : "Pp.Danger");
            TestStatusText.Text = res.Message;
        }
        finally
        {
            TestConnBtn.IsEnabled = true;
        }
    }

    private async void OnFetchModelsClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        FetchModelsBtn.IsEnabled = false;
        TestStatusText.Foreground = Ppinpix.Theme.ThemeLoader.Brush("Pp.Accent");
        TestStatusText.Text = "正在拉取模型列表…";

        try
        {
            var service = new TranslateService();
            var models = await service.FetchModelsAsync(UserInitiatedToken.IssueFromUserAction(), _selected);
            string current = ModelCombo.Text;
            ModelCombo.Items.Clear();
            foreach (string m in models)
            {
                ModelCombo.Items.Add(m);
            }
            ModelCombo.Text = string.IsNullOrEmpty(current) && models.Count > 0 ? models[0] : current;
            TestStatusText.Foreground = Ppinpix.Theme.ThemeLoader.Brush("Pp.Success");
            TestStatusText.Text = $"已拉取 {models.Count} 个可用模型。";
        }
        catch (Exception ex)
        {
            TestStatusText.Foreground = Ppinpix.Theme.ThemeLoader.Brush("Pp.Danger");
            TestStatusText.Text = $"拉取失败：{ex.Message}";
        }
        finally
        {
            FetchModelsBtn.IsEnabled = true;
        }
    }

    private void OnAddProfileClick(object sender, RoutedEventArgs e)
    {
        if (_settings is null) return;
        var p = new LlmProfile { Name = $"自定义配置 {_settings.Translate.Profiles.Count + 1}" };
        _settings.Translate.Profiles.Add(p);
        RefreshProfileList(p.Id);
        _onDirty?.Invoke();
    }

    private void OnCloneProfileClick(object sender, RoutedEventArgs e)
    {
        if (_settings is null || _selected is null) return;
        string json = JsonSerializer.Serialize(_selected, SettingsStore.JsonOptions);
        var copy = JsonSerializer.Deserialize<LlmProfile>(json, SettingsStore.JsonOptions)!;
        copy.Id = Guid.NewGuid().ToString("n");
        copy.Name = _selected.Name + " (副本)";
        _settings.Translate.Profiles.Add(copy);
        RefreshProfileList(copy.Id);
        _onDirty?.Invoke();
    }

    private void OnDeleteProfileClick(object sender, RoutedEventArgs e)
    {
        if (_settings is null || _selected is null) return;
        if (_settings.Translate.Profiles.Count <= 1)
        {
            MessageBox.Show("至少保留一套接口配置。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        string removedId = _selected.Id;
        _settings.Translate.Profiles.Remove(_selected);
        if (_settings.Translate.ActiveProfileId == removedId)
        {
            _settings.Translate.ActiveProfileId = _settings.Translate.Profiles[0].Id;
        }
        RefreshProfileList(_settings.Translate.ActiveProfileId);
        _onDirty?.Invoke();
    }

    private void OnSetDefaultClick(object sender, RoutedEventArgs e)
    {
        if (_settings is null || _selected is null) return;
        _settings.Translate.ActiveProfileId = _selected.Id;
        RefreshProfileList(_selected.Id);
        _onDirty?.Invoke();
    }

    private void OnExportProfilesClick(object sender, RoutedEventArgs e)
    {
        if (_settings is null) return;
        var dlg = new SaveFileDialog
        {
            Title = "导出翻译配置（自动剥离 API 密钥与代理密码）",
            Filter = "JSON 配置文件 (*.json)|*.json",
            FileName = "ppinpix-translate-profiles.json",
        };
        if (dlg.ShowDialog() != true) return;

        var sanitized = _settings.Translate.Profiles.Select(p =>
        {
            string raw = JsonSerializer.Serialize(p, SettingsStore.JsonOptions);
            var clone = JsonSerializer.Deserialize<LlmProfile>(raw, SettingsStore.JsonOptions)!;
            clone.EncryptedApiKey = string.Empty;
            clone.Proxy.EncryptedPassword = string.Empty;
            return clone;
        }).ToList();

        File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(sanitized, SettingsStore.JsonOptions));
        MessageBox.Show("配置已安全导出（不包含任何 API Key 和代理密码）。", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnImportProfilesClick(object sender, RoutedEventArgs e)
    {
        if (_settings is null) return;
        var dlg = new OpenFileDialog
        {
            Title = "导入翻译配置 JSON",
            Filter = "JSON 配置文件 (*.json)|*.json",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            string json = File.ReadAllText(dlg.FileName);
            var imported = JsonSerializer.Deserialize<List<LlmProfile>>(json, SettingsStore.JsonOptions);
            if (imported is null || imported.Count == 0)
            {
                MessageBox.Show("文件中没有有效的配置项。", "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            foreach (var p in imported)
            {
                p.Id = Guid.NewGuid().ToString("n");
                // 防护：导入不信任未加密伪造密文，统一清空待用户在本机重新填 Key
                p.EncryptedApiKey = string.Empty;
                p.Proxy.EncryptedPassword = string.Empty;
                _settings.Translate.Profiles.Add(p);
            }
            RefreshProfileList(imported[0].Id);
            _onDirty?.Invoke();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导入配置失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void SelectTag(ComboBox combo, string tag)
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

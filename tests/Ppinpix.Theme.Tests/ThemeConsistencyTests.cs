using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using Ppinpix.Theme;
using Xunit;

namespace Ppinpix.Theme.Tests;

public sealed class ThemeConsistencyTests
{
    private static void RunSta(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(Application).TypeHandle);
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (captured is not null)
        {
            throw new InvalidOperationException("STA test execution failed.", captured);
        }
    }

    [Fact]
    public void ThemeXaml_BrushesShouldMatchTokensExactly()
    {
        RunSta(() =>
        {
            var dict = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Ppinpix.Theme;component/Theme.xaml", UriKind.Absolute),
            };

            AssertBrushColor(dict, "Pp.Accent", Tokens.Accent);
            AssertBrushColor(dict, "Pp.AccentHover", Tokens.AccentHover);
            AssertBrushColor(dict, "Pp.AccentPressed", Tokens.AccentPressed);
            AssertBrushColor(dict, "Pp.OnAccent", Tokens.OnAccent);
            AssertBrushColor(dict, "Pp.AccentSoft", Tokens.AccentSoft);

            AssertBrushColor(dict, "Pp.Surface", Tokens.SurfaceOpaque);
            AssertBrushColor(dict, "Pp.SurfaceRaised", Tokens.SurfaceRaised);
            AssertBrushColor(dict, "Pp.SurfaceHover", Tokens.SurfaceHover);
            AssertBrushColor(dict, "Pp.SurfacePressed", Tokens.SurfacePressed);
            AssertBrushColor(dict, "Pp.Stroke", Tokens.Stroke);
            AssertBrushColor(dict, "Pp.GridLine", Tokens.GridLine);
            AssertBrushColor(dict, "Pp.Dim", Tokens.Dim);

            AssertBrushColor(dict, "Pp.TextPrimary", Tokens.TextPrimary);
            AssertBrushColor(dict, "Pp.TextSecondary", Tokens.TextSecondary);
            AssertBrushColor(dict, "Pp.TextDisabled", Tokens.TextDisabled);

            AssertBrushColor(dict, "Pp.Success", Tokens.Success);
            AssertBrushColor(dict, "Pp.Warning", Tokens.Warning);
            AssertBrushColor(dict, "Pp.Danger", Tokens.Danger);

            AssertBrushColor(dict, "Pp.Light.WindowBg", Tokens.Light.WindowBg);
            AssertBrushColor(dict, "Pp.Light.CardBg", Tokens.Light.CardBg);
            AssertBrushColor(dict, "Pp.Light.CardStroke", Tokens.Light.CardStroke);
            AssertBrushColor(dict, "Pp.Light.NavSelectedBg", Tokens.Light.NavSelectedBg);
            AssertBrushColor(dict, "Pp.Light.TextPrimary", Tokens.Light.TextPrimary);
            AssertBrushColor(dict, "Pp.Light.TextSecondary", Tokens.Light.TextSecondary);

            AssertBrushColor(dict, "Pp.Dark.WindowBg", Tokens.Dark.WindowBg);
            AssertBrushColor(dict, "Pp.Dark.CardBg", Tokens.Dark.CardBg);
            AssertBrushColor(dict, "Pp.Dark.CardStroke", Tokens.Dark.CardStroke);
            AssertBrushColor(dict, "Pp.Dark.NavSelectedBg", Tokens.Dark.NavSelectedBg);
            AssertBrushColor(dict, "Pp.Dark.TextPrimary", Tokens.Dark.TextPrimary);
            AssertBrushColor(dict, "Pp.Dark.TextSecondary", Tokens.Dark.TextSecondary);
        });
    }

    [Fact]
    public void ApplySettingsTheme_ShouldPopulateBothLightAndDarkKeys()
    {
        RunSta(() =>
        {
            var dict = new ResourceDictionary();

            ThemeLoader.ApplySettingsTheme(dict, isDark: false);
            AssertBrushColor(dict, "Pp.Settings.WindowBg", Tokens.Light.WindowBg);
            AssertBrushColor(dict, "Pp.Settings.Card", Tokens.Light.CardBg);
            AssertBrushColor(dict, "Pp.Settings.Text", Tokens.Light.TextPrimary);

            ThemeLoader.ApplySettingsTheme(dict, isDark: true);
            AssertBrushColor(dict, "Pp.Settings.WindowBg", Tokens.Dark.WindowBg);
            AssertBrushColor(dict, "Pp.Settings.Card", Tokens.Dark.CardBg);
            AssertBrushColor(dict, "Pp.Settings.Text", Tokens.Dark.TextPrimary);
        });
    }

    [Fact]
    public void Icons_AllGlyphConstantsShouldBePopulated()
    {
        var fields = typeof(Icons).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string))
            .ToList();

        Assert.NotEmpty(fields);
        foreach (var field in fields)
        {
            string? val = field.GetValue(null) as string;
            Assert.False(string.IsNullOrEmpty(val), $"Icons.{field.Name} should not be empty.");
        }
    }

    [Fact]
    public void ThemeResources_AllReferencedPpKeysInSourceShouldResolve()
    {
        RunSta(() =>
        {
            var merged = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Ppinpix.Theme;component/Theme.xaml", UriKind.Absolute),
            };
            ThemeLoader.ApplySettingsTheme(merged, isDark: true);
            var controls = new ResourceDictionary { Source = ThemeLoader.ControlsUri };

            string? root = FindRepoRoot();
            Assert.False(string.IsNullOrWhiteSpace(root), "Could not locate Ppinpix.sln repository root.");

            string srcDir = Path.Combine(root!, "src");
            var pattern = new Regex(
                @"(?:ThemeLoader\.(?:Find<[^>]+>|Brush)|(?:Try)?FindResource)\(""([^""]+)""\)|\{(?:Static|Dynamic)Resource\s+([^\}\s]+)",
                RegexOptions.Compiled);

            var missing = new List<string>();
            foreach (string file in Directory.EnumerateFiles(srcDir, "*.*", SearchOption.AllDirectories))
            {
                if ((!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                     !file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)) ||
                    file.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase) ||
                    file.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string content = File.ReadAllText(file);
                foreach (Match match in pattern.Matches(content))
                {
                    string key = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                    if (key.StartsWith("Pp.", StringComparison.Ordinal) && !merged.Contains(key) && !controls.Contains(key))
                    {
                        missing.Add($"{key} ({Path.GetRelativePath(root!, file)})");
                    }
                }
            }

            Assert.True(missing.Count == 0, "发现未在 Theme.xaml / Controls.xaml 或 ApplySettingsTheme 中定义的主题键：" + string.Join(", ", missing));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ControlsXaml_AllControlStylesShouldInstantiateAndApplyTemplates(bool isDark)
    {
        RunSta(() =>
        {
            var host = new System.Windows.Controls.StackPanel();
            host.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Ppinpix.Theme;component/Theme.xaml", UriKind.Absolute),
            });
            host.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = ThemeLoader.ControlsUri });
            ThemeLoader.ApplySettingsTheme(host.Resources, isDark);

            var combo = new System.Windows.Controls.ComboBox();
            combo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "A" });
            combo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "B" });
            combo.SelectedIndex = 0;
            var editableCombo = new System.Windows.Controls.ComboBox { IsEditable = true, Text = "gpt" };
            var list = new System.Windows.Controls.ListBox();
            list.Items.Add(new System.Windows.Controls.ListBoxItem { Content = "profile" });
            var expander = new System.Windows.Controls.Expander { Header = "高级", Content = new System.Windows.Controls.TextBlock { Text = "body" }, IsExpanded = true };

            System.Windows.Controls.Control[] controls =
            [
                new System.Windows.Controls.Button { Content = "次要" },
                new System.Windows.Controls.Button { Content = "主要", Style = (Style)host.FindResource("Pp.Button.Primary") },
                new System.Windows.Controls.Button { Content = "危险", Style = (Style)host.FindResource("Pp.Button.Danger") },
                new System.Windows.Controls.Button { Content = "幽灵", Style = (Style)host.FindResource("Pp.Button.Ghost") },
                new System.Windows.Controls.CheckBox { Content = "开关", IsChecked = true },
                new System.Windows.Controls.CheckBox { Content = "胶囊", IsChecked = true, Style = (Style)host.FindResource("Pp.Chip") },
                new System.Windows.Controls.TextBox { Text = "text", AcceptsReturn = true },
                new System.Windows.Controls.PasswordBox(),
                combo,
                editableCombo,
                new System.Windows.Controls.Slider { Minimum = 0, Maximum = 10, Value = 4, Width = 160 },
                list,
                expander,
            ];

            foreach (var control in controls)
            {
                host.Children.Add(control);
            }

            host.Measure(new Size(480, 900));
            host.Arrange(new Rect(0, 0, 480, 900));
            host.UpdateLayout();

            foreach (var control in controls)
            {
                Assert.True(control.IsMeasureValid, $"{control.GetType().Name} 未能完成布局。");
                Assert.True(
                    System.Windows.Media.VisualTreeHelper.GetChildrenCount(control) > 0,
                    $"{control.GetType().Name} 的自定义模板没有生成可视树。");
                Assert.True(control.DesiredSize.Height >= 20, $"{control.GetType().Name} 高度异常：{control.DesiredSize.Height}");
            }
        });
    }

    [Fact]
    public void ApplySettingsTheme_ShouldPopulateControlDerivedKeys()
    {
        RunSta(() =>
        {
            var dict = new ResourceDictionary();
            foreach (bool isDark in new[] { false, true })
            {
                ThemeLoader.ApplySettingsTheme(dict, isDark);
                foreach (string key in new[]
                {
                    "Pp.Settings.InputBorder", "Pp.Settings.PressedBg", "Pp.Settings.TextDisabled",
                    "Pp.Settings.ControlBg", "Pp.Settings.HoverBg",
                })
                {
                    Assert.True(dict.Contains(key), $"Missing settings key '{key}' (dark={isDark}).");
                    Assert.IsType<SolidColorBrush>(dict[key]);
                }
            }
        });
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Ppinpix.sln")))
            {
                return dir.FullName;
            }
        }
        return null;
    }

    private static void AssertBrushColor(ResourceDictionary dict, string key, Color expected)
    {
        Assert.True(dict.Contains(key), $"Missing theme key '{key}'.");
        var brush = Assert.IsType<SolidColorBrush>(dict[key]);
        Assert.Equal(expected, brush.Color);
    }
}

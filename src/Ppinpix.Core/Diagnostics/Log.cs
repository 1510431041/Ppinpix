using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Ppinpix.Core;

/// <summary>
/// 极简本地日志。写入 %APPDATA%\Ppinpix\logs\ppinpix-yyyyMMdd.log，保留 7 天。
/// 严禁写入 API Key、Authorization 头、识别到的文字内容。
/// </summary>
public static partial class Log
{
    private static readonly Lock Gate = new();
    public static readonly string LogDirectory = Path.Combine(Paths.AppDataRoot, "logs");
    private const int RetainDays = 7;

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        string line = ex is null
            ? $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {Redact(message)}"
            : $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {Redact(message)}{Environment.NewLine}{ex}";

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                string file = Path.Combine(LogDirectory, $"ppinpix-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
                Cleanup();
            }
        }
        catch
        {
            // 日志失败不能影响主流程。
        }

        Debug.WriteLine(line);
    }

    private static void Cleanup()
    {
        var cutoff = DateTime.Now.AddDays(-RetainDays);
        foreach (string file in Directory.EnumerateFiles(LogDirectory, "ppinpix-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
            }
            catch
            {
                // 忽略：文件可能正被其他进程占用。
            }
        }
    }

    /// <summary>脱敏：把疑似密钥、图片 Base64 数据替换掉。所有对外输出（日志、错误提示）都必须先过这里。</summary>
    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        string result = ImageBase64Pattern().Replace(text, static m => $"<image data {m.Groups[1].Value.Length} bytes>");
        result = AuthHeaderPattern().Replace(result, "$1***");
        result = BearerPattern().Replace(result, "$1***");
        result = SecretPattern().Replace(result, "$1***");
        return result;
    }

    /// <summary>data:image/...;base64,... 长度超过 100 字符的图像数据</summary>
    [GeneratedRegex("""data:image\/[a-zA-Z0-9+.\-]+;base64,([A-Za-z0-9+/=\s]{100,})""")]
    private static partial Regex ImageBase64Pattern();

    /// <summary>Authorization: Bearer &lt;token&gt; 或 Authorization: &lt;scheme&gt; &lt;token&gt;</summary>
    [GeneratedRegex("""(?i)(Authorization\s*[:=]\s*(?:Bearer|Basic|Token)\s+)(\S{4,})""")]
    private static partial Regex AuthHeaderPattern();

    /// <summary>Bearer &lt;token&gt;</summary>
    [GeneratedRegex("""(?i)(Bearer\s+)([A-Za-z0-9._\-]{8,})""")]
    private static partial Regex BearerPattern();

    /// <summary>api_key / api-key / apikey / key / token / password / secret 后面跟的取值（跳过 Bearer 等前缀词）。</summary>
    [GeneratedRegex("""(?i)("?(?:x-api-key|api[-_]?key|apikey|key|token|password|secret)"?\s*[=:]\s*"?)(?!Bearer\b)([^\s"&,}]{4,})""")]
    private static partial Regex SecretPattern();
}

/// <summary>应用的本地目录约定。</summary>
public static class Paths
{
    /// <summary>%APPDATA%\Ppinpix</summary>
    public static string AppDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ppinpix");

    public static string SettingsFile => Path.Combine(AppDataRoot, "settings.json");

    public static string PinsDirectory => Path.Combine(AppDataRoot, "pins");

    public static string HistoryDirectory => Path.Combine(AppDataRoot, "history");

    public static string ModelsDirectory => Path.Combine(AppContext.BaseDirectory, "models");

    public static string UserModelsDirectory => Path.Combine(AppDataRoot, "models");

    public static string ScreenshotsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Ppinpix");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(PinsDirectory);
    }
}

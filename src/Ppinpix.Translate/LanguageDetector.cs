namespace Ppinpix.Translate;

/// <summary>语种显示信息。</summary>
public sealed record LanguageOption(string Code, string DisplayName, string PromptName);

/// <summary>启发式语种判断（区分汉字 / 假名 / 谚文 / 拉丁字母，自动决定中译英或外译中）。</summary>
public static class LanguageDetector
{
    public static readonly IReadOnlyList<LanguageOption> SupportedLanguages =
    [
        new("auto", "自动判断", "auto"),
        new("zh-Hans", "简体中文", "Simplified Chinese"),
        new("zh-Hant", "繁体中文", "Traditional Chinese"),
        new("en", "English", "English"),
        new("ja", "日本語", "Japanese"),
        new("ko", "한국어", "Korean"),
        new("fr", "Français", "French"),
        new("de", "Deutsch", "German"),
        new("es", "Español", "Spanish"),
        new("ru", "Русский", "Russian"),
    ];

    public static string DetectPrimaryLanguageCode(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "en";

        int han = 0, kana = 0, hangul = 0, latin = 0;
        foreach (char c in text)
        {
            if (c is >= '\u4E00' and <= '\u9FFF' || c is >= '\u3400' and <= '\u4DBF') han++;
            else if (c is >= '\u3040' and <= '\u30FF') kana++;
            else if (c is >= '\uAC00' and <= '\uD7AF' || c is >= '\u1100' and <= '\u11FF') hangul++;
            else if (c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z')) latin++;
        }

        // 日语混用汉字与假名：只要存在明显假名即判日语而非中文
        if (kana > 0 && kana * 3 >= han) return "ja";
        if (hangul > 0 && hangul >= han) return "ko";
        if (han > 0 && han >= latin / 3) return "zh-Hans";
        return "en";
    }

    public static (string SourceCode, string TargetCode, string SourcePromptName, string TargetPromptName) Resolve(
        string text,
        string configuredSource,
        string configuredTarget)
    {
        string detected = DetectPrimaryLanguageCode(text);
        string srcCode = string.IsNullOrWhiteSpace(configuredSource) || configuredSource == "auto"
            ? detected
            : configuredSource;

        string tgtCode;
        if (string.IsNullOrWhiteSpace(configuredTarget) || configuredTarget == "auto")
        {
            tgtCode = srcCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "en" : "zh-Hans";
        }
        else
        {
            tgtCode = configuredTarget;
        }

        return (srcCode, tgtCode, GetPromptName(srcCode), GetPromptName(tgtCode));
    }

    public static string GetPromptName(string code) =>
        SupportedLanguages.FirstOrDefault(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase))?.PromptName
        ?? code;

    public static string GetDisplayName(string code) =>
        SupportedLanguages.FirstOrDefault(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase))?.DisplayName
        ?? code;
}

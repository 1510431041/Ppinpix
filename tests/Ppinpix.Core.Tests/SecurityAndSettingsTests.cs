using Ppinpix.Core.Security;
using Ppinpix.Core.Settings;
using Xunit;

namespace Ppinpix.Core.Tests;

public sealed class SecurityAndSettingsTests : IDisposable
{
    private readonly string _tempDir;

    public SecurityAndSettingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PpinpixTests_" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void SecretStore_ProtectAndUnprotect_ShouldRoundtripWithoutPlaintext()
    {
        const string secret = "sk-test-secret-1234567890-abcdef";
        string cipher = SecretStore.Protect(secret);

        Assert.StartsWith("dpapi:", cipher);
        Assert.DoesNotContain(secret, cipher);
        Assert.True(SecretStore.IsEncrypted(cipher));
        Assert.Equal(secret, SecretStore.Unprotect(cipher));
        Assert.Equal(string.Empty, SecretStore.Protect(string.Empty));
        Assert.Equal(string.Empty, SecretStore.Unprotect(string.Empty));
    }

    [Fact]
    public void LogRedact_ShouldStripKeysAuthAndBase64Images()
    {
        string raw = "POST https://example.com Authorization: Bearer sk-verysecret123456 " +
                     "\"x-api-key\": \"secret-key-9999\" data:image/png;base64," + new string('A', 150);

        string redacted = Log.Redact(raw);

        Assert.DoesNotContain("sk-verysecret123456", redacted);
        Assert.DoesNotContain("secret-key-9999", redacted);
        Assert.DoesNotContain(new string('A', 100), redacted);
        Assert.Contains("<image data 150 bytes>", redacted);
    }

    [Fact]
    public void SettingsStore_SaveLoadNormalize_AndBackupCorrupted()
    {
        string file = Path.Combine(_tempDir, "settings.json");
        var store = new SettingsStore(file);

        var s = new AppSettings();
        s.Capture.SnapThresholdDip = 999; // 超出上限，加载时应被 Normalize 裁剪
        s.Save.JpegQuality = 1;           // 低于下限 10
        store.Save(s);

        var loaded = store.Load();
        Assert.Equal(40, loaded.Capture.SnapThresholdDip);
        Assert.Equal(10, loaded.Save.JpegQuality);

        File.WriteAllText(file, "{ corrupted json !!!");
        var fallback = store.Load();
        Assert.NotNull(fallback);
        Assert.True(File.Exists(file + ".corrupted"));
    }

    [Fact]
    public void TranslateSettings_ShouldDefaultToOverlay_MigrateV1Compare_AndPreserveV2Compare()
    {
        var defaults = new AppSettings();
        Assert.Equal(TranslationPresentation.Overlay, defaults.Translate.Presentation);
        Assert.True(defaults.SchemaVersion >= 2);

        string file = Path.Combine(_tempDir, "migrate.json");
        var store = new SettingsStore(file);

        // Legacy v1 file created before Overlay became default
        File.WriteAllText(file, """
        {
          "SchemaVersion": 1,
          "Translate": {
            "Presentation": "Compare"
          }
        }
        """);
        var migrated = store.Load();
        Assert.Equal(2, migrated.SchemaVersion);
        Assert.Equal(TranslationPresentation.Overlay, migrated.Translate.Presentation);

        // User explicitly chooses Compare under v2 schema
        migrated.Translate.Presentation = TranslationPresentation.Compare;
        store.Save(migrated);

        var reloaded = store.Load();
        Assert.Equal(2, reloaded.SchemaVersion);
        Assert.Equal(TranslationPresentation.Compare, reloaded.Translate.Presentation);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}

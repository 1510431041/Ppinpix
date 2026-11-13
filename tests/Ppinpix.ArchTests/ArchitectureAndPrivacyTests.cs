using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Ppinpix.Core.Settings;
using Ppinpix.Translate;
using Xunit;

namespace Ppinpix.ArchTests;

/// <summary>
/// 通过 PE Metadata 直接扫描全部核心与业务程序集引用，在每次构建时强制校验架构隔离与隐私边界。
/// </summary>
public sealed class ArchitectureAndPrivacyTests
{
    private static readonly string[] AllProductAssemblies =
    [
        "Ppinpix.Core.dll",
        "Ppinpix.Theme.dll",
        "Ppinpix.Capture.dll",
        "Ppinpix.Ocr.dll",
        "Ppinpix.Pin.dll",
        "Ppinpix.LlmGateway.dll",
        "Ppinpix.Translate.dll",
        "Ppinpix.dll",
    ];

    private static readonly string[] ForbiddenNetworkAssemblies =
    [
        "System.Net.Http",
        "System.Net.Sockets",
        "System.Net.WebSockets",
        "System.Net.WebSockets.Client",
        "System.Net.Requests",
        "System.Net.WebClient",
        "System.Net.Mail",
        "System.Net.NameResolution",
    ];

    private static readonly string[] ForbiddenNetworkNamespaces =
    [
        "System.Net.Http",
        "System.Net.Sockets",
        "System.Net.WebSockets",
    ];

    [Fact]
    public void OnlyLlmGateway_MayReferenceNetworkingApis()
    {
        foreach (string dll in AllProductAssemblies)
        {
            if (dll.Equals("Ppinpix.LlmGateway.dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var (asmRefs, typeRefs) = ReadReferences(dll);

            var badAsm = asmRefs.Where(a => ForbiddenNetworkAssemblies.Contains(a, StringComparer.OrdinalIgnoreCase)).ToList();
            Assert.True(badAsm.Count == 0,
                $"{dll} 不允许直接引用网络程序集：{string.Join(", ", badAsm)}（所有联网出口必须统一收敛到 Ppinpix.LlmGateway）。");

            var badTypes = typeRefs
                .Where(t => ForbiddenNetworkNamespaces.Any(ns => t.StartsWith(ns + ".", StringComparison.OrdinalIgnoreCase)))
                .ToList();
            Assert.True(badTypes.Count == 0,
                $"{dll} 不允许引用网络类型：{string.Join(", ", badTypes)}。");
        }
    }

    [Fact]
    public void CoreAssembly_ShouldRemainPureWithoutUiOrHigherLayerReferences()
    {
        var (asmRefs, _) = ReadReferences("Ppinpix.Core.dll");
        string[] forbidden =
        [
            "PresentationFramework",
            "PresentationCore",
            "WindowsBase",
            "System.Windows.Forms",
            "Ppinpix.Theme",
            "Ppinpix.Capture",
            "Ppinpix.Ocr",
            "Ppinpix.Pin",
            "Ppinpix.LlmGateway",
            "Ppinpix.Translate",
            "Ppinpix",
        ];

        var hits = asmRefs.Where(a => forbidden.Contains(a, StringComparer.OrdinalIgnoreCase)).ToList();
        Assert.Empty(hits);
    }

    [Fact]
    public void OfflineModules_ShouldNotDependOnLlmOrTranslate()
    {
        foreach (string dll in new[] { "Ppinpix.Theme.dll", "Ppinpix.Capture.dll", "Ppinpix.Ocr.dll", "Ppinpix.Pin.dll" })
        {
            var (asmRefs, _) = ReadReferences(dll);
            Assert.DoesNotContain("Ppinpix.LlmGateway", asmRefs, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("Ppinpix.Translate", asmRefs, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TranslateService_RemoteTranslationMethods_MustRequireUserInitiatedToken()
    {
        var methods = typeof(TranslateService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name.Contains("Translate", StringComparison.Ordinal) && m.Name.Contains("Stream", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(methods);
        foreach (var method in methods)
        {
            bool hasToken = method.GetParameters().Any(p => p.ParameterType == typeof(UserInitiatedToken));
            Assert.True(hasToken, $"TranslateService.{method.Name} 必须要求 UserInitiatedToken 参数以防止后台擅自调用。");
        }
    }

    [Fact]
    public void SettingsModels_MustNotExposePlaintextSecretProperties()
    {
        Assert.Null(typeof(LlmProfile).GetProperty("ApiKey"));
        Assert.NotNull(typeof(LlmProfile).GetProperty(nameof(LlmProfile.EncryptedApiKey)));

        Assert.Null(typeof(ProxySettings).GetProperty("Password"));
        Assert.NotNull(typeof(ProxySettings).GetProperty(nameof(ProxySettings.EncryptedPassword)));
    }

    private static (HashSet<string> AssemblyRefs, HashSet<string> TypeRefs) ReadReferences(string assemblyFileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, assemblyFileName);
        Assert.True(File.Exists(path), $"找不到待检查程序集：{path}");

        using var fs = File.OpenRead(path);
        using var pe = new PEReader(fs);
        MetadataReader reader = pe.GetMetadataReader();

        var asmRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var handle in reader.AssemblyReferences)
        {
            var asmRef = reader.GetAssemblyReference(handle);
            asmRefs.Add(reader.GetString(asmRef.Name));
        }

        var typeRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var handle in reader.TypeReferences)
        {
            var typeRef = reader.GetTypeReference(handle);
            string ns = reader.GetString(typeRef.Namespace);
            string name = reader.GetString(typeRef.Name);
            typeRefs.Add(string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}");
        }

        return (asmRefs, typeRefs);
    }
}

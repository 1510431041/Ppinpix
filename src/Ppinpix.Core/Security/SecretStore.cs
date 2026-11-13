using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Ppinpix.Core.Security;

/// <summary>
/// API Key 的本地加密存储。
/// 使用 Windows DPAPI，作用域为当前用户：密钥与当前 Windows 账户绑定，
/// 配置文件被复制到其他机器或账户下无法解密。
/// 附加 entropy 与密文分开存放，进一步避免“拷走一个文件就能解”的情况。
/// </summary>
public static class SecretStore
{
    private static readonly byte[] Entropy = LoadEntropy();

    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;

        byte[]? plainBytes = null;
        try
        {
            plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] cipher = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
            return "dpapi:" + Convert.ToBase64String(cipher);
        }
        catch (Exception ex)
        {
            Log.Error("加密密钥失败。", ex);
            throw new InvalidOperationException("无法加密密钥：DPAPI 调用失败。", ex);
        }
        finally
        {
            if (plainBytes is not null) Array.Clear(plainBytes);
        }
    }

    public static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return string.Empty;
        if (!stored.StartsWith("dpapi:", StringComparison.Ordinal)) return stored; // 兼容手工填入的明文

        byte[]? plain = null;
        try
        {
            byte[] cipher = Convert.FromBase64String(stored["dpapi:".Length..]);
            plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex)
        {
            Log.Error("解密密钥失败：可能是配置文件来自其他 Windows 账户或机器。", ex);
            return string.Empty;
        }
        finally
        {
            if (plain is not null) Array.Clear(plain);
        }
    }

    public static bool IsEncrypted(string? stored) =>
        !string.IsNullOrEmpty(stored) && stored.StartsWith("dpapi:", StringComparison.Ordinal);

    private static byte[] LoadEntropy()
    {
        string path = Path.Combine(Paths.AppDataRoot, ".entropy");

        try
        {
            if (File.Exists(path))
            {
                byte[] existing = Convert.FromBase64String(File.ReadAllText(path).Trim());
                if (existing.Length >= 16) return existing;
            }

            byte[] fresh = RandomNumberGenerator.GetBytes(32);
            Directory.CreateDirectory(Paths.AppDataRoot);
            File.WriteAllText(path, Convert.ToBase64String(fresh), Encoding.UTF8);

            try
            {
                // 尽量限制为当前用户可读。
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
            }
            catch
            {
                // 非致命。
            }

            return fresh;
        }
        catch (Exception ex)
        {
            Log.Warn("读取或创建 entropy 文件失败，改用进程内随机值（已保存的密钥本次将无法解密）。", ex);
            return RandomNumberGenerator.GetBytes(32);
        }
    }
}

using System.Net;
using System.Net.Http;
using Ppinpix.Core;
using Ppinpix.Core.Security;
using Ppinpix.Core.Settings;

namespace Ppinpix.LlmGateway.Proxy;

/// <summary>
/// 构造符合直连 / 系统代理 / HTTP 代理 / SOCKS5 代理（含认证）的 <see cref="SocketsHttpHandler"/>。
/// 代理密码仅在内存中短暂解密，用后立即清零缓冲区。
/// </summary>
public static class ProxyFactory
{
    public static HttpMessageHandler CreateHandler(ProxySettings settings)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };

        switch (settings.Mode)
        {
            case ProxyMode.None:
                handler.UseProxy = false;
                break;

            case ProxyMode.System:
                handler.UseProxy = true;
                handler.Proxy = WebRequest.GetSystemWebProxy();
                handler.DefaultProxyCredentials = CredentialCache.DefaultCredentials;
                break;

            case ProxyMode.Http:
                handler.UseProxy = true;
                handler.Proxy = BuildExplicitProxy("http", settings);
                break;

            case ProxyMode.Socks5:
                handler.UseProxy = true;
                handler.Proxy = BuildExplicitProxy("socks5", settings);
                break;
        }

        return handler;
    }

    public static HttpClient CreateClient(ProxySettings proxy, int timeoutSeconds = 60, HttpMessageHandler? customHandler = null)
    {
        var handler = customHandler ?? CreateHandler(proxy);
        return new HttpClient(handler, disposeHandler: customHandler is null)
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 300)),
        };
    }

    private static IWebProxy BuildExplicitProxy(string scheme, ProxySettings s)
    {
        string host = (s.Host ?? string.Empty).Trim();
        int port = s.Port > 0 && s.Port <= 65535 ? s.Port : (scheme == "socks5" ? 1080 : 8080);

        if (string.IsNullOrEmpty(host))
        {
            throw new InvalidOperationException("已启用自定义代理，但代理服务器主机名为空。");
        }

        // 若用户填了完整 scheme://host 则兼容剥离再拼
        if (Uri.TryCreate(host, UriKind.Absolute, out var parsed) && !string.IsNullOrEmpty(parsed.Host))
        {
            host = parsed.Host;
            if (parsed.Port > 0) port = parsed.Port;
        }

        var proxyUri = new UriBuilder(scheme, host, port).Uri;
        var wp = new WebProxy(proxyUri);

        if (!string.IsNullOrWhiteSpace(s.UserName))
        {
            char[]? pwdChars = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(s.EncryptedPassword))
                {
                    string decrypted = SecretStore.Unprotect(s.EncryptedPassword);
                    pwdChars = decrypted.ToCharArray();
                    wp.Credentials = new NetworkCredential(s.UserName.Trim(), decrypted);
                }
                else
                {
                    wp.Credentials = new NetworkCredential(s.UserName.Trim(), string.Empty);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"解密代理凭据失败：{Log.Redact(ex.Message)}");
            }
            finally
            {
                if (pwdChars is not null) Array.Clear(pwdChars);
            }
        }

        return wp;
    }
}

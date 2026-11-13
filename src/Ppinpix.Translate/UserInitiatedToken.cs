namespace Ppinpix.Translate;

/// <summary>
/// 用户显式触发令牌（有效期 10 秒），在编译期约束所有翻译 API 调用必须源自真实用户输入事件。
/// 后台定时器或静态初始化流程无法伪造长期有效令牌。
/// </summary>
public sealed class UserInitiatedToken
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(10);

    private readonly DateTimeOffset _expiresAtUtc;

    private UserInitiatedToken(DateTimeOffset issuedAtUtc)
    {
        IssuedAtUtc = issuedAtUtc;
        _expiresAtUtc = issuedAtUtc + Lifetime;
    }

    public DateTimeOffset IssuedAtUtc { get; }

    public DateTimeOffset ExpiresAtUtc => _expiresAtUtc;

    public bool IsValid => DateTimeOffset.UtcNow <= _expiresAtUtc;

    public static UserInitiatedToken IssueFromUserAction() => new(DateTimeOffset.UtcNow);

    /// <summary>供单元测试验证过期拦截语义使用。</summary>
    internal static UserInitiatedToken IssueAtForTest(DateTimeOffset issuedAtUtc) => new(issuedAtUtc);

    public void ThrowIfExpired()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException("翻译操作令牌已过期（超过 10 秒），请重新手动触发翻译。");
        }
    }
}

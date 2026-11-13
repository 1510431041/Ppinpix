# Ppinpix 隐私保护与零外联承诺（v1.0）

> 本文档是 Ppinpix 对用户的隐私承诺，以及技术实现保证。

---

## 1. 核心承诺

**Ppinpix 承诺：除用户主动触发翻译请求所配置的接口外，软件全程在本地运行，不产生任何外部数据上传。**

具体保证：

1. **截图数据完全本地**：截图内容仅存储在用户计算机本地，不上传任何服务器。
2. **OCR 完全离线**：文字识别使用本地模型，不调用任何在线 OCR 服务。
3. **贴图完全本地**：贴图内容仅在本地显示和存储，不上传。
4. **翻译可控外联**：仅在用户点击「翻译」按钮时，向用户自己配置的接口发送数据。
5. **无遥测/无崩溃上报**：不向开发者或任何第三方上传使用数据、错误日志、崩溃信息。
6. **无在线更新检查**：不自动检查更新，不连接更新服务器。
7. **无在线资源加载**：不加载在线字体、在线图标、在线帮助文档。

---

## 2. 数据流向图

```
┌──────────────────────────────────────────────────────────────────┐
│                        Ppinpix 进程边界                           │
│                                                                  │
│  ┌────────┐  ┌────────┐  ┌────────┐  ┌────────┐  ┌────────┐    │
│  │ 截图   │  │ 标注   │  │ 贴图   │  │ OCR    │  │ 设置   │    │
│  └────────┘  └────────┘  └────────┘  └────────┘  └────────┘    │
│       │            │            │            │            │      │
│       └────────────┴────────────┴────────────┴────────────┘      │
│                             │                                    │
│                             ▼                                    │
│                    ┌─────────────────┐                           │
│                    │ 本地文件系统    │                           │
│                    │ %APPDATA%       │                           │
│                    │ %USERPROFILE%   │                           │
│                    └─────────────────┘                           │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  翻译模块（Ppinpix.LlmGateway）                          │   │
│  │  ★ 全程序唯一允许网络访问的组件                          │   │
│  └──────────────────────────────────────────────────────────┘   │
│       │                                                          │
└───────┼──────────────────────────────────────────────────────────┘
        │ ★ 仅在用户点击「翻译」时
        │ ★ 仅发送到用户配置的接口
        ▼
   ┌─────────────────────┐
   │ 用户自配置的接口     │
   │ https://api.xxx.com │
   └─────────────────────┘
```

---

## 3. 技术实现保证

### 3.1 架构隔离

**模块依赖规则**：

```
Ppinpix.App (托盘/热键/设置 UI)
    │
    ├─── Ppinpix.Capture (截图/标注)
    │         └─── Ppinpix.Core (基础设施)
    │
    ├─── Ppinpix.Pin (贴图)
    │         └─── Ppinpix.Core
    │
    ├─── Ppinpix.Ocr (本地 OCR)
    │         └─── Ppinpix.Core
    │
    ├─── Ppinpix.Translate (翻译编排)
    │         ├─── Ppinpix.Ocr
    │         ├─── Ppinpix.LlmGateway ← 唯一网络组件
    │         └─── Ppinpix.Core
    │
    └─── Ppinpix.LlmGateway (网络请求)
              └─── System.Net.Http ← 唯一允许引用网络 API
```

**强制规则**：
- 只有 `Ppinpix.LlmGateway` 允许引用 `System.Net.Http` / `System.Net.Sockets` 等网络命名空间
- 其他所有模块禁止引用网络 API（编译时检查 + ArchTests 强制）

### 3.2 架构测试（CI 强制执行）

**测试代码**（`Ppinpix.ArchTests`）：

```csharp
[Fact]
public void Only_LlmGateway_Can_Access_Network()
{
    var result = Types.InCurrentDomain()
        .That().ResideInNamespaceStartingWith("Ppinpix")
        .And().DoNotResideInNamespace("Ppinpix.LlmGateway")
        .ShouldNot().HaveDependencyOn("System.Net.Http")
        .ShouldNot().HaveDependencyOn("System.Net.Sockets")
        .ShouldNot().HaveDependencyOn("System.Net.WebRequest")
        .GetResult();
    
    Assert.True(result.IsSuccessful, 
        "发现非 LlmGateway 模块访问网络 API：\n" + 
        string.Join("\n", result.FailingTypes));
}
```

**CI 流程**：
1. 每次提交代码时自动运行 ArchTests
2. 违反规则时构建失败，无法合并
3. 发布前必须通过此测试

### 3.3 用户授权令牌

**翻译请求必须携带用户授权**：

```csharp
// 编译时强制要求 UserInitiatedToken
public async Task<TranslationResult> TranslateAsync(
    string text,
    string sourceLang,
    string targetLang,
    UserInitiatedToken authToken,  // ← 必须传入
    CancellationToken ct)
{
    // 验证令牌来自 UI 用户操作
    if (authToken.Timestamp < DateTime.UtcNow.AddSeconds(-10))
        throw new InvalidOperationException("授权令牌已过期");
    
    // 发送网络请求...
}

// 令牌由 UI 层创建
button.Click += (s, e) =>
{
    var token = new UserInitiatedToken(DateTime.UtcNow, "用户点击翻译");
    await translationService.TranslateAsync(text, "auto", "zh", token, ct);
};
```

**作用**：防止后台代码在用户不知情时偷偷发起翻译请求。

### 3.4 无遥测代码

**禁止的行为**（代码审查清单）：
- ❌ 任何形式的匿名使用统计上传
- ❌ Application Insights / Sentry / Bugsnag 等崩溃报告服务
- ❌ Google Analytics / Mixpanel 等分析服务
- ❌ 自动更新检查（联网检查版本号）
- ❌ 在线许可证验证
- ❌ 在线激活

**允许的网络行为**：
- ✅ 用户点击「翻译」时向用户配置的接口发送请求
- ✅ 用户点击「测试连接」时向用户配置的接口发送测试请求
- ✅ 用户手动点击「检查更新」时打开内部分发页面（浏览器，非程序内）

---

## 4. 数据存储位置

### 4.1 本地存储清单

| 数据类型 | 存储位置 | 加密 | 可导出 | 可清空 |
|----------|----------|------|--------|--------|
| 截图文件 | `%USERPROFILE%\Pictures\Screenshots\` | ❌ | ✅ | ✅ |
| 应用设置 | `%APPDATA%\Ppinpix\settings.json` | API Key 加密 | ✅（Key 脱敏） | ✅ |
| 加密盐 | `%APPDATA%\Ppinpix\.entropy` | ❌ | ❌ | ❌ |
| 保存的贴图 | `%APPDATA%\Ppinpix\pins\` | ❌ | ✅ | ✅ |
| 截图历史 | `%APPDATA%\Ppinpix\history\` | ❌ | ✅ | ✅ |
| 日志文件 | `%APPDATA%\Ppinpix\logs\` | 敏感信息脱敏 | ✅ | ✅ |
| OCR 模型 | `%ProgramFiles%\Ppinpix\models\` | ❌ | ❌ | ❌ |

### 4.2 自动清理策略

| 数据类型 | 清理策略 | 说明 |
|----------|----------|------|
| 日志文件 | 保留最近 7 天 | 每日滚动，自动删除旧日志 |
| 截图历史 | 保留最近 50 张（可配置 0-200） | 超出时自动删除最旧 |
| 保存的贴图 | 不自动清理（用户可手动清空） | 退出时保存，启动时恢复 |
| 临时文件 | 程序退出时清理 | 标注编辑、翻译缓存等 |

### 4.3 用户数据导出

**设置页提供「导出所有数据」功能**：
- 打包 `settings.json`（API Key 默认脱敏）+ `pins/` + 最近截图
- 导出为 ZIP 文件
- 用户可选「包含 API Key」（二次确认）

**设置页提供「清空所有数据」功能**：
- 删除 `%APPDATA%\Ppinpix\` 下所有文件（除 `.entropy`）
- 恢复默认设置
- 需二次确认

---

## 5. API Key 安全

### 5.1 加密存储

**技术方案**：Windows DPAPI（Data Protection API）

```csharp
// 加密（保存时）
public static string Encrypt(string plaintext)
{
    var entropy = LoadOrGenerateEntropy(); // 32 字节随机值
    var plainBytes = Encoding.UTF8.GetBytes(plaintext);
    var cipherBytes = ProtectedData.Protect(
        plainBytes, 
        entropy, 
        DataProtectionScope.CurrentUser
    );
    return "dpapi:" + Convert.ToBase64String(cipherBytes);
}

// 解密（使用时）
public static string Decrypt(string ciphertext)
{
    if (!ciphertext.StartsWith("dpapi:"))
        return ciphertext; // 兼容明文（旧版本）
    
    var entropy = LoadOrGenerateEntropy();
    var cipherBytes = Convert.FromBase64String(ciphertext["dpapi:".Length..]);
    var plainBytes = ProtectedData.Unprotect(
        cipherBytes, 
        entropy, 
        DataProtectionScope.CurrentUser
    );
    var result = Encoding.UTF8.GetString(plainBytes);
    
    // 立即清零内存
    Array.Clear(plainBytes, 0, plainBytes.Length);
    
    return result;
}
```

**安全特性**：
- 绑定到 **当前 Windows 用户账户**
- 拷贝 `settings.json` 到其他电脑或其他用户账户无法解密
- 即使拷走 `.entropy` 文件也无法解密（还需要 Windows 用户凭据）

### 5.2 内存安全

- 解密后的明文 API Key 仅在构造 HTTP 请求时短暂存在
- 请求构造完成后立即清零：`Array.Clear(keyBytes, 0, keyBytes.Length);`
- 不在字符串池中留存（使用 `SecureString` 或字节数组）
- GC 回收前覆盖写入

### 5.3 日志脱敏

**自动脱敏规则**（`Log.cs`）：

| 原始内容 | 脱敏后 |
|----------|--------|
| `Authorization: Bearer sk-abc123456789` | `Authorization: Bearer sk-a***` |
| `"apiKey": "sk-abc123456789"` | `"apiKey": "sk-a***"` |
| `x-api-key: abc123456789` | `x-api-key: abc***` |
| `data:image/png;base64,iVBORw0KGgo...` (长度>100) | `<image data 12345 bytes>` |

**实现**：
```csharp
[GeneratedRegex(@"(?i)(authorization|x-api-key)\s*:\s*\w+\s+(\S{3})\S+")]
private static partial Regex AuthHeaderPattern();

public static string Redact(string logMessage)
{
    return AuthHeaderPattern().Replace(logMessage, "$1: $2***");
}
```

### 5.4 UI 显示

- 配置页 API Key 输入框为 **密码框**（显示 `••••••••`）
- 「显示」按钮需**二次确认**对话框："确定要显示明文密钥吗？"
- 显示 5 秒后自动隐藏
- 截图/录屏时自动遮蔽（Windows `SetWindowDisplayAffinity`）

---

## 6. 网络活动监控（验收测试）

### 6.1 Wireshark 测试

**测试步骤**：
1. 启动 Wireshark 监听所有网络接口
2. 启动 Ppinpix
3. 进行以下操作（不触发翻译）：
   - 截图 10 次
   - 添加标注并保存
   - 创建贴图
   - OCR 识别
   - 打开设置页
   - 修改设置
   - 退出程序
4. 筛选 `ip.src == <本机IP>` 且 `tcp.dstport != (53 or 137 or 138)`（排除 DNS 和 NetBIOS）

**预期结果**：
- ✅ 无任何 TCP 连接建立
- ✅ 无任何 HTTP/HTTPS 请求

### 6.2 防火墙测试

**测试步骤**：
1. Windows 防火墙设置「阻止所有传出连接」
2. 添加 `Ppinpix.exe` 例外：「阻止」
3. 启动 Ppinpix
4. 进行所有操作（不触发翻译）

**预期结果**：
- ✅ 所有功能正常（无网络错误提示）
- ✅ 截图/标注/贴图/OCR 全部可用

### 6.3 翻译测试

**测试步骤**：
1. 配置翻译接口为本地抓包代理（如 Fiddler `http://127.0.0.1:8888`）
2. 进行翻译操作
3. 检查代理捕获的请求

**预期结果**：
- ✅ 仅在点击「翻译」时才有请求
- ✅ 请求目标为用户配置的 URL
- ✅ 请求体包含截图内容或 OCR 文字
- ✅ 无其他额外请求（统计/遥测/更新检查）

---

## 7. 首次使用提示

### 7.1 翻译功能首次使用

**触发时机**：用户首次点击任何翻译相关按钮

**弹窗内容**：
```
╔═══════════════════════════════════════════════════╗
║  🔒 隐私提示                                      ║
╠═══════════════════════════════════════════════════╣
║                                                   ║
║  您即将使用翻译功能。                             ║
║                                                   ║
║  翻译时，截图内容或识别的文字将发送至：            ║
║  https://api.2020111.xyz/v1                      ║
║                                                   ║
║  除此之外，Ppinpix 不会向任何服务器上传数据。      ║
║                                                   ║
║  您可以在「设置 > 翻译接口」中查看或修改配置。      ║
║                                                   ║
║  ☐ 不再提示                                       ║
║                                                   ║
║              [ 取消 ]        [ 继续 ]             ║
╚═══════════════════════════════════════════════════╝
```

### 7.2 设置页隐私说明

**位置**：设置窗口「关于」页

**内容**：
```markdown
## 🔒 隐私承诺

Ppinpix 是完全本地运行的工具。我们承诺：

✅ **截图/标注/贴图/OCR 完全本地处理**  
   您的数据不会离开您的计算机。

✅ **翻译功能可控外联**  
   仅在您点击「翻译」时，向您自己配置的接口发送数据。  
   您可以使用公司内部接口或任意 OpenAI 兼容服务。

✅ **无遥测/无崩溃上报**  
   我们不收集任何使用数据、错误日志或崩溃信息。

✅ **API Key 安全存储**  
   使用 Windows DPAPI 加密，绑定您的账户，他人无法窃取。

✅ **开源透明**  
   代码已开源，欢迎审计。

---

📁 **您的数据存储在**：
   - 截图：`C:\Users\您的用户名\Pictures\Screenshots\`
   - 设置：`%APPDATA%\Ppinpix\`

🔍 **查看日志**：[打开日志文件夹]

🗑️ **清空数据**：[清空所有设置和历史]
```

---

## 8. 开源与审计

### 8.1 代码开源

- 源代码托管在内部 GitLab
- 许可证：MIT（或内部许可）
- 欢迎安全团队审计

### 8.2 构建可验证性

- 提供完整构建脚本（`build.ps1`）
- 从源代码可完整重现发布的二进制文件
- 发布版本附带 SHA256 校验和

### 8.3 安全联系方式

发现安全问题请发送至：`security@company.com`

---

## 9. 与 PixPin 的隐私对比

| 项目 | Ppinpix | PixPin（参考） |
|------|---------|----------------|
| 截图数据 | 完全本地 | 完全本地 |
| OCR | 本地模型（RapidOCR） | 本地模型（PaddleOCR） |
| 翻译 | 用户自配置接口 | 内置接口（需联网） |
| 遥测 | 无 | 有（匿名使用统计） |
| 崩溃报告 | 无 | 有（Bugly） |
| 自动更新 | 无（手动安装） | 有（自动检查） |
| 云同步 | 无 | 有（登录后） |

**Ppinpix 的优势**：
- ✅ 零外联（除用户主动翻译）
- ✅ 无账号体系（无需注册/登录）
- ✅ 无云服务依赖
- ✅ 适合内网环境

---

## 10. 常见问题（FAQ）

**Q1：翻译功能是如何保证隐私的？**  
A：翻译时仅向您自己配置的接口发送数据，接口地址和 API Key 由您填写。您可以使用公司内部接口，数据不会流出公司网络。

**Q2：OCR 需要联网吗？**  
A：不需要。OCR 使用本地模型（RapidOCR），完全离线，识别速度快且准确。

**Q3：如何确认 Ppinpix 没有偷偷上传数据？**  
A：您可以：
1. 使用 Wireshark 监控网络流量
2. 使用防火墙阻止 Ppinpix 联网，所有功能（除翻译外）仍正常
3. 审计源代码（已开源）

**Q4：API Key 如何保护？**  
A：使用 Windows DPAPI 加密，绑定您的 Windows 账户。即使别人拿到您的配置文件，也无法在其他电脑或其他账户下解密。

**Q5：我可以完全断网使用吗？**  
A：可以。除翻译功能外，所有功能（截图/标注/贴图/OCR）完全离线可用。

**Q6：会不会收集使用数据用于改进产品？**  
A：不会。我们不收集任何数据。如需反馈，请主动通过内部渠道联系我们。

**Q7：日志文件会泄露敏感信息吗？**  
A：不会。日志文件自动脱敏 API Key、图片数据等敏感信息，可以安全分享用于问题排查。

---

## 11. 验收标准

### 11.1 架构测试
- [ ] ArchTests 通过（仅 LlmGateway 有网络权限）
- [ ] 所有其他模块无 `System.Net` 引用

### 11.2 网络测试
- [ ] Wireshark 测试：不触发翻译时零网络活动
- [ ] 防火墙测试：阻止联网后所有功能（除翻译外）正常
- [ ] 翻译测试：仅向用户配置的 URL 发送数据

### 11.3 安全测试
- [ ] API Key 加密后无法用文本编辑器读取明文
- [ ] 拷贝配置到其他账户无法解密
- [ ] 日志文件无 API Key 明文
- [ ] 崩溃转储无敏感信息

### 11.4 用户体验测试
- [ ] 首次翻译显示隐私提示
- [ ] 设置页有清晰的隐私说明
- [ ] 可一键清空所有数据

---

**承诺签署**：

本文档是 Ppinpix 对所有用户的正式承诺。如有违背，请通过内部渠道举报。

Ppinpix 开发团队  
2025 年 1 月

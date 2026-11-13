# Ppinpix M4 里程碑：大模型截图翻译（3-4 周）

> **目标**：集成自定义大模型接口，实现流式翻译对照浮窗、API 配置管理、零外联架构验证，完成 1.0 版本。

---

## 1. 里程碑概览

**前置依赖**：M3 完成  
**周期**：3-4 周（18 个工作日）  
**交付日期**：2026-02-28（预估）  
**状态**：📋 计划中  
**版本号**：**v1.0.0**（正式版）

---

## 2. 功能范围

### 2.1 ✅ 包含功能（必须实现）

#### A. 翻译核心功能
- [ ] **翻译对照浮窗**（原文/译文上下分栏）
- [ ] **流式输出**（SSE 逐字追加）
- [ ] **编辑原文重译**（`Ctrl+Enter`）
- [ ] **语言自动识别**（中→英 / 其它→中）
- [ ] **手动切换语言**（源语言/目标语言下拉）
- [ ] **停止翻译**（取消按钮，`Esc` 快捷键）
- [ ] **复制译文/原文/双语**
- [ ] **显示翻译耗时**

#### B. 翻译触发入口
- [ ] **截图工具栏「翻译」按钮**
- [ ] **贴图右键「翻译」**
- [ ] **OCR 结果面板「翻译」按钮**
- [ ] **独立热键 `Ctrl+Shift+T`** 截图并翻译
- [ ] **首次使用提示**（隐私告知）

#### C. 翻译模式
- [ ] **模式一：先 OCR 后翻译**（默认，省 token）
- [ ] **模式二：直接发图翻译**（多模态模型，适合复杂排版）
- [ ] **每套配置可单独设定默认模式**

#### D. API 配置管理
- [ ] **支持多套配置**（公司网关/DeepSeek/OpenAI...）
- [ ] **配置项**：
  - 名称、协议类型（OpenAI 兼容/Anthropic/Gemini）
  - 接口地址、API Key、模型名称
  - 翻译 Prompt（可自定义，支持变量）
  - 识别模式（OCR 后 / 直接发图）
  - 网络代理（不使用/系统/HTTP/SOCKS5）
  - 高级选项（温度/最大 tokens/超时/流式/额外 Header）
- [ ] **标记当前默认配置**
- [ ] **翻译时可临时切换配置**
- [ ] **复制/编辑/删除配置**

#### E. API Key 安全
- [ ] **DPAPI 加密存储**（绑定 Windows 账户）
- [ ] **Entropy 随机生成**（单独存储 `.entropy` 文件）
- [ ] **UI 密码框显示**（••••）
- [ ] **「显示」按钮需二次确认**
- [ ] **日志自动脱敏**（`Authorization: Bearer sk-***`）
- [ ] **导出配置时默认不含 Key**

#### F. 测试连接功能
- [ ] **「测试连接」按钮**
- [ ] **发送最小化请求验证配置**
- [ ] **成功显示**：✅ + 耗时 + 模型返回
- [ ] **失败显示分类提示**：
  - DNS 错误/连接失败/TLS 错误
  - 401/403（API Key 无效）
  - 404（接口或模型不存在）
  - 429（限流/余额不足）
  - 超时/服务器错误
- [ ] **显示原始响应**（前 500 字符，Key 脱敏）
- [ ] **自动探测流式支持**

#### G. 网络配置
- [ ] **代理支持**：不使用/系统/HTTP/SOCKS5
- [ ] **代理认证**（用户名/密码，密码加密存储）
- [ ] **连接超时**（默认 15 秒）
- [ ] **整体请求超时**（默认 60 秒）
- [ ] **自定义 Header**（用于网关鉴权）

#### H. 设置页新增
- [ ] **翻译接口页**：
  - 左侧配置列表（显示名称 + 默认标记）
  - 右侧配置详情编辑区
  - 「新建」「复制」「删除」按钮
  - 「设为默认」按钮
  - 「测试连接」按钮
  - 「恢复默认 Prompt」按钮
  - API Key 密码框 + 眼睛图标

#### I. 隐私与零外联保证
- [ ] **架构测试**（ArchTests，强制只有 LlmGateway 有网络权限）
- [ ] **Wireshark 验证**（不触发翻译时零网络连接）
- [ ] **防火墙测试**（阻止联网后 M1-M3 功能正常）
- [ ] **首次翻译弹窗告知**（数据发送地址）

### 2.2 ✅ 可选功能（M4 后期）
- [ ] **翻译原位覆盖**（在原图上覆盖译文）
- [ ] **结构化 JSON 输出**（段落 id + 译文）
- [ ] **流式增量解析**（每完成一段立刻覆盖）
- [ ] **背景色/文字色自动适配**
- [ ] **Anthropic / Gemini 协议支持**

### 2.3 ❌ 不包含功能（未来版本）
- ❌ 历史记录管理 → **v1.1**
- ❌ 术语库/翻译记忆 → **v1.2**
- ❌ 批量翻译 → **低优先级**

---

## 3. 技术架构

### 3.1 新增模块

```
src/
├── Ppinpix.LlmGateway/          ← M4 新建（唯一网络权限）
│   ├── Providers/
│   │   ├── ILlmProvider.cs      LLM 提供商接口
│   │   ├── OpenAiProvider.cs    OpenAI 兼容协议
│   │   ├── AnthropicProvider.cs （可选）
│   │   └── GeminiProvider.cs    （可选）
│   │
│   ├── Sse/
│   │   └── SseStreamParser.cs   SSE 流式解析器
│   │
│   ├── Models/
│   │   ├── LlmRequest.cs        请求模型
│   │   ├── LlmResponse.cs       响应模型
│   │   └── ConnectionTestResult.cs
│   │
│   ├── Proxy/
│   │   └── ProxyFactory.cs      代理工厂（HTTP/SOCKS5）
│   │
│   └── ConnectionTester.cs      测试连接
│
├── Ppinpix.Translate/           ← M4 新建
│   ├── TranslateService.cs      翻译服务（编排 OCR + LLM）
│   ├── LanguageDetector.cs      源语言检测
│   ├── PromptBuilder.cs         Prompt 模板展开
│   └── UserInitiatedToken.cs    用户授权令牌
│
└── Ppinpix.App/
    └── Translate/
        └── TranslateWindow.xaml  ← M4 新建（对照浮窗）
```

### 3.2 依赖关系

```
Ppinpix.App
    ├─── Ppinpix.Translate
    │       ├─── Ppinpix.Ocr
    │       ├─── Ppinpix.LlmGateway  ← 唯一网络组件
    │       └─── Ppinpix.Core
    └─── Ppinpix.Capture/Pin/Theme/Core
```

**架构约束**：
- 只有 `Ppinpix.LlmGateway` 允许引用 `System.Net.Http`
- 其他所有程序集禁止引用网络 API
- `ArchTests` 在 CI 中强制验证

### 3.3 核心设计

#### LLM 提供商接口

```csharp
public interface ILlmProvider
{
    string Name { get; }
    
    IAsyncEnumerable<string> StreamChatAsync(
        LlmRequest request, 
        CancellationToken ct
    );
    
    Task<string> ChatAsync(
        LlmRequest request, 
        CancellationToken ct
    );
    
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct);
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct);
}

public record LlmRequest(
    string SystemPrompt,
    string UserText,
    byte[]? ImagePng,
    double Temperature,
    int MaxTokens
);
```

#### 用户授权令牌（防止后台偷偷翻译）

```csharp
public record UserInitiatedToken(DateTime Timestamp, string ActionName)
{
    public bool IsValid => DateTime.UtcNow - Timestamp < TimeSpan.FromSeconds(10);
}

// 编译时强制要求
public async Task<string> TranslateAsync(
    string text,
    UserInitiatedToken authToken,  // ← 必须传入
    CancellationToken ct)
{
    if (!authToken.IsValid)
        throw new InvalidOperationException("授权令牌已过期");
    
    // 翻译逻辑...
}
```

#### 翻译服务编排

```csharp
public class TranslateService
{
    private readonly OcrService _ocr;
    private readonly ILlmProvider _llm;
    
    public async IAsyncEnumerable<string> TranslateAsync(
        ScreenFrame frame,
        TranslateOptions options,
        UserInitiatedToken authToken,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // 1. 语言检测
        var sourceLang = options.SourceLang == "auto" 
            ? await LanguageDetector.DetectAsync(frame) 
            : options.SourceLang;
        var targetLang = DetermineTargetLang(sourceLang, options);
        
        // 2. 模式选择
        string text;
        byte[]? image = null;
        
        if (options.Mode == TranslateMode.OcrThenTranslate)
        {
            // 先 OCR
            var ocrResult = await _ocr.RecognizeAsync(frame, ct);
            text = string.Join("\n\n", ocrResult.Paragraphs.Select(p => p.Text));
        }
        else
        {
            // 直接发图
            text = "请识别并翻译图片中的文字";
            image = frame.ToBytes(ImageFormat.Png);
        }
        
        // 3. 构造 Prompt
        var prompt = PromptBuilder.Build(options.PromptTemplate, sourceLang, targetLang, text);
        
        // 4. 流式翻译
        var request = new LlmRequest(prompt, text, image, options.Temperature, options.MaxTokens);
        await foreach (var chunk in _llm.StreamChatAsync(request, ct))
        {
            yield return chunk;
        }
    }
}
```

#### SSE 流式解析

```csharp
public static async IAsyncEnumerable<string> ParseSseStream(
    Stream stream,
    [EnumeratorCancellation] CancellationToken ct)
{
    using var reader = new StreamReader(stream, Encoding.UTF8);
    
    while (!ct.IsCancellationRequested)
    {
        var line = await reader.ReadLineAsync(ct);
        if (line == null) break;
        
        if (string.IsNullOrWhiteSpace(line)) continue;
        if (!line.StartsWith("data: ")) continue;
        
        var data = line["data: ".Length..].Trim();
        if (data == "[DONE]") break;
        
        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(data);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("delta")
                .GetProperty("content")
                .GetString();
            
            if (!string.IsNullOrEmpty(content))
                yield return content;
        }
        catch (JsonException)
        {
            // 容错：忽略无法解析的行
        }
        finally
        {
            doc?.Dispose();
        }
    }
}
```

---

## 4. 开发任务清单

### Week 1（Day 1-6）：LLM 网关与基础翻译

#### Day 1-2：LlmGateway 基础
- [ ] 创建 `Ppinpix.LlmGateway` 项目
- [ ] 实现 `ILlmProvider` 接口
- [ ] 实现 `OpenAiProvider.cs`：
  - [ ] 构造 HTTP 请求（`/chat/completions`）
  - [ ] 处理 Bearer Token 认证
  - [ ] 非流式请求（`stream: false`）
  - [ ] 错误处理（401/404/429 等）
- [ ] 实现 `SseStreamParser.cs`：
  - [ ] 解析 `data: {...}` 行
  - [ ] 提取 `choices[0].delta.content`
  - [ ] 处理 `[DONE]` 或连接关闭
- [ ] 实现流式请求（`StreamChatAsync`）
- [ ] 单元测试：Mock HTTP 响应

**验收**：
- ✅ 非流式请求成功
- ✅ 流式解析正确（逐字追加）

#### Day 3：代理与网络配置
- [ ] 实现 `ProxyFactory.cs`：
  - [ ] 系统代理（`WebRequest.GetSystemWebProxy`）
  - [ ] HTTP 代理（`WebProxy`）
  - [ ] SOCKS5 代理（`WebProxy` + `UseDefaultCredentials`）
  - [ ] 认证支持（`NetworkCredential`）
- [ ] 实现超时配置：
  - [ ] 连接超时（`SocketsHttpHandler.ConnectTimeout`）
  - [ ] 整体请求超时（`HttpClient.Timeout`）
- [ ] `HttpClient` 实例管理：
  - [ ] 按配置哈希缓存复用
  - [ ] `SocketsHttpHandler` 单独配置代理

**验收**：
- ✅ HTTP 代理连接成功
- ✅ 超时配置生效

#### Day 4：测试连接功能
- [ ] 实现 `ConnectionTester.cs`：
  - [ ] 发送最小化请求（`"Reply with OK"`，`max_tokens=5`）
  - [ ] 捕获所有异常类型并分类
  - [ ] 返回 `ConnectionTestResult`（成功/失败 + 详情）
- [ ] 错误分类逻辑（见 `LLM_API.md` 6.1）：
  - DNS 解析失败 → "无法连接到服务器"
  - 连接被拒绝 → "服务器拒绝连接"
  - TLS 握手失败 → "SSL/TLS 连接失败"
  - 401 → "API Key 无效"
  - 404 → "接口或模型不存在"
  - 429 → "请求过于频繁或余额不足"
  - 超时 → "连接超时"
- [ ] 单元测试：模拟各种错误场景

**验收**：
- ✅ 10 种常见错误分类准确
- ✅ 成功时返回耗时和模型响应

#### Day 5-6：翻译服务编排
- [ ] 创建 `Ppinpix.Translate` 项目
- [ ] 实现 `TranslateService.cs`：
  - [ ] `TranslateAsync` 流式接口
  - [ ] OCR 模式 / 直接发图模式切换
  - [ ] 语言自动检测（`LanguageDetector`）
  - [ ] Prompt 模板展开（`PromptBuilder`）
  - [ ] 用户授权令牌验证
- [ ] 实现 `LanguageDetector.cs`：
  - [ ] 统计字符：CJK / 英文 / 其它
  - [ ] CJK 占比 > 50% → 中文
  - [ ] 英文占比 > 50% → 英文
  - [ ] 其它 → 英文（默认）
- [ ] 实现 `PromptBuilder.cs`：
  - [ ] 替换变量：`{source_lang}` `{target_lang}` `{text}`
  - [ ] 提供默认模板（见 `LLM_API.md` 3.1）
- [ ] 实现 `UserInitiatedToken`：
  - [ ] 10 秒有效期
  - [ ] 编译时强制传入

**验收**：
- ✅ 翻译服务流式输出正常
- ✅ 语言自动检测准确率 > 95%
- ✅ 用户授权令牌机制工作正常

---

### Week 2（Day 7-12）：API 配置管理与 UI

#### Day 7-8：API 配置数据模型
- [ ] 实现 `LlmProfile.cs`（`Ppinpix.Core/Settings/`）：
  - [ ] 所有配置字段（见 `LLM_API.md` 附录 12）
  - [ ] `CreateDefault()` 预填默认值：
    ```csharp
    BaseUrl = "https://api.2020111.xyz/v1",
    Model = "翻译",
    Temperature = 0.3,
    Stream = true
    ```
- [ ] 扩展 `AppSettings.cs`：
  - [ ] `List<LlmProfile> LlmProfiles`
  - [ ] `string DefaultLlmProfileId`
- [ ] API Key 加密/解密（`SecretStore.cs`，M1 已实现）：
  - [ ] `Encrypt(string plaintext)` → `"dpapi:<Base64>"`
  - [ ] `Decrypt(string ciphertext)` → 明文
  - [ ] Entropy 生成/加载（`.entropy` 文件）
- [ ] 单元测试：
  - [ ] 加密后无法文本编辑器读取明文
  - [ ] 拷贝到其他账户无法解密

**验收**：
- ✅ 配置序列化/反序列化正常
- ✅ API Key 加密安全

#### Day 9-10：翻译接口设置页
- [ ] 创建 `TranslateSettingsPanel.xaml`：
  - [ ] 左侧配置列表（`ListBox`）
  - [ ] 右侧配置编辑区（`Grid`）
  - [ ] 「新建」「复制」「删除」「设为默认」按钮
- [ ] 配置列表项显示：
  - [ ] 名称 + 默认徽标（`Badge "默认"`）
  - [ ] 接口地址预览
- [ ] 配置编辑表单：
  - [ ] 名称（TextBox）
  - [ ] 协议类型（ComboBox：OpenAI 兼容 / Anthropic / Gemini）
  - [ ] 接口地址（TextBox + 验证）
  - [ ] API Key（PasswordBox + 眼睛按钮）
  - [ ] 模型名称（TextBox + 「获取模型列表」按钮，可选）
  - [ ] 翻译 Prompt（多行 TextBox + 「恢复默认」按钮）
  - [ ] 识别模式（RadioButton：OCR 后 / 直接发图）
  - [ ] 代理设置（展开面板）
  - [ ] 高级选项（展开面板）
- [ ] 「测试连接」按钮：
  - [ ] 点击后禁用按钮，显示「测试中…」
  - [ ] 调用 `ConnectionTester.TestAsync`
  - [ ] 成功/失败结果内联显示（见 `LLM_API.md` 7.2）
- [ ] API Key 显示按钮：
  - [ ] 点击弹出确认对话框："确定要显示明文密钥吗？"
  - [ ] 确认后临时显示明文（5 秒后自动隐藏）

**验收**：
- ✅ 所有配置项可编辑
- ✅ 新建/复制/删除/设为默认正常
- ✅ 测试连接分类提示准确
- ✅ API Key 显示需二次确认

#### Day 11-12：翻译对照浮窗
- [ ] 创建 `TranslateWindow.xaml`：
  - [ ] 宽 420px（可拖拽 320-720px）
  - [ ] 深色主题（`Surface` 背景）
  - [ ] 标题栏：配置下拉 / 语言下拉 / 钉住 / 关闭
  - [ ] 原文区（`TextBox`，可编辑）
  - [ ] 「重新翻译 Ctrl+Enter」按钮
  - [ ] 译文区（`TextBox`，只读）
  - [ ] 底栏：「复制译文」「复制原文」「复制双语」「停止」+ 耗时
- [ ] 实现 `TranslateWindow.xaml.cs`：
  - [ ] 绑定 `TranslateViewModel`
  - [ ] 流式输出：`IAsyncEnumerable<string>` → UI 逐字追加
  - [ ] 闪烁光标 `▍`（530ms 周期，`DispatcherTimer`）
  - [ ] `Ctrl+Enter` 重新翻译
  - [ ] 停止按钮：`CancellationTokenSource.Cancel()`
  - [ ] 配置/语言下拉切换后重译
- [ ] 窗口样式：
  - [ ] 置顶、可拖动、8px 圆角、阴影
  - [ ] 宽度可拖拽调整（`Thumb` 控件）
- [ ] 首次翻译弹窗：
  - [ ] 检测 `Settings.TranslateFirstTimeShown`
  - [ ] 弹窗告知："截图内容将发送至：<URL>"
  - [ ] 勾选「不再提示」

**验收**：
- ✅ 流式输出逐字显示
- ✅ 修改原文重译正常
- ✅ 停止按钮 < 500ms 响应
- ✅ 配置切换正常

---

### Week 3（Day 13-15）：集成与测试

#### Day 13：翻译功能集成
- [ ] 工具栏「翻译」按钮启用（`Features.Translate = true`）
- [ ] 点击后：
  - [ ] 创建 `UserInitiatedToken`
  - [ ] 调用 `TranslateService.TranslateAsync`
  - [ ] 弹出 `TranslateWindow` 流式显示
- [ ] 独立热键 `Ctrl+Shift+T`：
  - [ ] 截图 → 立即翻译
- [ ] 贴图右键菜单「翻译」
- [ ] OCR 结果面板「翻译」按钮

**验收**：
- ✅ 所有入口翻译功能正常
- ✅ 默认接口 `https://api.2020111.xyz/v1` + 模型 `翻译` 可用

#### Day 14：架构测试与隐私验证
- [ ] 创建 `Ppinpix.ArchTests` 项目
- [ ] 实现架构测试：
  ```csharp
  [Fact]
  public void Only_LlmGateway_Can_Access_Network()
  {
      var result = Types.InCurrentDomain()
          .That().ResideInNamespaceStartingWith("Ppinpix")
          .And().DoNotResideInNamespace("Ppinpix.LlmGateway")
          .ShouldNot().HaveDependencyOn("System.Net.Http")
          .ShouldNot().HaveDependencyOn("System.Net.Sockets")
          .GetResult();
      
      Assert.True(result.IsSuccessful);
  }
  ```
- [ ] CI 集成：构建时自动运行 ArchTests
- [ ] **Wireshark 验证**：
  - [ ] 不触发翻译时零网络连接
  - [ ] 翻译时仅连接用户配置的 URL
- [ ] **防火墙验证**：
  - [ ] 阻止 Ppinpix 联网
  - [ ] M1-M3 所有功能正常
  - [ ] 翻译功能提示网络错误（不崩溃）

**验收**：
- ✅ ArchTests 通过
- ✅ Wireshark 验证通过
- ✅ 防火墙验证通过

#### Day 15：日志脱敏与错误处理
- [ ] 实现日志脱敏（`Log.cs`，M1 已有基础）：
  - [ ] 正则 `Authorization: Bearer sk-xxx` → `sk-***`
  - [ ] 正则 `"apiKey": "sk-xxx"` → `sk-***`
  - [ ] Base64 图片数据 > 100 字符 → `<image data 12345 bytes>`
- [ ] 单元测试：所有敏感信息脱敏
- [ ] 翻译错误处理：
  - [ ] 网络错误 → Toast 友好提示
  - [ ] 超时 → 显示"服务器响应缓慢"
  - [ ] 401 → "API Key 无效"
  - [ ] 429 → "请求过于频繁"
  - [ ] 模型返回格式错误 → 显示原始响应前 200 字符

**验收**：
- ✅ 日志文件无 API Key 明文
- ✅ 所有错误有友好提示

---

### Week 4（Day 16-18）：优化、文档、发布

#### Day 16：性能与稳定性测试
- [ ] **性能测试**：
  - [ ] 翻译首字延迟 < 50ms（程序开销）
  - [ ] 流式渲染帧率 60fps
  - [ ] 并发 3 个翻译窗口不崩溃
- [ ] **稳定性测试**：
  - [ ] 连续翻译 100 次不崩溃
  - [ ] 中途取消 50 次不崩溃
  - [ ] 网络断开/恢复 20 次不崩溃
- [ ] **内存测试**：
  - [ ] 常驻内存 < 150MB（含 OCR 模型）
  - [ ] 翻译 20 次无内存泄漏

**验收**：
- ✅ 性能指标达标
- ✅ 稳定性测试通过

#### Day 17：集成测试与回归测试
- [ ] **M1-M3 回归测试**：
  - [ ] 截图、标注、贴图、OCR 全部功能正常
  - [ ] DPI 测试矩阵（100%/150%/混合双屏）
  - [ ] 键盘快捷键全部正常
- [ ] **M4 集成测试**：
  - [ ] 所有翻译入口可用
  - [ ] 配置管理正常
  - [ ] 测试连接准确
  - [ ] 流式输出正常
  - [ ] 停止/重译正常
- [ ] **隐私测试**：
  - [ ] ArchTests 通过
  - [ ] Wireshark 零外联
  - [ ] 首次翻译提示显示

**验收**：
- ✅ M1-M3 无回归问题
- ✅ M4 所有功能正常

#### Day 18：文档、演示、发布
- [ ] 更新 `README.md`（完整 1.0 功能说明）
- [ ] 更新 `CHANGELOG.md`（**v1.0.0 正式版**）
- [ ] 录制完整演示视频（10 分钟）：
  - [ ] 截图 → 标注 → 贴图
  - [ ] OCR → 翻译
  - [ ] 配置管理 → 测试连接
- [ ] 编写《使用手册》（PDF / Markdown）
- [ ] 编写《隐私与安全说明》
- [ ] 编写《API 接口对接指南》
- [ ] 打包发布：
  - [ ] `Ppinpix-v1.0.0-win-x64.zip`（自包含单文件，约 120MB）
  - [ ] SHA256 校验和
  - [ ] 内部分发页面更新

---

## 5. 可选功能：翻译原位覆盖（M4 后期）

### 5.1 任务
- [ ] OCR 段落 ID 标记（`[1] text`, `[2] text`）
- [ ] Prompt 要求模型返回 JSON：`[{"id":1,"text":"..."}]`
- [ ] 流式增量 JSON 解析（正则提取完整对象）
- [ ] 背景色自适应（取段落外沿像素中位数）
- [ ] 文字色自适应（K-means 聚类，选高对比度）
- [ ] 字号自适应（二分搜索最大可放入字号）
- [ ] 渲染为新贴图
- [ ] 按住 `Alt` 临时显示原图

### 5.2 验收
- ✅ 简单排版覆盖正确
- ✅ 背景色/文字色自适应合理
- ✅ 流式增量覆盖每段完成立刻显示

---

## 6. 风险与应对

| 风险 | 概率 | 影响 | 应对措施 |
|------|------|------|----------|
| 用户不填 API Key | 高 | 低 | 首次使用引导，提供测试 Key（有限额度） |
| 各家接口 SSE 细节不一致 | 中 | 中 | 宽松解析 + 兼容测试（OpenAI/DeepSeek/通义） |
| 架构测试被绕过 | 低 | 高 | CI 强制运行，违反时构建失败 |
| 原位覆盖复杂背景效果差 | 中 | 低 | 保留对照浮窗为默认，覆盖为可选 |
| 用户担心隐私泄露 | 中 | 高 | 首次弹窗明确告知 + PRIVACY.md + Wireshark 演示 |

---

## 7. 验收标准（M4 / v1.0 交付门槛）

### 7.1 必须通过（P0）
- [ ] ✅ M1-M3 所有功能保持正常
- [ ] ✅ 默认接口 `https://api.2020111.xyz/v1` + 模型 `翻译` 可用
- [ ] ✅ 流式翻译延迟 < 50ms（程序开销）
- [ ] ✅ API Key 加密存储，日志脱敏
- [ ] ✅ 测试连接分类提示准确
- [ ] ✅ ArchTests 通过（仅 LlmGateway 有网络权限）
- [ ] ✅ Wireshark 验证通过（除翻译外零网络连接）
- [ ] ✅ 防火墙验证通过（M1-M3 功能不受影响）

### 7.2 应该通过（P1）
- [ ] ✅ 对照浮窗所有功能正常
- [ ] ✅ 配置管理完善（新建/编辑/删除/测试）
- [ ] ✅ 首次翻译隐私提示
- [ ] ✅ 并发 3 个翻译窗口不崩溃

### 7.3 可选通过（P2）
- [ ] 翻译原位覆盖
- [ ] Anthropic / Gemini 协议支持
- [ ] 代理认证支持

---

## 8. 发布计划

### 8.1 v1.0.0 正式版
- **发布日期**：2026-02-28
- **版本号**：1.0.0
- **里程碑**：完整功能，对标 PixPin
- **交付物**：
  - `Ppinpix-v1.0.0-win-x64.zip`（单文件自包含，约 120MB）
  - 使用手册（PDF / Markdown）
  - 演示视频（10 分钟）
  - 隐私与安全说明
  - API 对接指南

### 8.2 后续版本规划
- **v1.1**（2026-Q2）：历史记录、多语言 UI
- **v1.2**（2026-Q3）：术语库、翻译记忆
- **v2.0**（2026-Q4）：macOS 支持（Qt 重写）

---

## 9. 成功标准

M4 / v1.0 成功的标志：
1. ✅ 功能完整度达到 PixPin 核心功能 100%
2. ✅ 翻译功能实用且流畅（流式输出无卡顿）
3. ✅ 零外联承诺兑现（Wireshark + ArchTests 双重验证）
4. ✅ 内部至少 30 人日常使用，反馈正面
5. ✅ 无重大 Bug，无隐私问题
6. ✅ 文档完善，新用户可快速上手

达到以上标准后，宣布 **v1.0.0 正式发布**！🎉

---

## 10. 里程碑总结

| 里程碑 | 周期 | 功能 | 状态 |
|--------|------|------|------|
| M1 | 3 周 | 智能截图 | 🚧 进行中 |
| M2 | 3-4 周 | 标注 + 贴图 | 📋 计划中 |
| M3 | 2-3 周 | 离线 OCR | 📋 计划中 |
| **M4** | **3-4 周** | **大模型翻译** | **📋 计划中** |
| **总计** | **约 12-14 周** | **完整 1.0** | **目标 2026-02-28** |

---

**Ppinpix 1.0 —— 让每一次截图，都有价值。** 🚀

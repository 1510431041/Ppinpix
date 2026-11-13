# Ppinpix 大模型 API 集成规范（v1.0）

> 本文档规定 Ppinpix M4 大模型翻译功能的 API 对接标准、协议细节、错误处理、安全要求。

---

## 1. 支持的协议类型

### 1.1 OpenAI 兼容协议（主要支持）

**适用范围**：
- OpenAI 官方 API
- DeepSeek、Moonshot、通义千问、智谱 GLM 等国内厂商
- 公司内部网关
- 自建推理服务（vLLM、Ollama、text-generation-webui）

**默认配置**：
```json
{
  "name": "默认翻译接口",
  "provider": "OpenAI",
  "baseUrl": "https://api.2020111.xyz/v1",
  "model": "翻译",
  "apiKey": "<用户填写>",
  "stream": true
}
```

### 1.2 其他协议（M4 后期可选）

- **Anthropic Messages API**（Claude）
- **Google Gemini API**

---

## 2. OpenAI 兼容协议详细规范

### 2.1 端点与请求

#### 聊天完成端点

```
POST {baseUrl}/chat/completions
Content-Type: application/json
Authorization: Bearer {apiKey}
```

**请求体（纯文本翻译）**：
```json
{
  "model": "翻译",
  "messages": [
    {
      "role": "system",
      "content": "你是专业翻译助手。将用户输入的文本从{源语言}翻译为{目标语言}。只输出译文，不要解释。"
    },
    {
      "role": "user",
      "content": "The quick brown fox jumps over the lazy dog."
    }
  ],
  "temperature": 0.3,
  "max_tokens": 2000,
  "stream": true
}
```

**请求体（多模态图片翻译）**：
```json
{
  "model": "翻译",
  "messages": [
    {
      "role": "system",
      "content": "你是专业翻译助手。识别图片中的文字并翻译为{目标语言}。"
    },
    {
      "role": "user",
      "content": [
        {
          "type": "text",
          "text": "请识别并翻译这张图片中的文字"
        },
        {
          "type": "image_url",
          "image_url": {
            "url": "data:image/png;base64,iVBORw0KGgoAAAANS..."
          }
        }
      ]
    }
  ],
  "temperature": 0.3,
  "max_tokens": 2000,
  "stream": true
}
```

### 2.2 响应（流式 SSE）

**Content-Type**: `text/event-stream`

**事件流格式**：
```
data: {"id":"chatcmpl-123","object":"chat.completion.chunk","created":1234567890,"model":"翻译","choices":[{"index":0,"delta":{"role":"assistant","content":"敏"},"finish_reason":null}]}

data: {"id":"chatcmpl-123","object":"chat.completion.chunk","created":1234567890,"model":"翻译","choices":[{"index":0,"delta":{"content":"捷"},"finish_reason":null}]}

data: {"id":"chatcmpl-123","object":"chat.completion.chunk","created":1234567890,"model":"翻译","choices":[{"index":0,"delta":{"content":"的"},"finish_reason":null}]}

...

data: {"id":"chatcmpl-123","object":"chat.completion.chunk","created":1234567890,"model":"翻译","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

data: [DONE]
```

**解析规则**：
1. 按行读取，每行以 `data: ` 开头
2. 遇到 `data: [DONE]` 时流结束（某些接口不发此消息，改为检测连接关闭）
3. 每行 JSON 解析 `choices[0].delta.content`，逐字追加
4. 忽略 `content` 为空的 chunk
5. `finish_reason` 为 `stop` 或 `length` 时正常结束，为 `null` 时继续

### 2.3 响应（非流式）

**Content-Type**: `application/json`

```json
{
  "id": "chatcmpl-123",
  "object": "chat.completion",
  "created": 1234567890,
  "model": "翻译",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "敏捷的棕色狐狸跳过懒狗。"
      },
      "finish_reason": "stop"
    }
  ],
  "usage": {
    "prompt_tokens": 20,
    "completion_tokens": 15,
    "total_tokens": 35
  }
}
```

**解析规则**：
- 取 `choices[0].message.content` 作为完整译文

### 2.4 错误响应

```json
{
  "error": {
    "message": "Incorrect API key provided",
    "type": "invalid_request_error",
    "code": "invalid_api_key"
  }
}
```

**标准 HTTP 状态码**：
- `400` Bad Request - 请求格式错误
- `401` Unauthorized - API Key 无效
- `403` Forbidden - 无权限
- `404` Not Found - 端点或模型不存在
- `429` Too Many Requests - 限流或余额不足
- `500` Internal Server Error - 服务器错误
- `502`/`503`/`504` - 网关错误或超时

---

## 3. Prompt 模板设计

### 3.1 对照模式 Prompt（纯文本）

**默认模板**：
```
你是专业翻译助手，精通{source_lang}到{target_lang}的翻译。

任务：将以下文本从{source_lang}翻译为{target_lang}。

要求：
1. 准确传达原文含义，符合{target_lang}表达习惯
2. 保持原文的语气和风格（正式/口语/技术）
3. 专有名词、品牌、技术术语保持原文或使用通用译法
4. 只输出译文，不要添加解释或额外内容

原文：
{text}

译文：
```

**变量说明**：
- `{source_lang}` - 源语言（中文/英文/日文等）
- `{target_lang}` - 目标语言
- `{text}` - 待翻译文本

### 3.2 对照模式 Prompt（图片）

**默认模板**：
```
你是专业翻译助手。请：
1. 识别图片中的所有可见文字
2. 将识别的文字从{source_lang}翻译为{target_lang}

按以下格式输出：
【原文】
<识别的原文>

【译文】
<翻译结果>

要求同纯文本翻译。
```

### 3.3 原位覆盖模式 Prompt（结构化输出）

**默认模板**：
```
你是专业翻译助手。我将提供一段包含多个段落的文本，每段前有 [id] 标记。请将每段从{source_lang}翻译为{target_lang}，按 JSON 数组格式返回。

格式：
[{"id": 1, "text": "译文1"}, {"id": 2, "text": "译文2"}, ...]

原文：
{text}

请直接输出 JSON 数组，不要包含其他内容。
```

**程序追加约束**（自动附加到用户 Prompt 后，用户无法编辑）：
```

【严格要求】
- 必须输出合法 JSON 数组
- 数组元素为对象，包含 id（整数）和 text（字符串）
- id 必须与原文中的 [id] 对应
- 不要输出 Markdown 代码块标记
```

**OCR 段落输入格式**：
```
[1] The quick brown fox
[2] jumps over the lazy dog
[3] Hello World
```

**期望输出**：
```json
[
  {"id": 1, "text": "敏捷的棕色狐狸"},
  {"id": 2, "text": "跳过懒狗"},
  {"id": 3, "text": "你好世界"}
]
```

### 3.4 用户自定义 Prompt

- 用户可完全覆盖默认 Prompt
- 必须包含 `{text}` 变量（纯文本模式）
- 可选包含 `{source_lang}` / `{target_lang}`
- 程序在 UI 中提供「恢复默认」按钮

---

## 4. 流式解析实现

### 4.1 SSE 解析器

```csharp
public async IAsyncEnumerable<string> ParseSseStream(
    Stream stream, 
    [EnumeratorCancellation] CancellationToken ct)
{
    using var reader = new StreamReader(stream, Encoding.UTF8);
    
    while (!ct.IsCancellationRequested)
    {
        var line = await reader.ReadLineAsync(ct);
        if (line == null) break; // 流结束
        
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
            // 忽略无法解析的行（容错）
        }
        finally
        {
            doc?.Dispose();
        }
    }
}
```

### 4.2 原位覆盖模式增量 JSON 解析

```csharp
private readonly StringBuilder _buffer = new();
private readonly Regex _jsonArrayPattern = new(@"\[\s*\{.*\}\s*\]", RegexOptions.Singleline);

public void AppendChunk(string chunk)
{
    _buffer.Append(chunk);
    
    // 尝试提取完整的 JSON 对象
    var match = _jsonArrayPattern.Match(_buffer.ToString());
    if (match.Success)
    {
        try
        {
            var items = JsonSerializer.Deserialize<TranslationItem[]>(match.Value);
            foreach (var item in items)
            {
                OnSegmentCompleted(item.Id, item.Text);
            }
        }
        catch (JsonException) { /* 继续累积 */ }
    }
}

record TranslationItem(int Id, string Text);
```

---

## 5. 网络配置

### 5.1 代理支持

| 类型 | 配置项 | 示例 |
|------|--------|------|
| 不使用代理 | `ProxyType = None` | — |
| 跟随系统代理 | `ProxyType = System` | 自动检测 IE/Edge 代理设置 |
| HTTP 代理 | `ProxyType = Http` | `http://proxy.company.com:8080` |
| SOCKS5 代理 | `ProxyType = Socks5` | `socks5://127.0.0.1:1080` |
| 认证代理 | 附加 `ProxyUser` / `ProxyPassword` | HTTP Basic / SOCKS5 用户名密码 |

**实现**：
```csharp
var handler = new SocketsHttpHandler
{
    Proxy = config.ProxyType switch
    {
        ProxyType.System => WebRequest.GetSystemWebProxy(),
        ProxyType.Http or ProxyType.Socks5 => new WebProxy(config.ProxyUrl)
        {
            Credentials = config.ProxyUser != null 
                ? new NetworkCredential(config.ProxyUser, config.ProxyPassword)
                : null
        },
        _ => null
    },
    UseProxy = config.ProxyType != ProxyType.None,
    ConnectTimeout = TimeSpan.FromSeconds(15)
};

var client = new HttpClient(handler, disposeHandler: true)
{
    Timeout = TimeSpan.FromSeconds(config.Timeout)
};
```

### 5.2 超时配置

| 超时类型 | 默认值 | 说明 |
|----------|--------|------|
| 连接超时 | 15 秒 | TCP 握手 + TLS 握手 |
| 整体请求超时 | 60 秒 | 从发送请求到流结束 |
| 读超时 | 30 秒 | 流式：两次 chunk 之间最大间隔 |

### 5.3 自定义 Header

允许配置额外 HTTP Header（用于某些网关鉴权）：

```json
{
  "extraHeaders": {
    "X-Gateway-Token": "abc123",
    "X-User-Id": "user@company.com"
  }
}
```

**实现时过滤危险 Header**（不允许覆盖）：
- `Authorization`（由 API Key 控制）
- `Content-Type`
- `Content-Length`
- `Host`

---

## 6. 错误处理与分类提示

### 6.1 错误分类

| HTTP 状态码 | 错误类型 | 用户提示 |
|-------------|----------|----------|
| DNS 解析失败 | `NameResolutionFailure` | "无法连接到服务器。请检查接口地址是否正确。" |
| 连接被拒绝 | `ConnectionRefused` | "服务器拒绝连接。请检查接口地址和端口，或检查防火墙设置。" |
| TLS 握手失败 | `SslHandshakeFailed` | "SSL/TLS 连接失败。请检查证书是否有效，或尝试关闭代理。" |
| `401` | `Unauthorized` | "API Key 无效。请检查配置中的 API Key 是否正确。" |
| `403` | `Forbidden` | "无权限访问。请检查 API Key 权限或账户状态。" |
| `404` | `NotFound` | "接口或模型不存在。请检查接口地址和模型名称。" |
| `429` | `RateLimited` | "请求过于频繁或余额不足。请稍后再试或充值。" |
| `500`/`502`/`503` | `ServerError` | "服务器错误。请稍后再试或联系服务提供方。" |
| `504` | `Timeout` | "服务器响应超时。请检查网络连接或增加超时设置。" |
| 连接超时 | `ConnectTimeout` | "连接超时。请检查网络连接和代理设置。" |
| 读超时 | `ReadTimeout` | "服务器响应缓慢。请增加超时设置或检查网络。" |
| JSON 解析失败 | `InvalidResponse` | "服务器返回了无效的响应格式。请检查接口地址是否正确。" |

### 6.2 错误日志记录

```csharp
// 自动脱敏
Log.Error("Translation request failed: {StatusCode} {ReasonPhrase}\nRequest: {Method} {Url}\nHeaders: {Headers}\nBody: {Body}",
    response.StatusCode,
    response.ReasonPhrase,
    request.Method,
    Redact(request.RequestUri),  // 隐藏 URL 中的 Key 参数
    RedactHeaders(request.Headers),  // 脱敏 Authorization
    await RedactBody(request.Content)  // 脱敏请求体中的图片数据
);
```

**脱敏规则**：
- `Authorization: Bearer sk-xxx...` → `Authorization: Bearer sk-***`
- `x-api-key: xxx...` → `x-api-key: ***`
- Base64 图片数据 > 100 字符 → 截断为 `<image data 12345 bytes>`

---

## 7. 测试连接功能

### 7.1 测试流程

1. 发送最小化请求（非流式）：
   ```json
   {
     "model": "{配置的模型名}",
     "messages": [{"role": "user", "content": "Reply with OK"}],
     "max_tokens": 5,
     "stream": false
   }
   ```

2. 解析响应：
   - 成功：显示 ✅ + 耗时 + 模型返回内容（前 50 字符）
   - 失败：显示 ❌ + 分类错误提示（见 6.1） + 原始响应前 500 字符

3. 流式探测（可选）：
   - 如果非流式测试成功，再发一次 `"stream": true` 请求
   - 如果收到 SSE 流 → 标记「支持流式 ✅」
   - 如果失败 → 标记「不支持流式，已自动切换非流式模式」

### 7.2 UI 呈现

**成功状态**：
```
✅ 连接成功（523ms）
模型返回：OK
支持流式：✅
```

**失败状态**：
```
❌ 连接失败：API Key 无效
状态码：401 Unauthorized
建议：请检查配置中的 API Key 是否正确

原始响应：
{"error":{"message":"Incorrect API key provided","type":"invalid_request_error"}}
```

---

## 8. 安全要求

### 8.1 API Key 存储

- **加密算法**：DPAPI（`ProtectedData.Protect`）
- **作用域**：`DataProtectionScope.CurrentUser`
- **Entropy**：安装时生成 32 字节随机值，存储在 `%APPDATA%\Ppinpix\.entropy`
- **格式**：`dpapi:<Base64 密文>`

**加密示例**：
```csharp
public static string Encrypt(string plaintext)
{
    var entropy = LoadOrGenerateEntropy();
    var plainBytes = Encoding.UTF8.GetBytes(plaintext);
    var cipherBytes = ProtectedData.Protect(plainBytes, entropy, DataProtectionScope.CurrentUser);
    return "dpapi:" + Convert.ToBase64String(cipherBytes);
}
```

### 8.2 内存安全

- 解密后的 API Key 仅在请求构造时存在于内存
- 使用后立即清零：`Array.Clear(keyBytes, 0, keyBytes.Length);`
- 不在异常堆栈、日志、崩溃转储中出现明文

### 8.3 日志脱敏

**正则模式**（`Log.cs` 中已实现）：
```csharp
[GeneratedRegex(@"(?i)(authorization|x-api-key)\s*:\s*\w+\s+(\S{3})\S+", RegexOptions.Compiled)]
private static partial Regex AuthHeaderPattern();
// 替换为：$1: $2***

[GeneratedRegex(@"(?i)[""']?api[_-]?key[""']?\s*[:=]\s*[""']?(\S{3})\S+", RegexOptions.Compiled)]
private static partial Regex ApiKeyInJsonPattern();
// 替换为：apiKey: "$1***"
```

### 8.4 导出配置

- 导出时默认不含 API Key（显示为 `<已隐藏>`）
- 用户勾选「包含密钥」后显示确认对话框："导出的文件将包含明文密钥，请妥善保管"
- 导出文件建议加 `.encrypted` 后缀提示

---

## 9. 性能要求

| 指标 | 目标 | 说明 |
|------|------|------|
| 首次连接延迟 | < 2 秒 | DNS + TLS + 首个 chunk |
| 程序处理开销 | < 50ms | OCR → 构造请求 → 发送 |
| 流式渲染延迟 | < 16ms/chunk | 收到 chunk → UI 更新（60fps） |
| 并发翻译 | 支持 3 个同时进行 | 不同窗口可同时翻译 |
| 取消响应时间 | < 500ms | 点击「停止」→ 网络关闭 |

---

## 10. 兼容性测试矩阵

| 接口类型 | 测试项 | 预期 |
|----------|--------|------|
| OpenAI 官方 | 流式/非流式、GPT-4/3.5 | ✅ |
| Azure OpenAI | 需额外 `api-version` Header | ✅ |
| DeepSeek | 流式、多模态 | ✅ |
| Moonshot | 流式 | ✅ |
| 通义千问 | 流式、多模态 | ✅ |
| Ollama 本地 | `http://localhost:11434/v1` | ✅ |
| vLLM | 流式 | ✅ |
| 不规范接口 | SSE 缺少 `[DONE]` | 容错：连接关闭时结束 |
| 不规范接口 | `data:` 后无空格 | 容错：支持 `data:{...}` |
| 不规范接口 | JSON 字段缺失 | 容错：忽略异常 chunk |

---

## 11. 架构约束（零外联保证）

### 11.1 隔离原则

- **只有 `Ppinpix.LlmGateway` 程序集允许引用网络 API**
- 其他程序集（Core/Capture/Pin/Ocr/Translate）禁止引用：
  - `System.Net.Http`
  - `System.Net.Sockets`
  - `System.Net.WebSockets`
  - `System.Net.WebRequest`

### 11.2 架构测试（强制）

```csharp
[Fact]
public void LlmGateway_Should_Be_Only_Assembly_With_Network_Access()
{
    var assemblies = Types.InCurrentDomain()
        .That().ResideInNamespaceStartingWith("Ppinpix")
        .GetTypes();
    
    var violations = assemblies
        .Where(t => t.Assembly.GetName().Name != "Ppinpix.LlmGateway")
        .Where(t => t.GetReferencedTypes().Any(r =>
            r.Namespace?.StartsWith("System.Net") == true &&
            r.Name is not ("IPAddress" or "IPEndPoint"))) // 允许的例外
        .ToList();
    
    Assert.Empty(violations);
}
```

### 11.3 用户授权令牌

```csharp
// 翻译请求必须携带用户授权令牌
public record TranslateRequest(
    string Text,
    string SourceLang,
    string TargetLang,
    UserInitiatedToken AuthToken  // ← 编译时强制
);

// 令牌由 UI 层创建（用户点击按钮时）
public record UserInitiatedToken(DateTime Timestamp, string ActionName);
```

---

## 12. 附录：完整配置 JSON Schema

```json
{
  "llmProfiles": [
    {
      "id": "uuid-1234",
      "name": "默认翻译接口",
      "provider": "OpenAI",
      "baseUrl": "https://api.2020111.xyz/v1",
      "apiKey": "dpapi:c2stYWJjMTIzNDU2Nzg5MA==",
      "model": "翻译",
      "systemPrompt": "你是专业翻译助手...",
      "temperature": 0.3,
      "maxTokens": 2000,
      "timeout": 60,
      "stream": true,
      "supportsVision": false,
      "defaultMode": "OcrThenTranslate",
      "proxy": {
        "type": "Http",
        "url": "http://proxy.company.com:8080",
        "username": null,
        "password": null
      },
      "extraHeaders": {
        "X-Custom-Header": "value"
      },
      "isDefault": true
    }
  ]
}
```

---

## 13. 迁移与版本兼容

- 配置文件带 `"schemaVersion": 1`
- 未来版本新增字段时，旧配置自动填充默认值
- 删除字段时，读取时忽略未知字段
- API Key 格式变化（如改为其他加密算法）时，检测前缀 `dpapi:` / `aes256:` 分别处理

---

## 14. 验收标准

- [ ] 默认配置 `https://api.2020111.xyz/v1` + 模型 `翻译` 可正常翻译
- [ ] 流式输出逐字显示，延迟 < 50ms
- [ ] 非流式降级正常
- [ ] 测试连接准确分类 10 种常见错误
- [ ] API Key 加密存储，日志脱敏
- [ ] HTTP/SOCKS5 代理正常工作
- [ ] 取消翻译 < 500ms 响应
- [ ] 原位覆盖 JSON 增量解析正确
- [ ] ArchTests 通过（仅 LlmGateway 有网络权限）
- [ ] 断网时除翻译外所有功能正常

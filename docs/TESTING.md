# Ppinpix 测试策略与验收清单（v1.0）

> 本文档定义 Ppinpix 的测试策略、测试分级、测试用例清单、验收标准。

---

## 1. 测试策略概览

### 1.1 测试金字塔

```
              /\
             /  \        E2E 测试（10%）
            /────\       - 完整用户流程
           /      \      - 关键场景端到端
          /────────\     
         /          \    集成测试（30%）
        /────────────\   - 模块间交互
       /              \  - API 对接
      /────────────────\ 
     /                  \ 单元测试（60%）
    /────────────────────\ - 纯函数逻辑
   /                      \ - 算法验证
  /________________________\ 
```

### 1.2 测试分类

| 测试类型 | 目标覆盖率 | 工具 | 执行频率 |
|----------|-----------|------|----------|
| **单元测试** | 核心模块 > 80% | xUnit | 每次提交 |
| **集成测试** | 关键路径 100% | xUnit | 每次提交 |
| **架构测试** | 100% | NetArchTest | 每次构建 |
| **UI 测试** | 手动 | — | 发布前 |
| **性能测试** | 关键指标 | BenchmarkDotNet | 发布前 |
| **DPI 测试** | 测试矩阵 | 手动 + 自动截图对比 | 发布前 |
| **隐私测试** | 100% | Wireshark + ArchTests | 发布前 |

---

## 2. 单元测试（Unit Tests）

### 2.1 Ppinpix.Core.Tests

#### 2.1.1 几何计算（Geometry/）

**`PxRectTests.cs`**：
```csharp
[Theory]
[InlineData(10, 20, 100, 50, 10, 20, 110, 70)] // Left, Top, Width, Height → Left, Top, Right, Bottom
public void FromLtwh_ShouldCreateCorrectRect(int l, int t, int w, int h, int el, int et, int er, int eb)
{
    var rect = PxRect.FromLtwh(l, t, w, h);
    Assert.Equal(el, rect.Left);
    Assert.Equal(et, rect.Top);
    Assert.Equal(er, rect.Right);
    Assert.Equal(eb, rect.Bottom);
}

[Theory]
[InlineData(10, 20, 100, 50, 30, 40, true)]  // 点在矩形内
[InlineData(10, 20, 100, 50, 5, 40, false)]   // 点在矩形外
public void Contains_ShouldDetectPointInside(int l, int t, int w, int h, int px, int py, bool expected)
{
    var rect = PxRect.FromLtwh(l, t, w, h);
    var point = new PxPoint(px, py);
    Assert.Equal(expected, rect.Contains(point));
}

[Fact]
public void Intersect_ShouldReturnOverlapArea()
{
    var rect1 = PxRect.FromLtwh(0, 0, 100, 100);
    var rect2 = PxRect.FromLtwh(50, 50, 100, 100);
    var result = rect1.Intersect(rect2);
    Assert.Equal(PxRect.FromLtwh(50, 50, 50, 50), result);
}

[Fact]
public void Union_ShouldReturnBoundingRect()
{
    var rect1 = PxRect.FromLtwh(0, 0, 50, 50);
    var rect2 = PxRect.FromLtwh(100, 100, 50, 50);
    var result = PxRect.Union(rect1, rect2);
    Assert.Equal(PxRect.FromLtwh(0, 0, 150, 150), result);
}
```

**覆盖**：
- [ ] `FromLtwh` / `FromLtrb` / `FromCorners`
- [ ] `Contains` 点、矩形
- [ ] `IntersectsWith`
- [ ] `Intersect` / `Union`
- [ ] `Offset` / `Inflate` / `ClampTo`
- [ ] 边界情况：空矩形、负尺寸

#### 2.1.2 吸附算法（Windowing/）

**`SnapEngineTests.cs`**：
```csharp
[Fact]
public void Snap_ShouldSnapToNearestEdge_WithinThreshold()
{
    var candidates = new[] {
        PxRect.FromLtwh(100, 100, 200, 100) // 目标矩形
    };
    var moving = PxRect.FromLtwh(95, 150, 50, 50); // 移动矩形，左边缘距离目标 5px
    var threshold = 8;
    
    var result = SnapEngine.SnapRect(moving, candidates, threshold);
    
    Assert.True(result.Snapped);
    Assert.Equal(100, result.Rect.Left); // 吸附到目标左边缘
}

[Fact]
public void Snap_ShouldNotSnap_BeyondThreshold()
{
    var candidates = new[] { PxRect.FromLtwh(100, 100, 200, 100) };
    var moving = PxRect.FromLtwh(85, 150, 50, 50); // 距离 15px，超出阈值
    var threshold = 8;
    
    var result = SnapEngine.SnapRect(moving, candidates, threshold);
    
    Assert.False(result.Snapped);
    Assert.Equal(85, result.Rect.Left); // 不变
}

[Theory]
[InlineData(100, 100, 200, 100, 95, 150, 50, 50, 100, 150)] // 吸附左边
[InlineData(100, 100, 200, 100, 305, 150, 50, 50, 300, 150)] // 吸附右边
[InlineData(100, 100, 200, 100, 150, 95, 50, 50, 150, 100)] // 吸附上边
[InlineData(100, 100, 200, 100, 150, 205, 50, 50, 150, 200)] // 吸附下边
public void Snap_ShouldSnapToAllFourEdges(
    int cx, int cy, int cw, int ch, // 候选矩形
    int mx, int my, int mw, int mh, // 移动矩形
    int ex, int ey)                 // 预期位置
{
    var candidates = new[] { PxRect.FromLtwh(cx, cy, cw, ch) };
    var moving = PxRect.FromLtwh(mx, my, mw, mh);
    
    var result = SnapEngine.SnapRect(moving, candidates, 8);
    
    Assert.True(result.Snapped);
    Assert.Equal(ex, result.Rect.Left);
    Assert.Equal(ey, result.Rect.Top);
}
```

**覆盖**：
- [ ] 四边吸附（左/右/上/下）
- [ ] 多候选矩形（选最近）
- [ ] 阈值边界
- [ ] 无候选时不吸附

#### 2.1.3 快捷键解析（Settings/）

**`HotkeyGestureTests.cs`**：
```csharp
[Theory]
[InlineData("F1", HotkeyModifiers.None, 0x70)]
[InlineData("Ctrl+Shift+T", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x54)]
[InlineData("Ctrl+Alt+Delete", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x2E)]
public void TryParse_ShouldParseValidHotkeys(string input, HotkeyModifiers expectedMods, int expectedVk)
{
    var result = HotkeyGesture.TryParse(input, out var gesture);
    
    Assert.True(result);
    Assert.Equal(expectedMods, gesture.Modifiers);
    Assert.Equal(expectedVk, gesture.VirtualKey);
}

[Theory]
[InlineData("")] // 空字符串
[InlineData("InvalidKey")]
[InlineData("Ctrl+")] // 缺少键
[InlineData("+++")]
public void TryParse_ShouldReturnFalse_ForInvalidInput(string input)
{
    var result = HotkeyGesture.TryParse(input, out _);
    Assert.False(result);
}

[Fact]
public void Format_ShouldProduceCorrectString()
{
    var gesture = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x54);
    var formatted = HotkeyFormatter.Format(gesture);
    Assert.Equal("Ctrl+Shift+T", formatted);
}
```

**覆盖**：
- [ ] 所有修饰键组合
- [ ] A-Z、0-9、F1-F12
- [ ] 特殊键（Delete、Insert、Home、End...）
- [ ] 无效输入

#### 2.1.4 设置存储（Settings/）

**`SettingsStoreTests.cs`**：
```csharp
[Fact]
public void SaveAndLoad_ShouldRoundTrip()
{
    var tempFile = Path.GetTempFileName();
    var store = new SettingsStore(tempFile);
    
    var settings = new AppSettings
    {
        CaptureHotkey = HotkeyGesture.Parse("F1"),
        SavePath = @"C:\Screenshots",
        LlmProfiles = new List<LlmProfile>
        {
            new() { Name = "Test", BaseUrl = "https://example.com", ApiKey = "sk-test" }
        }
    };
    
    store.Save(settings);
    var loaded = store.Load();
    
    Assert.Equal("F1", HotkeyFormatter.Format(loaded.CaptureHotkey));
    Assert.Equal(@"C:\Screenshots", loaded.SavePath);
    Assert.Single(loaded.LlmProfiles);
    Assert.Equal("Test", loaded.LlmProfiles[0].Name);
    
    File.Delete(tempFile);
}

[Fact]
public void Load_ShouldReturnDefault_WhenFileNotExists()
{
    var nonExistentFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    var store = new SettingsStore(nonExistentFile);
    
    var settings = store.Load();
    
    Assert.NotNull(settings);
    Assert.Equal("F1", HotkeyFormatter.Format(settings.CaptureHotkey));
}
```

**覆盖**：
- [ ] 保存/加载往返
- [ ] 文件不存在时返回默认值
- [ ] 损坏文件时返回默认值
- [ ] 原子写入（写临时文件→替换）

#### 2.1.5 日志脱敏（Diagnostics/）

**`LogRedactTests.cs`**：
```csharp
[Theory]
[InlineData(
    "Authorization: Bearer sk-abc123456789",
    "Authorization: Bearer sk-a***")]
[InlineData(
    "x-api-key: abc123456789xyz",
    "x-api-key: abc***")]
[InlineData(
    "{\"apiKey\":\"sk-proj-abc123\"}",
    "{\"apiKey\":\"sk-p***\"}")]
public void Redact_ShouldMaskApiKeys(string input, string expected)
{
    var redacted = Log.Redact(input);
    Assert.Equal(expected, redacted);
}

[Fact]
public void Redact_ShouldTruncateLargeBase64Data()
{
    var largeData = "data:image/png;base64," + new string('A', 10000);
    var redacted = Log.Redact(largeData);
    Assert.Contains("<image data", redacted);
    Assert.Contains("bytes>", redacted);
    Assert.DoesNotContain(new string('A', 100), redacted);
}
```

**覆盖**：
- [ ] Authorization Bearer
- [ ] x-api-key
- [ ] JSON 中的 apiKey 字段
- [ ] Base64 图片数据截断

---

### 2.2 Ppinpix.Capture.Tests

#### 2.2.1 选区状态（Session/）

**`SelectionStateTests.cs`**：
```csharp
[Theory]
[InlineData(SelectionHandle.TopLeft, 50, 50, 50, 50, 100, 100)] // 拖动左上角
[InlineData(SelectionHandle.BottomRight, 50, 50, 100, 100, 200, 200)]
public void Resize_ShouldAdjustBounds(
    SelectionHandle handle, int sx, int sy, int sw, int sh,
    int dx, int dy) // 拖动到的位置
{
    var state = new SelectionState(PxRect.FromLtwh(sx, sy, sw, sh));
    state.StartResize(handle);
    state.Resize(new PxPoint(dx, dy));
    
    // 验证新边界
    if (handle == SelectionHandle.TopLeft)
    {
        Assert.Equal(dx, state.Selection.Left);
        Assert.Equal(dy, state.Selection.Top);
    }
    else if (handle == SelectionHandle.BottomRight)
    {
        Assert.Equal(dx, state.Selection.Right);
        Assert.Equal(dy, state.Selection.Bottom);
    }
}

[Fact]
public void HitTestHandle_ShouldDetectHandleProximity()
{
    var state = new SelectionState(PxRect.FromLtwh(100, 100, 200, 100));
    var dpi = new DpiScale(96);
    
    // 8 个手柄位置
    Assert.Equal(SelectionHandle.TopLeft, state.HitTestHandle(new PxPoint(100, 100), dpi));
    Assert.Equal(SelectionHandle.TopRight, state.HitTestHandle(new PxPoint(300, 100), dpi));
    Assert.Equal(SelectionHandle.BottomLeft, state.HitTestHandle(new PxPoint(100, 200), dpi));
    Assert.Equal(SelectionHandle.BottomRight, state.HitTestHandle(new PxPoint(300, 200), dpi));
    
    // 内部
    Assert.Equal(SelectionHandle.None, state.HitTestHandle(new PxPoint(200, 150), dpi));
}
```

**覆盖**：
- [ ] 8 个手柄拖动
- [ ] 手柄命中测试
- [ ] 移动选区
- [ ] 边界限制（不允许负宽高）

#### 2.2.2 输出处理（Output/）

**`CaptureOutputTests.cs`**：
```csharp
[Theory]
[InlineData("Screenshot_{yyyy-MM-dd}_{HH-mm-ss}.png", "Screenshot_2025-01-15_14-30-45.png")]
[InlineData("{yyyy}{MM}{dd}_{HHmmss}.jpg", "20250115_143045.jpg")]
public void ExpandTemplate_ShouldReplaceVariables(string template, string expected)
{
    var timestamp = new DateTime(2025, 1, 15, 14, 30, 45);
    var result = CaptureOutput.ExpandTemplate(template, timestamp);
    Assert.Equal(expected, result);
}

[Fact]
public void Save_ShouldCreatePngFile()
{
    var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(tempDir);
    
    var frame = CreateTestFrame(100, 100, Colors.Red);
    var path = CaptureOutput.Save(frame, tempDir, "test.png");
    
    Assert.True(File.Exists(path));
    Assert.EndsWith("test.png", path);
    
    Directory.Delete(tempDir, true);
}
```

**覆盖**：
- [ ] 文件名模板展开
- [ ] PNG/JPG 编码
- [ ] 文件保存
- [ ] 剪贴板复制（Mock）

---

### 2.3 Ppinpix.Ocr.Tests（M3）

#### 2.3.1 段落重组

**`ParagraphGrouperTests.cs`**：
```csharp
[Fact]
public void Group_ShouldMergeLinesIntoParargraphs()
{
    var lines = new List<TextLine>
    {
        new("Line 1", new Quad(...), 0.99f, []),
        new("Line 2", new Quad(...), 0.99f, []), // 行距小，同段
        new("Title", new Quad(...), 0.99f, []),  // 行距大，新段
    };
    
    var paragraphs = ParagraphGrouper.Group(lines);
    
    Assert.Equal(2, paragraphs.Count);
    Assert.Equal("Line 1\nLine 2", paragraphs[0].Text);
    Assert.Equal("Title", paragraphs[1].Text);
}
```

**覆盖**：
- [ ] 单列文本
- [ ] 双列文本（不混淆）
- [ ] 标题与正文分离
- [ ] 空行处理

---

### 2.4 Ppinpix.LlmGateway.Tests（M4）

#### 2.4.1 SSE 解析

**`SseStreamParserTests.cs`**：
```csharp
[Fact]
public async Task ParseSseStream_ShouldExtractContent()
{
    var sse = @"
data: {""choices"":[{""delta"":{""content"":""Hello""}}]}

data: {""choices"":[{""delta"":{""content"":"" World""}}]}

data: [DONE]
";
    var stream = new MemoryStream(Encoding.UTF8.GetBytes(sse));
    var chunks = new List<string>();
    
    await foreach (var chunk in SseStreamParser.ParseSseStream(stream, CancellationToken.None))
    {
        chunks.Add(chunk);
    }
    
    Assert.Equal(2, chunks.Count);
    Assert.Equal("Hello", chunks[0]);
    Assert.Equal(" World", chunks[1]);
}

[Fact]
public async Task ParseSseStream_ShouldHandleMissingDone()
{
    var sse = @"data: {""choices"":[{""delta"":{""content"":""Test""}}]}
";
    var stream = new MemoryStream(Encoding.UTF8.GetBytes(sse));
    var chunks = new List<string>();
    
    await foreach (var chunk in SseStreamParser.ParseSseStream(stream, CancellationToken.None))
    {
        chunks.Add(chunk);
    }
    
    Assert.Single(chunks);
}
```

**覆盖**：
- [ ] 标准 SSE 格式
- [ ] 缺少 `[DONE]`
- [ ] 无效 JSON 行（容错）
- [ ] 空 content（忽略）

---

## 3. 集成测试（Integration Tests）

### 3.1 截图流程

**`CaptureFlowTests.cs`**：
```csharp
[Fact]
public void CaptureSession_ShouldCreateOverlays()
{
    var displays = DisplayService.GetCurrent();
    var settings = AppSettings.Default;
    
    var session = CaptureSession.Start(settings, displays, new CaptureRequest(), _ => {});
    
    Assert.NotNull(session);
    Assert.True(session.IsFinished == false);
    
    session.Dispose();
}
```

### 3.2 OCR 完整流程（M3）

**`OcrIntegrationTests.cs`**：
```csharp
[Fact]
public async Task OcrService_ShouldRecognizeSimpleText()
{
    var service = new OcrService(new RapidOcrEngine());
    var frame = LoadTestImage("hello_world.png");
    
    var result = await service.RecognizeAsync(frame, CancellationToken.None);
    
    Assert.Contains("Hello", result.Lines.Select(l => l.Text));
    Assert.Contains("World", result.Lines.Select(l => l.Text));
    Assert.True(result.Elapsed < TimeSpan.FromMilliseconds(500));
}
```

### 3.3 翻译流程（M4）

**`TranslateIntegrationTests.cs`**：
```csharp
[Fact]
public async Task TranslateService_ShouldStreamTranslation()
{
    var mockLlm = new MockLlmProvider("敏捷的棕色狐狸");
    var service = new TranslateService(new OcrService(...), mockLlm);
    
    var frame = CreateTestFrame("The quick brown fox");
    var token = new UserInitiatedToken(DateTime.UtcNow, "test");
    var chunks = new List<string>();
    
    await foreach (var chunk in service.TranslateAsync(frame, TranslateOptions.Default, token, CancellationToken.None))
    {
        chunks.Add(chunk);
    }
    
    var fullText = string.Join("", chunks);
    Assert.Contains("狐狸", fullText);
}
```

---

## 4. 架构测试（Architecture Tests）

### 4.1 零外联保证

**`Ppinpix.ArchTests/NetworkIsolationTests.cs`**：
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
        string.Join("\n", result.FailingTypes?.Select(t => t.FullName) ?? []));
}

[Fact]
public void Core_Should_Not_Reference_UI()
{
    var result = Types.InAssembly(typeof(Ppinpix.Core.Geometry.PxRect).Assembly)
        .ShouldNot().HaveDependencyOn("PresentationCore")
        .ShouldNot().HaveDependencyOn("PresentationFramework")
        .GetResult();
    
    Assert.True(result.IsSuccessful);
}
```

---

## 5. 性能测试（Performance Tests）

### 5.1 基准测试

**`Ppinpix.Benchmarks/CaptureBenchmarks.cs`**：
```csharp
[MemoryDiagnoser]
public class CaptureBenchmarks
{
    [Benchmark]
    public void CaptureFullScreen()
    {
        var capture = new GdiScreenCapture();
        var frame = capture.Capture(PxRect.FromLtwh(0, 0, 1920, 1080));
        frame.Dispose();
    }
    
    [Benchmark]
    public void SnapEngine_1000Candidates()
    {
        var candidates = Enumerable.Range(0, 1000)
            .Select(i => PxRect.FromLtwh(i * 10, i * 10, 100, 50))
            .ToArray();
        var moving = PxRect.FromLtwh(500, 500, 50, 50);
        
        var result = SnapEngine.SnapRect(moving, candidates, 8);
    }
}
```

**目标**：
- [ ] 1080p 截图 < 50ms
- [ ] 1000 候选窗口吸附 < 10ms
- [ ] OCR 1080p < 500ms
- [ ] 翻译首字延迟 < 50ms（程序开销）

---

## 6. DPI 测试矩阵

### 6.1 测试配置

| 配置 | 主屏 | 副屏 | 说明 |
|------|------|------|------|
| **A** | 100% | — | 单屏标准 DPI |
| **B** | 125% | — | 单屏常见笔记本 |
| **C** | 150% | — | 单屏高 DPI |
| **D** | 200% | — | 单屏 4K |
| **E** | 100% | 150% | 双屏混合（左右） |
| **F** | 150% | 100% | 双屏混合（主屏高 DPI） |
| **G** | 100% | 150% | 双屏混合（上下） |
| **H** | 150% | — (右侧主屏) | 负坐标主屏 |
| **I** | 100% | 150% | 200% | 三屏混合 |

### 6.2 验收项目

每个配置下验证：
- [ ] 截图覆盖层瞬时显示，无错位
- [ ] 选区边框 1 物理像素（用测量工具验证）
- [ ] 窗口识别高亮准确
- [ ] 放大镜跨屏移动正常
- [ ] 工具栏跨屏移动正常
- [ ] 贴图跨屏拖动大小/清晰度正确
- [ ] Toast 位置正确

### 6.3 自动化对比

```csharp
[Theory]
[InlineData(100, 150)] // 双屏 100% + 150%
public void Screenshot_ShouldHave1PxBorder(int dpi1, int dpi2)
{
    // 1. 截图
    var screenshot = CaptureTestHelper.TakeScreenshot(dpi1, dpi2);
    
    // 2. 提取选区边框
    var border = ExtractBorder(screenshot);
    
    // 3. 测量物理像素宽度
    var thickness = MeasureBorderThickness(border, dpi1);
    
    Assert.Equal(1, thickness); // 必须是 1 物理像素
}
```

---

## 7. 隐私测试（Privacy Tests）

### 7.1 Wireshark 验证

**测试步骤**：
1. 启动 Wireshark 监听所有接口
2. 启动 Ppinpix
3. 执行以下操作（**不触发翻译**）：
   - 截图 20 次
   - 标注并保存 10 次
   - 创建贴图 5 个
   - OCR 识别 10 次
   - 打开/修改设置
   - 退出程序
4. 筛选：`ip.src == <本机IP> && tcp.dstport != (53 or 137 or 138)`

**预期结果**：
- ✅ 无任何 TCP 连接
- ✅ 无任何 HTTP/HTTPS 请求

### 7.2 防火墙验证

**测试步骤**：
1. Windows 防火墙添加出站规则：阻止 `Ppinpix.exe`
2. 启动 Ppinpix
3. 执行 M1-M3 所有功能

**预期结果**：
- ✅ 截图/标注/贴图/OCR 全部正常
- ✅ 无网络错误提示
- ✅ 翻译功能提示网络错误（预期行为）

### 7.3 翻译网络验证

**测试步骤**：
1. 配置翻译接口为本地代理（Fiddler `http://127.0.0.1:8888`）
2. 点击「翻译」
3. 检查代理捕获的请求

**预期结果**：
- ✅ 仅在点击「翻译」时有请求
- ✅ 目标 URL 为用户配置的地址
- ✅ 无其他请求（统计/遥测/更新检查）

---

## 8. 验收清单（Acceptance Checklist）

### 8.1 M1 验收

#### 功能验收
- [ ] `F1` 热键触发截图 < 150ms
- [ ] 窗口/控件识别成功率 > 90%（测试 20 个常见应用）
- [ ] 滚轮切换层级正常
- [ ] 手动拖框吸附边缘（距离 < 8px）
- [ ] 方向键微调 1 物理像素
- [ ] `C` 复制颜色，`Shift` 切换格式
- [ ] 放大镜跟随无延迟
- [ ] `Enter` 复制，`Ctrl+S` 保存正常
- [ ] 设置持久化正常

#### DPI 验收
- [ ] 配置 A-I 全部测试通过
- [ ] 选区边框 1 物理像素（所有 DPI）

#### 隐私验收
- [ ] Wireshark 验证：零网络连接
- [ ] 防火墙验证：阻止后所有功能正常

#### 性能验收
- [ ] 双 4K 屏热键到覆盖层 < 150ms
- [ ] 拖动选区 60fps
- [ ] 常驻内存 < 80MB

#### 稳定性验收
- [ ] 连续截图 1000 次不崩溃
- [ ] 热插拔显示器 50 次不崩溃
- [ ] 改缩放 + 截图 20 次不崩溃

---

### 8.2 M2 验收

#### 功能验收
- [ ] M1 所有功能保持正常
- [ ] 8 种标注工具可用，样式可调
- [ ] 撤销/重做无次数限制（测试 50 次）
- [ ] 标注 50 个对象后仍 60fps
- [ ] 贴图滚轮缩放（10%-1000%）
- [ ] `Ctrl+滚轮` 透明度（10%-100%）
- [ ] 双击折叠/展开
- [ ] 右键菜单所有项功能正常
- [ ] 跨屏拖动贴图无错位
- [ ] 退出保存/恢复贴图（可选功能）

---

### 8.3 M3 验收

#### 功能验收
- [ ] M1-M2 所有功能保持正常
- [ ] 1080p 中英混合 OCR < 500ms
- [ ] OCR 准确率 > 95%（测试 20 张图）
- [ ] 二维码识别成功率 > 98%（测试 50 个码）
- [ ] 贴图文字划选准确率 > 90%
- [ ] 段落重组正确（单列/双列）
- [ ] 完全离线（断网测试）
- [ ] 模型空闲 5 分钟后释放内存

---

### 8.4 M4 / v1.0 验收

#### 功能验收
- [ ] M1-M3 所有功能保持正常
- [ ] 默认接口 `https://api.2020111.xyz/v1` + 模型 `翻译` 可用
- [ ] 流式翻译逐字显示，延迟 < 50ms
- [ ] 修改原文重译正常
- [ ] 停止按钮 < 500ms 响应
- [ ] 配置管理完善（新建/编辑/删除/测试）
- [ ] 测试连接分类 10 种错误准确
- [ ] API Key 加密存储
- [ ] 日志脱敏（无明文 Key）
- [ ] 首次翻译隐私提示

#### 架构验收
- [ ] ArchTests 通过（仅 LlmGateway 有网络权限）
- [ ] Wireshark 验证通过（除翻译外零网络连接）
- [ ] 防火墙验证通过（M1-M3 功能不受影响）

#### 性能验收
- [ ] 翻译首字延迟 < 网络延迟 + 50ms
- [ ] 并发 3 个翻译窗口不崩溃
- [ ] 常驻内存 < 150MB（含 OCR 模型）

#### 稳定性验收
- [ ] 连续翻译 100 次不崩溃
- [ ] 中途取消 50 次不崩溃
- [ ] 网络断开/恢复 20 次不崩溃

---

## 9. 测试环境

### 9.1 硬件配置

| 配置 | 说明 |
|------|------|
| **开发机** | i7-10700 / 16GB / Win 11 / 150% 笔记本屏 |
| **测试机 1** | 外接 1080p 显示器（100%） |
| **测试机 2** | 4K 显示器（200%） |
| **测试机 3** | 混合双屏（100% + 150%） |

### 9.2 软件环境

- Windows 11 23H2
- Windows 10 22H2（最低支持 1809）
- .NET 10 Runtime
- Visual Studio 2022 / Rider 2024.3
- Wireshark 4.x
- Fiddler Classic

---

## 10. 测试报告模板

### 10.1 功能测试报告

```markdown
# Ppinpix M1 功能测试报告

**测试日期**：2025-11-30  
**测试人员**：张三  
**版本号**：v0.1.0-M1  

## 测试环境
- 操作系统：Windows 11 23H2
- DPI 配置：150% 笔记本 + 100% 外接显示器
- 硬件：i7-10700 / 16GB

## 测试结果

| 编号 | 测试项 | 预期结果 | 实际结果 | 状态 |
|------|--------|----------|----------|------|
| TC-01 | 热键触发截图 | < 150ms | 82ms | ✅ 通过 |
| TC-02 | 窗口识别 | > 90% | 93% (28/30) | ✅ 通过 |
| TC-03 | 选区边框 | 1 物理像素 | 1px | ✅ 通过 |
| ... | ... | ... | ... | ... |

## 发现问题
- 无

## 总结
M1 所有功能测试通过，符合发布标准。
```

### 10.2 性能测试报告

```markdown
# Ppinpix M1 性能测试报告

## 测试结果

| 指标 | 目标 | 实际 | 状态 |
|------|------|------|------|
| 热键到覆盖层 | < 150ms | 82ms | ✅ |
| 拖动选区帧率 | ≥ 60fps | 60fps | ✅ |
| 常驻内存 | < 80MB | 65MB | ✅ |
| 1080p 截图 | < 50ms | 38ms | ✅ |

## Benchmark 结果
见附件 `BenchmarkDotNet.Artifacts/*.md`
```

---

## 11. CI/CD 集成

### 11.1 GitHub Actions / Azure DevOps Pipeline

```yaml
name: Ppinpix CI

on: [push, pull_request]

jobs:
  test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      
      - name: Restore dependencies
        run: dotnet restore
      
      - name: Build
        run: dotnet build --configuration Release --no-restore
      
      - name: Run unit tests
        run: dotnet test --no-build --verbosity normal --filter "Category=Unit"
      
      - name: Run architecture tests
        run: dotnet test --no-build --verbosity normal --filter "Category=Architecture"
      
      - name: Publish test results
        uses: dorny/test-reporter@v1
        if: always()
        with:
          name: Test Results
          path: '**/TestResults/*.trx'
          reporter: dotnet-trx
```

---

## 12. 总结

Ppinpix 的测试策略强调：

1. **自动化优先**：单元测试 + 架构测试自动化，CI 强制执行
2. **隐私保证**：Wireshark + 防火墙双重验证零外联
3. **DPI 严格**：所有配置逐项验证，1 物理像素边框不容妥协
4. **性能基准**：BenchmarkDotNet 建立基准，避免回归
5. **分级验收**：每个里程碑有明确验收标准，通过后才进入下一阶段

**目标**：确保 v1.0.0 发布时，功能完整、性能达标、隐私可信、体验流畅。

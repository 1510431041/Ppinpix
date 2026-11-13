# Ppinpix M3 里程碑：纯本地离线 OCR（2-3 周）

> **目标**：集成本地 OCR 引擎，实现完全离线的文字识别、二维码扫描、贴图文字划选功能。

---

## 1. 里程碑概览

**前置依赖**：M2 完成  
**周期**：2-3 周（12 个工作日）  
**交付日期**：2026-01-31（预估）  
**状态**：📋 计划中  

---

## 2. 功能范围

### 2.1 ✅ 包含功能（必须实现）

#### A. OCR 引擎集成
- [ ] **RapidOCR 集成**（PP-OCRv4 ONNX 模型）
- [ ] **中英文混合识别**（默认模型）
- [ ] **可选语言包**（日语/韩语/繁体，独立模型文件）
- [ ] **Windows.Media.Ocr 兜底**（系统自带，可在设置中切换）
- [ ] **模型懒加载**（首次使用时加载，常驻内存）
- [ ] **独立工作线程**（不阻塞 UI）
- [ ] **大图自动缩放**（长边 1600px 优化性能）

#### B. 二维码/条码识别
- [ ] **ZXing.Net 集成**
- [ ] **QR Code / EAN / Code128** 等常见格式
- [ ] **与文字识别同时进行**
- [ ] **结果包含内容、类型、位置**

#### C. OCR 结果面板
- [ ] **弹出窗口显示识别文字**（可编辑）
- [ ] **段落重组**（按行高、行距、对齐聚类）
- [ ] **「复制全部」按钮**
- [ ] **「翻译」按钮**（跳转 M4 功能）
- [ ] **二维码内容显示**（链接不自动打开）
- [ ] **识别耗时显示**

#### D. 贴图文字划选
- [ ] **创建贴图后自动静默 OCR**（可在设置中关闭）
- [ ] **鼠标移到文字区域时光标变 I 型**
- [ ] **拖拽选择文字，高亮显示**
- [ ] **`Ctrl+C` 复制选中文字**
- [ ] **`Ctrl+A` 全选所有文字**
- [ ] **按住 `Space` 拖动贴图**（避免与文字选择冲突）

#### E. OCR 触发入口
- [ ] **截图工具栏「OCR」按钮**
- [ ] **贴图右键「OCR」**
- [ ] **独立热键 `Ctrl+Shift+O`** 截图并 OCR

#### F. 设置页新增
- [ ] **OCR 设置页**：
  - OCR 引擎选择（RapidOCR / Windows.Media.Ocr）
  - 语言包选择（中英 / +日语 / +韩语 / +繁体）
  - 空闲释放模型（默认 5 分钟，0=不释放）
  - 大图缩放阈值（默认 1600px）
- [ ] **贴图设置页补充**：
  - 创建贴图后自动 OCR（默认开启）

#### G. WebP 图片格式支持（可选）
- [ ] **保存为 WebP**（更小体积）
- [ ] **加载 WebP 贴图**

### 2.2 ❌ 不包含功能（后续里程碑）
- ❌ 翻译功能 → **M4**
- ❌ 在线 OCR 接口 → **永不支持**（隐私承诺）
- ❌ 表格识别/公式识别 → **低优先级**

---

## 3. 技术架构

### 3.1 新增模块

```
src/
├── Ppinpix.Ocr/                 ← M3 新建
│   ├── Engines/
│   │   ├── IOcrEngine.cs        OCR 引擎接口
│   │   ├── RapidOcrEngine.cs    RapidOCR 实现（ONNX Runtime）
│   │   └── WindowsOcrEngine.cs  Windows.Media.Ocr 实现
│   │
│   ├── Barcode/
│   │   └── ZxingDecoder.cs      ZXing.Net 封装
│   │
│   ├── Models/
│   │   ├── OcrResult.cs         识别结果
│   │   ├── TextLine.cs          文字行
│   │   ├── CharBox.cs           字符级框
│   │   ├── Barcode.cs           二维码/条码
│   │   └── Paragraph.cs         段落（重组后）
│   │
│   ├── Processing/
│   │   ├── ParagraphGrouper.cs  段落重组（聚类）
│   │   └── ImagePreprocessor.cs 图像预处理（缩放/二值化）
│   │
│   ├── OcrService.cs            OCR 服务（单例，管理引擎生命周期）
│   └── OcrWorker.cs             工作队列（独立线程）
│
├── Ppinpix.Pin/
│   └── TextSelection/           ← M3 新建
│       ├── TextSelectionLayer.xaml  文字选择覆盖层
│       ├── TextHitTest.cs       文字命中测试
│       └── TextHighlight.cs     高亮绘制
│
└── Ppinpix.App/
    └── Ocr/
        └── OcrResultWindow.xaml  ← M3 新建（OCR 结果面板）
```

### 3.2 依赖包

```xml
<!-- Ppinpix.Ocr.csproj -->
<ItemGroup>
  <!-- RapidOCR 依赖 -->
  <PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.17.0" />
  <PackageReference Include="SixLabors.ImageSharp" Version="3.1.0" />
  
  <!-- ZXing.Net -->
  <PackageReference Include="ZXing.Net" Version="0.16.9" />
  
  <!-- Windows OCR（框架自带，仅引用） -->
  <FrameworkReference Include="Microsoft.Windows.SDK.Contracts" />
</ItemGroup>

<!-- 模型文件（随安装包分发） -->
<ItemGroup>
  <Content Include="Models\**\*.onnx" CopyToOutputDirectory="Always" />
</ItemGroup>
```

### 3.3 核心设计

#### OCR 结果模型

```csharp
public record OcrResult(
    IReadOnlyList<TextLine> Lines,
    IReadOnlyList<Paragraph> Paragraphs,
    IReadOnlyList<Barcode> Codes,
    PxSize ImageSize,
    TimeSpan Elapsed
);

public record TextLine(
    string Text,
    Quad Box,              // 四边形框（支持旋转文字）
    float Confidence,
    IReadOnlyList<CharBox> Chars  // 字符级框
);

public record CharBox(
    char Char,
    PxRect Box             // 单个字符的矩形框
);

public record Paragraph(
    int Id,
    string Text,
    PxRect Bounds,
    IReadOnlyList<TextLine> Lines
);

public record Barcode(
    string Content,
    BarcodeType Type,      // QR_CODE / EAN_13 / CODE_128 ...
    PxRect Box
);

public record Quad(PxPoint P1, PxPoint P2, PxPoint P3, PxPoint P4);
```

#### OCR 引擎接口

```csharp
public interface IOcrEngine : IDisposable
{
    string Name { get; }
    bool IsLoaded { get; }
    IReadOnlyList<string> SupportedLanguages { get; }
    
    Task LoadAsync(CancellationToken ct);
    Task<OcrResult> RecognizeAsync(ScreenFrame frame, CancellationToken ct);
}

public class RapidOcrEngine : IOcrEngine
{
    private InferenceSession? _detectionModel;
    private InferenceSession? _recognitionModel;
    
    public async Task LoadAsync(CancellationToken ct)
    {
        var modelPath = Path.Combine(Paths.ModelsDirectory, "ch_PP-OCRv4_det.onnx");
        _detectionModel = new InferenceSession(modelPath);
        
        var recPath = Path.Combine(Paths.ModelsDirectory, "ch_PP-OCRv4_rec.onnx");
        _recognitionModel = new InferenceSession(recPath);
    }
    
    public async Task<OcrResult> RecognizeAsync(ScreenFrame frame, CancellationToken ct)
    {
        // 1. 检测文字区域（DBNet）
        var regions = await DetectTextRegions(frame, ct);
        
        // 2. 识别每个区域（CRNN）
        var lines = new List<TextLine>();
        foreach (var region in regions)
        {
            var cropped = frame.Crop(region.Box);
            var text = await RecognizeText(cropped, ct);
            lines.Add(new TextLine(text, region.Box, region.Confidence, []));
        }
        
        // 3. 重组段落
        var paragraphs = ParagraphGrouper.Group(lines);
        
        return new OcrResult(lines, paragraphs, [], frame.Size, elapsed);
    }
}
```

#### 段落重组算法

```csharp
public static class ParagraphGrouper
{
    public static IReadOnlyList<Paragraph> Group(IReadOnlyList<TextLine> lines)
    {
        // 1. 按 Y 坐标排序
        var sorted = lines.OrderBy(l => l.Box.TopLeft.Y).ToList();
        
        // 2. 按行高和行距聚类
        var paragraphs = new List<List<TextLine>>();
        var current = new List<TextLine> { sorted[0] };
        
        for (int i = 1; i < sorted.Count; i++)
        {
            var prev = sorted[i - 1];
            var line = sorted[i];
            
            var prevHeight = prev.Box.Height;
            var gap = line.Box.TopLeft.Y - prev.Box.BottomLeft.Y;
            
            // 行距 < 0.5 倍行高 → 同一段落
            if (gap < prevHeight * 0.5)
            {
                current.Add(line);
            }
            else
            {
                paragraphs.Add(current);
                current = new List<TextLine> { line };
            }
        }
        paragraphs.Add(current);
        
        // 3. 生成段落对象
        return paragraphs.Select((p, i) => new Paragraph(
            Id: i + 1,
            Text: string.Join("\n", p.Select(l => l.Text)),
            Bounds: PxRect.Union(p.Select(l => l.Box.BoundingRect)),
            Lines: p
        )).ToList();
    }
}
```

#### 贴图文字选择

```csharp
public class TextSelectionLayer : FrameworkElement
{
    private OcrResult? _ocrResult;
    private List<CharBox> _selectedChars = new();
    private PxPoint _selectionStart;
    private bool _isSelecting;
    
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = e.GetPosition(this).ToPx(_dpi);
        
        // 命中测试
        var hitChar = _ocrResult?.Lines
            .SelectMany(l => l.Chars)
            .FirstOrDefault(c => c.Box.Contains(pos));
        
        Cursor = hitChar != null ? Cursors.IBeam : Cursors.Arrow;
        
        if (_isSelecting && _ocrResult != null)
        {
            // 按阅读顺序选择字符
            _selectedChars = SelectCharsBetween(_selectionStart, pos, _ocrResult);
            InvalidateVisual();
        }
    }
    
    private List<CharBox> SelectCharsBetween(PxPoint start, PxPoint end, OcrResult ocr)
    {
        var startChar = FindNearestChar(start, ocr);
        var endChar = FindNearestChar(end, ocr);
        
        // 按行顺序、从左到右选择
        var allChars = ocr.Lines
            .OrderBy(l => l.Box.TopLeft.Y)
            .SelectMany(l => l.Chars.OrderBy(c => c.Box.Left))
            .ToList();
        
        var startIdx = allChars.IndexOf(startChar);
        var endIdx = allChars.IndexOf(endChar);
        
        if (startIdx > endIdx) (startIdx, endIdx) = (endIdx, startIdx);
        
        return allChars.Skip(startIdx).Take(endIdx - startIdx + 1).ToList();
    }
    
    protected override void OnRender(DrawingContext dc)
    {
        // 绘制高亮（半透明 Accent）
        var brush = new SolidColorBrush(Tokens.Accent) { Opacity = 0.18 };
        foreach (var c in _selectedChars)
        {
            dc.DrawRectangle(brush, null, c.Box.ToDip(_dpi));
        }
    }
}
```

---

## 4. 开发任务清单

### Week 1（Day 1-6）：OCR 引擎集成

#### Day 1-2：RapidOCR 集成
- [ ] 创建 `Ppinpix.Ocr` 项目
- [ ] 下载 RapidOCR ONNX 模型：
  - [ ] `ch_PP-OCRv4_det.onnx`（检测，~3.5MB）
  - [ ] `ch_PP-OCRv4_rec.onnx`（识别，~9MB）
  - [ ] `ppocr_keys_v1.txt`（字典，~6KB）
- [ ] 放置到 `Models/` 目录
- [ ] 实现 `RapidOcrEngine.cs`：
  - [ ] `LoadAsync` 加载模型到 ONNX Runtime
  - [ ] `DetectTextRegions` DBNet 检测
  - [ ] `RecognizeText` CRNN 识别
  - [ ] CTC 解码（时间步 → 字符序列）
- [ ] 单元测试：识别简单文字图片

**验收**：
- ✅ 加载模型成功（< 2 秒）
- ✅ 识别 "Hello World" 准确率 100%

#### Day 3：Windows OCR 兜底
- [ ] 实现 `WindowsOcrEngine.cs`：
  - [ ] 使用 `Windows.Media.Ocr.OcrEngine`
  - [ ] `GetAvailableLanguages` 检测系统语言包
  - [ ] `RecognizeAsync` 识别
  - [ ] 转换结果格式为 `OcrResult`
- [ ] 设置页选择引擎：
  - [ ] 单选框：RapidOCR（推荐）/ Windows OCR
  - [ ] 显示支持的语言列表

**验收**：
- ✅ Windows OCR 可用（依赖系统语言包）
- ✅ 两种引擎结果格式一致

#### Day 4：二维码识别
- [ ] 实现 `ZxingDecoder.cs`：
  - [ ] 使用 `ZXing.BarcodeReader`
  - [ ] 识别 QR Code / EAN / Code128
  - [ ] 返回 `Barcode` 结果
- [ ] 集成到 OCR 流程：
  - [ ] `OcrService.RecognizeAsync` 同时跑 OCR + 二维码
  - [ ] 并行执行（`Task.WhenAll`）
- [ ] 单元测试：识别标准二维码

**验收**：
- ✅ 识别 QR 码成功率 > 98%
- ✅ 二维码识别不影响 OCR 性能

#### Day 5：段落重组
- [ ] 实现 `ParagraphGrouper.cs`：
  - [ ] 按行高、行距聚类
  - [ ] 按左对齐/居中/右对齐分组
  - [ ] 生成段落 ID 和合并文本
- [ ] 测试复杂排版（多列、标题、正文）

**验收**：
- ✅ 单列文本段落重组正确
- ✅ 双列排版不混淆

#### Day 6：OCR 服务与工作队列
- [ ] 实现 `OcrService.cs`（单例）：
  - [ ] 管理引擎实例（懒加载）
  - [ ] `RecognizeAsync(ScreenFrame)` 公开接口
  - [ ] 空闲 5 分钟后释放模型（可配置）
- [ ] 实现 `OcrWorker.cs`：
  - [ ] 独立后台线程
  - [ ] 请求队列（`BlockingCollection`）
  - [ ] 防止 UI 线程阻塞
- [ ] 大图预处理：
  - [ ] 长边 > 1600px 时缩放
  - [ ] 保持宽高比

**验收**：
- ✅ OCR 不阻塞 UI
- ✅ 空闲 5 分钟后模型释放（内存降低）

---

### Week 2（Day 7-10）：OCR 结果面板与贴图划选

#### Day 7：OCR 结果窗口
- [ ] 创建 `OcrResultWindow.xaml`：
  - [ ] 宽 480px，深色主题
  - [ ] 上半部：识别文字（`TextBox`，多行，可编辑）
  - [ ] 下半部：二维码内容（只读，可点击显示）
  - [ ] 底栏：「复制全部」「翻译」按钮 + 耗时显示
- [ ] 实现 `OcrResultWindow.xaml.cs`：
  - [ ] 绑定 `OcrResult` 数据
  - [ ] 「复制全部」→ `Clipboard.SetText`
  - [ ] 「翻译」→ 跳转翻译功能（M4）
  - [ ] 二维码内容显示（不自动打开链接）
- [ ] 窗口样式：
  - [ ] 置顶、可拖动
  - [ ] 8px 圆角、阴影

**验收**：
- ✅ 识别文字可编辑、复制
- ✅ 二维码内容正确显示

#### Day 8：截图 OCR 集成
- [ ] 工具栏「OCR」按钮启用（`Features.Ocr = true`）
- [ ] 点击后：
  - [ ] 显示加载提示（Toast "正在识别…"）
  - [ ] 调用 `OcrService.RecognizeAsync`
  - [ ] 弹出 `OcrResultWindow` 显示结果
  - [ ] 失败时 Toast 错误提示
- [ ] 独立热键 `Ctrl+Shift+O`：
  - [ ] 截图 → 立即 OCR → 显示结果
- [ ] 贴图右键菜单「OCR」项：
  - [ ] 对贴图图片进行 OCR
  - [ ] 弹出结果窗口

**验收**：
- ✅ 所有入口 OCR 功能正常
- ✅ 1080p 截图 < 500ms 出结果

#### Day 9-10：贴图文字划选
- [ ] 创建 `TextSelectionLayer.xaml`：
  - [ ] 透明覆盖层，覆盖整个贴图
  - [ ] 绘制高亮选区（半透明 Accent）
- [ ] 实现文字命中测试：
  - [ ] `OnMouseMove` 检测光标位置
  - [ ] 命中字符框时光标变 I 型
  - [ ] 非文字区域光标变箭头
- [ ] 实现文字选择：
  - [ ] `OnMouseDown` 记录起点
  - [ ] `OnMouseMove` 按阅读顺序选择字符
  - [ ] `OnMouseUp` 完成选择
  - [ ] `Ctrl+A` 全选
- [ ] 实现复制：
  - [ ] `Ctrl+C` 复制选中文字
  - [ ] 右键菜单「复制选中文字」
- [ ] 集成到 `PinWindow`：
  - [ ] 创建贴图后自动 OCR（后台静默）
  - [ ] OCR 完成后添加 `TextSelectionLayer`
  - [ ] 设置项控制是否自动 OCR
- [ ] 拖动冲突处理：
  - [ ] 在非文字区域拖动 → 移动贴图
  - [ ] 按住 `Space` 拖动 → 强制移动贴图

**验收**：
- ✅ 鼠标移到文字区域光标变 I 型
- ✅ 拖拽选择文字高亮正确
- ✅ 复制文字内容正确
- ✅ 按住 Space 可强制拖动贴图

---

### Week 3（Day 11-12）：测试、优化、文档

#### Day 11：性能优化与测试
- [ ] **性能测试**：
  - [ ] 1080p 中英文混合 < 500ms
  - [ ] 4K 图片 < 1500ms（自动缩放后）
  - [ ] 二维码识别 < 100ms
- [ ] **性能优化**：
  - [ ] ONNX Runtime 使用 CPU 并行（设置 `IntraOpNumThreads`）
  - [ ] 图像预处理优化（减少内存拷贝）
  - [ ] 模型常驻内存（避免重复加载）
- [ ] **准确率测试**：
  - [ ] 中文准确率 > 95%（常见字体）
  - [ ] 英文准确率 > 98%
  - [ ] 二维码成功率 > 98%
- [ ] **稳定性测试**：
  - [ ] 连续 OCR 100 次不崩溃
  - [ ] 模型加载/释放 50 次无内存泄漏
  - [ ] 大图（8K）不崩溃

**验收**：
- ✅ 性能指标达标
- ✅ 准确率达标
- ✅ 稳定性测试通过

#### Day 12：文档与发布
- [ ] 更新 `README.md`（M3 OCR 功能说明）
- [ ] 更新 `CHANGELOG.md`（v0.1.0-M3）
- [ ] 录制 OCR 演示视频
- [ ] 编写《OCR 已知限制》：
  - 手写识别效果差（可用图片翻译模式 M4）
  - 艺术字/变形字识别困难
  - 复杂背景影响准确率
- [ ] 语言包说明文档（如何添加日语/韩语）
- [ ] 打包发布 `Ppinpix-v0.1.0-M3-win-x64.zip`

---

## 5. 可选功能：WebP 支持

### 5.1 任务
- [ ] 引入 `libwebp` 或 `SixLabors.ImageSharp.Formats.Webp`
- [ ] `CaptureOutput` 增加 WebP 编码
- [ ] `PinWindow` 支持加载 WebP
- [ ] 设置页增加「WebP 图片格式」选项

### 5.2 验收
- ✅ 保存为 WebP，体积比 PNG 小 30-50%
- ✅ 贴图可正常显示 WebP

---

## 6. 风险与应对

| 风险 | 概率 | 影响 | 应对措施 |
|------|------|------|----------|
| ONNX Runtime 依赖大（~50MB） | 高 | 低 | 自包含发布，可接受 |
| RapidOCR 模型准确率不达标 | 中 | 高 | 提供 Windows OCR 兜底 |
| 字符级框推算不准确 | 中 | 中 | 降级为行级划选 |
| 段落重组复杂排版错乱 | 中 | 低 | 提供原始行文本备用 |
| 贴图文字划选性能差 | 低 | 中 | 限制 OCR 字符数（> 1000 字符禁用划选） |

---

## 7. 验收标准（M3 交付门槛）

### 7.1 必须通过（P0）
- [ ] ✅ M1-M2 所有功能保持正常
- [ ] ✅ 1080p 中英混合 OCR < 500ms，准确率 > 95%
- [ ] ✅ 二维码识别成功率 > 98%
- [ ] ✅ 贴图文字划选准确率 > 90%
- [ ] ✅ 完全离线（断网可用）
- [ ] ✅ 零网络连接（Wireshark 验证）

### 7.2 应该通过（P1）
- [ ] ✅ 段落重组正确（单列/双列）
- [ ] ✅ 模型空闲释放内存正常
- [ ] ✅ OCR 结果面板功能完整

### 7.3 可选通过（P2）
- [ ] WebP 格式支持
- [ ] 日语/韩语语言包
- [ ] 表格识别

---

## 8. 下一步（M4 预告）

M3 完成后，启动 M4：
- 大模型翻译功能
- API 配置面板（多套配置）
- API Key DPAPI 加密
- 测试连接功能
- 翻译对照浮窗（流式输出）
- 翻译原位覆盖（可选）
- 零外联架构验证

**预计工期**：3-4 周  
**交付时间**：2026-02-28  
**版本号**：v1.0.0（完整版）

---

## 9. 成功标准

M3 成功的标志：
1. ✅ OCR 准确率和性能达到日常可用水平
2. ✅ 完全离线，断网下所有功能正常
3. ✅ 贴图文字划选体验流畅
4. ✅ 内部至少 15 人试用反馈正面
5. ✅ 无隐私问题（零外联承诺保持）

达到以上标准后，宣布 **M3 完成**，进入 M4 最终里程碑开发。

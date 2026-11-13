# Ppinpix 设计方案（v0.1）

> 内部免费的 Windows 桌面截图 / 贴图 / 离线 OCR / 大模型翻译工具，对标 PixPin。
> 设计原则：**快**（热键到出框 < 150ms）、**准**（混合 DPI 下像素级对齐）、**静**（除用户主动发起的翻译请求外零外联）。

---

## 1. 技术选型

| 项 | 选择 | 理由 |
|---|---|---|
| 语言 / 运行时 | **C# 13 / .NET 10**（自包含发布 + ReadyToRun） | 团队效率高；Win32 / WinRT 互操作完善；DPAPI、HttpClient 流式原生支持 |
| UI | **WPF + 大量 Win32 互操作**（覆盖层、贴图窗口直接控制窗口样式） | PerMonitorV2 支持成熟；设置页等常规 UI 开发快 |
| 截屏 | **GDI BitBlt + CAPTUREBLT**（M1 实现），M2+ 可选 DXGI Desktop Duplication | 兜底方案稳定；DXGI 后续优化（HDR/硬件加速窗口不黑屏） |
| 渲染 | **WPF 直接渲染**（M1），覆盖层用 WriteableBitmap + WPF Shape | 快速实现；4K×多屏优化留 M2（可选 Direct2D） |
| 图像格式 | **PNG（M1）**，WebP 延迟到 M3 | 标准库支持好 |
| OCR | **RapidOCR（PP-OCRv4 ONNX）+ ONNX Runtime CPU**（M3），可选 DirectML | 纯离线、中英日韩效果好、模型 ~15MB 随包分发 |
| 二维码 | **ZXing.Net**（M3），可选叠加 OpenCV WeChat QR 提高小码识别率 | 离线 |
| 密钥加密 | **DPAPI（CurrentUser）+ 应用级 entropy** | 绑定 Windows 账户，拷走配置文件无法解密 |
| 翻译 API | 默认 **`https://api.2020111.xyz/v1`**，模型 `"翻译"`（M4） | 用户自行配置 API Key；OpenAI 兼容协议 |
| 安装 | MSIX / Inno Setup 二选一，另提供绿色版 zip | 内部分发 |

> 备选：若后续必须支持 macOS，改用 C++ / Qt 6（同样支持混合 DPI），本文的模块划分与数据流不变。

---

## 2. 总体架构

```
┌─────────────────────────── Ppinpix.exe (单进程, PerMonitorV2) ───────────────────────────┐
│                                                                                           │
│  App Shell ── 托盘 / 全局热键 / 单实例 / 设置窗口 / 启动项                                     │
│      │                                                                                    │
│      ├── Capture       截屏、窗口/控件识别、覆盖层、放大镜、标注编辑器                           │
│      ├── Pin           贴图窗口管理（置顶/缩放/透明/折叠/穿透/文字划选）                           │
│      ├── Ocr           本地 OCR + 二维码（独立工作线程，模型懒加载）                               │
│      ├── Translate     翻译编排：模式选择 → 构造请求 → 流式解析 → 对照浮窗 / 原位覆盖               │
│      │     └── LlmGateway   ★ 全程序唯一允许发起网络请求的组件                                  │
│      └── Core          坐标/DPI、图像缓冲、配置存储、密钥保护、日志（脱敏）                        │
└───────────────────────────────────────────────────────────────────────────────────────────┘
```

解决方案结构：

```
src/
  Ppinpix.App/          托盘、热键、设置 UI、组合根
  Ppinpix.Core/         Geometry, Dpi, ImageBuffer, Settings, SecretStore, Logging
  Ppinpix.Capture/      ScreenGrabber, WindowSnapper, Overlay, Magnifier, Annotation/*
  Ppinpix.Theme/        Tokens.cs, Theme.xaml, ThemeLoader, UiFactory (视觉规范实现)
  Ppinpix.Pin/          PinWindow, PinManager, TextSelectionLayer (M2)
  Ppinpix.Ocr/          RapidOcrEngine, QrDecoder, OcrResult (M3)
  Ppinpix.Translate/    TranslateService, Prompting, Renderers(Compare/Overlay) (M4)
  Ppinpix.LlmGateway/   Providers(OpenAI/Anthropic/Gemini), Sse, ProxyFactory, ConnectionTester (M4)
tests/
  Ppinpix.*.Tests/
  Ppinpix.ArchTests/    ★ 断言只有 LlmGateway 引用 System.Net.Http / Sockets
```

---

## 3. 高分屏与多显示器（贯穿所有模块的基础）

**统一坐标系：虚拟桌面物理像素（Physical Pixel, PX）。** 所有截图、选区、窗口识别、标注、贴图位置一律以 PX 存储；只有 UI 控件（工具栏、菜单、字号）按所在显示器的 DPI 转为 DIP。

1. 进程清单声明 `PerMonitorV2`，启动即 `SetProcessDpiAwarenessContext`。
2. `MonitorService` 枚举显示器：`{HMONITOR, BoundsPx, WorkAreaPx, Dpi, Scale}`，监听 `WM_DISPLAYCHANGE` / `WM_DPICHANGED` 刷新。
3. **截图覆盖层 = 每个显示器一个无边框置顶窗口**，各自按本屏 DPI 创建，背景为该屏冻结帧。选区在 PX 坐标中统一维护，跨屏选区由各屏窗口分别绘制自己的交集部分 → 125% + 150% + 100% 混排时边线无缝。
4. 工具栏、放大镜跟随鼠标所在屏的 Scale 缩放，跨屏时实时重排。
5. 贴图默认 **1 图像像素 = 1 物理像素**（与截取时看到的大小一致）；拖到不同缩放屏时处理 `WM_DPICHANGED`，可在设置里选「保持物理像素」或「保持视觉尺寸」。
6. 输出图片保留原始 PX 分辨率，复制到剪贴板同时放 `CF_DIB` 和 `PNG` 格式。

测试矩阵：单屏 100/150/200%；双屏 100%+150%（左右/上下/负坐标主屏在右）；三屏混排；运行中改缩放、热插拔显示器。

---

## 4. 模块一：智能截图与标注

### 4.1 唤醒流程（目标 < 150ms 出框）

```
热键 → 立即 DXGI 抓全部屏帧（冻结画面）
     ├─ 并行：EnumWindows 抓顶层窗口矩形快照（Z 序）
     ├─ 并行：后台线程预取 UI Automation 控件树（鼠标所在窗口优先）
     └─ 显示各屏覆盖层（暗化遮罩 + 冻结帧）
```

- **全局热键**：`RegisterHotKey`；设置页录制组合键，注册失败即提示冲突并给出占用说明。默认：`F1` 截图、`F3` 贴剪贴板、`Ctrl+Shift+T` 截图翻译、`Ctrl+Shift+O` 截图 OCR。
- 可选「捕获前延迟 N 秒」与「捕获鼠标指针」。

### 4.2 窗口 / 控件边缘吸附

- 层级：**窗口 → 子窗口 → UIA 控件（按钮、输入框、列表项）**。鼠标悬停时高亮最内层命中元素；滚轮 / `Tab` 在层级间上下切换。
- 窗口矩形用 `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` 去掉 Win10/11 的隐形阴影边框；过滤 cloaked、不可见、自身覆盖层窗口。
- UIA 用 `ElementFromPoint` + 预取缓存，超时 30ms 即降级为窗口级，绝不阻塞 UI 线程。
- 手动拖框时边缘距离已知矩形 < 8px（按 DIP 换算）自动磁吸，按住 `Alt` 临时关闭吸附。
- 选区确定后可拖拽 8 个手柄微调，方向键 1px 移动、`Shift+方向键` 1px 调整尺寸。

### 4.3 像素放大镜 / 取色

- 跟随光标的 Direct2D 小窗：以光标为中心 15×15 像素放大 8 倍，像素网格 + 中心十字；下方显示坐标（PX）、选区尺寸、`RGB` / `HEX`。
- `C` 复制当前颜色（`Shift` 切换 HEX/RGB 格式）；靠近屏幕边缘时自动翻转到另一侧。

### 4.4 标注编辑器

工具：矩形 / 椭圆、箭头、直线 / 画笔、**递增序号**（①②③，可设起始值与样式，删除后可选择自动重排）、文字（多行、描边、背景）、**马赛克 / 高斯模糊**（矩形或画笔区域）、荧光笔、橡皮擦。

- 对象模型：每个标注是一个 `Shape`（几何 + 样式），存于有序列表，可再次选中、移动、改样式。
- 马赛克/模糊始终从**原始冻结帧**采样计算，不会叠加已画的标注，也不会因多次修改而劣化。
- **撤销/重做**：命令模式（`AddShape / ModifyShape / DeleteShape / ResizeSelection`），`Ctrl+Z / Ctrl+Y`，无次数上限（内存中仅存几何差量）。
- 工具栏动作：复制、保存（PNG/JPG/WebP，可配命名模板）、**贴图**、**OCR**、**翻译**、取消。`Enter` 复制、`Ctrl+S` 保存、`Ctrl+T` 贴图、`Esc` 逐级退出。

---

## 5. 模块二：悬浮贴图（Pin）

窗口样式：`WS_POPUP | WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_TOOLWINDOW`（不进任务栏），内容用 D2D 绘制，带 1px 描边 + 可选阴影。

| 功能 | 交互 |
|---|---|
| 来源 | 截图工具栏「贴图」；`F3` 贴剪贴板（图片 / 文本→渲染为图片 / 文件路径中的图片） |
| 移动 | 左键拖动 |
| 缩放 | 滚轮（10%~1000%，以光标为中心），显示比例提示；`Ctrl+0` 还原 |
| 透明度 | `Ctrl+滚轮`（10%~100%） |
| 折叠 | 双击折叠为缩略标题条 / 再双击展开；`H` 隐藏，托盘菜单「显示所有贴图」 |
| 鼠标穿透 | 切换 `WS_EX_TRANSPARENT`。穿透后窗口不再接收输入，因此通过 **全局热键** 或 **托盘菜单** 或「按住 `Ctrl+Alt` 时临时解除穿透」（低级键盘钩子仅在存在穿透贴图时安装）恢复 |
| 其它 | 右键菜单：复制、保存、编辑标注、OCR、**翻译**、旋转/翻转、置顶开关、关闭；`Esc` 关闭 |

`PinManager` 负责所有贴图的生命周期、贴图组隐藏/显示，可选「退出时保存贴图，下次启动恢复」（仅本地）。

### 5.1 贴图上直接划选文字

- 贴图创建后后台静默跑 OCR（可在设置关闭），结果为行框 + **字符级框**（从 CTC 解码的时间步位置推算每个字符的横向区间，比按宽度均分更准）。
- 鼠标移到文字上光标变 I 形；拖拽按阅读顺序选中字符，绘制半透明高亮；`Ctrl+C` / 右键「复制选中文字」；`Ctrl+A` 全选。
- 文字层随缩放/旋转做同一变换，确保选区与图像对齐。
- 拖动贴图改为「在非文字区域拖动」或按住 `Space` 拖动，避免与划选冲突。

---

## 6. 模块三：纯本地离线 OCR

- **引擎**：RapidOCR 流水线 `DBNet 检测 → 方向分类 → CRNN/SVTR 识别`，ONNX Runtime 推理；模型随安装包分发，**不做任何在线下载**。可选语言包（日、韩、繁体）作为额外本地模型文件。
- **兜底**：Windows 自带 `Windows.Media.Ocr`（离线，依赖系统语言包），设置中可切换。
- **二维码/条码**：ZXing.Net 同时扫描，结果与文字一起返回。
- **结果模型**：
  ```csharp
  record OcrResult(IReadOnlyList<TextLine> Lines, IReadOnlyList<Barcode> Codes, Size ImageSizePx);
  record TextLine(string Text, Quad Box, float Confidence, IReadOnlyList<CharBox> Chars);
  ```
- **段落重组**：按行高、行距、左对齐聚类为段落，供翻译和复制使用（避免逐行翻译割裂语义）。
- **性能**：模型首次使用时懒加载并常驻；单独 OCR 工作线程 + 队列；大图按 1600px 长边缩放检测、原图裁剪识别。目标：1080p 截图 < 500ms（主流 CPU）。
- OCR 结果面板：可编辑文本框、复制全部、二维码内容（链接只显示不自动打开）。

---

## 7. 模块四：自定义大模型截图翻译

### 7.1 触发入口

1. 截图工具栏「翻译」按钮；
2. 贴图右键「翻译」；
3. 独立热键 `Ctrl+Shift+T`：直接进入截图选区 → 松开即翻译；
4. OCR 结果面板「翻译」。

### 7.2 两种工作模式（每套 API 配置可单独设定）

| 模式 | 流程 | 适用 |
|---|---|---|
| **先本地识字再翻译**（默认） | 本地 OCR → 段落重组 → 只把**文字**发给模型 | 省 token、快、隐私更好；任何纯文本模型可用 |
| **直接发图翻译** | 将截图（PNG/JPEG，长边≤2048）发给多模态模型 | 复杂排版、手写、艺术字、OCR 难识别场景 |

### 7.3 语言策略

- 默认「自动互译」：本地按字符统计判定源语言——中文为主 → 译成英文，否则 → 译成中文。
- 工具条上可切换源/目标语言（中、英、日、韩、法、德、西、俄……可配置列表），记住上次选择。

### 7.4 呈现方式 A：原文/译文上下对照浮窗

```
┌──────────── 翻译 · GPT-4o-mini ▾ · 自动 → 中文 ▾ ─── 📌 ✕ ┐
│ 原文（可编辑）                                         │
│ ┌──────────────────────────────────────────────────┐   │
│ │ The quick brown fox ...                          │   │
│ └──────────────────────────────────────────────────┘   │
│                                   [重新翻译 Ctrl+Enter]│
│ 译文（流式输出中…▍）                                    │
│ ┌──────────────────────────────────────────────────┐   │
│ │ 敏捷的棕色狐狸……                                  │   │
│ └──────────────────────────────────────────────────┘   │
│ [复制译文] [复制原文] [复制双语]       ⏹ 停止 · 1.2s   │
└────────────────────────────────────────────────────────┘
```

- 浮窗置顶、可拖动、可钉住；出现在选区附近且不越出当前显示器工作区。
- 译文 **SSE 流式逐字追加**；可随时「停止」（取消 `CancellationToken`）。
- 修改原文后 `Ctrl+Enter` 重译；可在标题栏下拉切换配置/模型后重译，便于对比。
- 直接发图模式下，原文区显示模型返回的原文（提示词要求模型先输出原文再输出译文，用分隔标记切分）。

### 7.5 呈现方式 B：原图原位排版覆盖

1. 需要文字位置 → 始终先跑本地 OCR 得到段落框（直接发图模式下也如此：图发给模型，同时附带段落 id 列表请模型按 id 返回译文）。
2. 请求模型返回结构化 JSON：`[{"id":3,"text":"…"}]`，按 id 流式增量解析，**每完成一段就立刻覆盖一段**。
3. 覆盖绘制：
   - 背景：取段落框外沿一圈像素的中位数色填充（复杂背景退化为对框内做强模糊 + 半透明底色）；
   - 文字颜色：框内像素 2-means 聚类，取与背景对比度更高的簇；
   - 字号：二分搜索最大可放入框内的字号（允许换行，最小字号下限，溢出时允许框向下扩展）。
4. 结果以新贴图形式呈现（或替换当前贴图内容），按住 `Alt` 或点工具按钮可**临时显示原图对比**；支持复制/保存覆盖后的图片。

### 7.6 提示词

- 每套配置有自定义 Prompt，支持变量：`{source_lang}` `{target_lang}` `{text}` `{format_hint}`。
- 内置默认模板（对照模式 / 覆盖模式 JSON 输出 / 直接发图各一份），用户 Prompt 为空时使用默认。
- 覆盖模式的 JSON 格式约束由程序追加在用户 Prompt 之后，用户改 Prompt 不会破坏解析。

---

## 8. 大模型 API 配置面板

### 8.1 配置项（可添加多套，一键切换「当前默认」）

| 字段 | 说明 |
|---|---|
| 名称 | 如「公司网关-GPT4o」「DeepSeek」 |
| 协议类型 | **OpenAI 兼容**（覆盖 OpenAI、DeepSeek、通义、Moonshot、Ollama、vLLM、各类网关）/ Anthropic Messages / Gemini |
| 接口地址 URL | 如 `https://api.xxx.com/v1`，自动补全 `/chat/completions` 路径（可关闭自动补全） |
| API Key | 密码框显示，见 8.3 |
| 模型名称 | 文本输入 + 「获取模型列表」（调用 `/models`，失败不影响手填） |
| 翻译 Prompt | 多行编辑器 + 「恢复默认」 |
| 识别模式 | 先本地识字再翻译 / 直接发图翻译（未勾选“模型支持图片”时禁用后者） |
| 网络代理 | 不使用 / 跟随系统 / HTTP / SOCKS5（主机、端口、可选认证，认证密码同样加密） |
| 高级 | 温度、最大 tokens、超时、是否流式、额外 Header（如网关需要） |

### 8.2 测试连接

点击后发送最小请求（`"Reply with OK"`，`max_tokens=5`，非流式），展示：
- ✅ 成功：耗时、模型返回、是否支持流式（再发一次流式探测）；
- ❌ 失败：**分类提示**——DNS/连不上（检查 URL/代理）、TLS 错误、401/403（Key 无效）、404（URL 或模型名错）、429（限流/余额）、超时，并附原始响应前 500 字符（Key 已脱敏）。

### 8.3 密钥本地加密

- 使用 `ProtectedData.Protect(key, entropy, DataProtectionScope.CurrentUser)`，存为 Base64 于 `%APPDATA%\Ppinpix\settings.json`；entropy 为安装时生成并另存的随机值。
- 内存中仅在发送请求时解密，使用后清零字节数组；UI 不回显明文（「👁」按钮需二次确认才临时显示）。
- 导出配置时**默认不含 Key**；日志、错误信息、崩溃转储中统一对 `Authorization` / `x-api-key` / `key=` 做脱敏。

### 8.4 Provider 抽象

```csharp
interface ILlmProvider {
    IAsyncEnumerable<string> StreamChatAsync(LlmRequest req, CancellationToken ct); // SSE 增量文本
    Task<ConnectionTestResult> TestAsync(CancellationToken ct);
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct);
}
record LlmRequest(string SystemPrompt, string UserText, byte[]? ImagePng, double? Temperature, int? MaxTokens);
```

每个配置实例化一个 `HttpClient`（带独立的 `SocketsHttpHandler` 与代理设置），按配置哈希缓存复用。

---

## 9. 隐私与「零外联」保证

**目标：除用户主动触发、且发往用户自己配置地址的翻译/测试请求外，程序不产生任何网络流量。**

1. **代码层隔离**：只有 `Ppinpix.LlmGateway` 程序集允许使用 `HttpClient` / `Socket` / `WebRequest`；`ArchTests` 在 CI 中用 NetArchTest 断言，违反则构建失败。
2. **无遥测、无崩溃上报、无在线 OCR、无自动更新检查**（更新由内部分发渠道手动安装；若将来加更新检查，必须默认关闭且仅指向内部地址）。
3. **WebView / 在线字体 / 在线帮助文档一律不用**，设置页纯 WPF。
4. `LlmGateway` 只在收到来自 UI 的显式用户动作（带 `UserInitiated` 令牌）时发请求；后台静默 OCR 永远不触发翻译。
5. 首次使用翻译时弹窗告知「截图内容/识别文字将发送至：<URL>」，可勾选不再提示。
6. 发布前验收：在 Windows 防火墙/Wireshark 下跑完整功能回归，确认未触发翻译时零出站连接。
7. 截图、OCR 结果、历史记录仅存本地（历史记录可关闭、可一键清空）。

---

## 10. 配置与数据存储

```
%APPDATA%\Ppinpix\
  settings.json        常规设置 + 热键 + LLM 配置（Key 为 DPAPI 密文）
  pins\                退出时保存的贴图（可选）
  history\             截图历史（可选，默认保留最近 50 张）
  logs\                滚动日志（脱敏，7 天）
%ProgramFiles%\Ppinpix\models\   OCR 模型
```

设置写入采用「写临时文件 → 原子替换」，带 schema 版本号以便迁移。

---

## 11. 性能与稳定性指标

| 指标 | 目标 |
|---|---|
| 热键 → 覆盖层可见 | < 150ms（双 4K 屏） |
| 选框拖动 / 标注绘制帧率 | ≥ 60fps |
| 常驻内存（无贴图） | < 80MB（OCR 模型懒加载后 +~120MB，可设空闲 5 分钟释放） |
| 1080p 截图 OCR | < 500ms |
| 翻译首字延迟 | 取决于接口；程序自身开销 < 50ms |
| 崩溃隔离 | OCR / 网络异常不影响截图主流程；全局异常捕获写本地日志 |

---

## 12. 里程碑（建议 4 个迭代，约 12~14 周）

| 迭代 | 内容 | 产出 |
|---|---|---|
| **M1 基础截图**（3 周） | 托盘、热键、DPI 体系、多屏覆盖层、窗口/控件吸附、放大镜取色、复制/保存 | 可日常替代系统截图 |
| **M2 标注 + 贴图**（3~4 周） | 全部标注工具 + 撤销重做；贴图缩放/透明/折叠/穿透/剪贴板贴图 | 对标 PixPin 核心体验 |
| **M3 离线 OCR**（2~3 周） | RapidOCR 集成、二维码、OCR 面板、贴图划选复制 | 断网可用 |
| **M4 大模型翻译**（3~4 周） | 配置面板 + 加密 + 代理 + 测试连接；对照浮窗流式；原位覆盖；零外联 CI 校验 | 完整 1.0 |

---

## 13. 主要风险与对策

| 风险 | 对策 |
|---|---|
| 混合 DPI 下坐标错位 | 全局 PX 坐标 + 每屏独立覆盖层；建立第 3 节测试矩阵并做自动截图比对 |
| UIA 在部分应用（Electron、游戏）卡顿或拿不到控件 | 预取 + 30ms 超时降级为窗口级 |
| 全屏独占游戏/受保护内容截不到 | DXGI 失败时降级 BitBlt；DRM 内容为黑属预期，提示用户 |
| 原位覆盖在复杂背景下观感差 | 自适应模糊底 + 半透明衬底；保留「对照浮窗」作为默认 |
| 各家「OpenAI 兼容」接口 SSE 细节不一致 | SSE 解析容错（忽略未知字段、兼容 `data:` 无空格、`[DONE]` 缺失）；测试连接时探测流式 |
| 模型返回 JSON 不规范（覆盖模式） | 增量宽松解析 + 失败自动回退到对照浮窗模式 |
| 杀软误报低级键盘钩子 | 钩子只在存在穿透贴图时安装；代码签名 |

---

## 14. 待确认事项

1. 是否需要 macOS / Linux（影响 WPF vs Qt 选型）？
2. 是否有统一的内部大模型网关可作为默认预置配置？
3. 是否需要长截图（滚动截图）、录屏/GIF —— 本期未纳入。
4. OCR 需支持的语种范围（影响安装包体积）。
5. 安装包分发方式与代码签名证书。

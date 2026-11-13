# Ppinpix

**Ppinpix** 是一款面向 Windows 平台的轻量级桌面截图、悬浮贴图、离线 OCR 与大模型原位翻译工具，采用 `.NET 10` + `C# 13` + `WPF` 构建，完整支持 `PerMonitorV2` 多显示器混合 DPI。

---

## 核心特性

### 1. 智能区域截图与标注 (`Ppinpix.Capture`)
- **多屏混合 DPI 捕获**：支持跨显示器、负坐标虚拟桌面与 `PerMonitorV2` 物理像素级精准捕获。
- **窗口与子控件智能吸附**：鼠标移动自动高亮顶层窗口及子控件边界，滚轮切换识别层级，支持单击快速锁选或拖拽框选。
- **实时放大镜与取色器**：`15×15` 像素放大镜，支持 `HEX` / `RGB` 格式切换（`Shift`）与一键复制色值（`C`）。
- **内置 9 种矢量标注工具**：矩形、椭圆、箭头、直线、画笔、荧光笔、序号标尺、文字、马赛克、高斯模糊，支持无限级撤销/重做（`Ctrl+Z` / `Ctrl+Y`）。

### 2. 悬浮贴图 (`Ppinpix.Pin`)
- **原位贴图与剪贴板贴图**：支持选区直接转贴图、剪贴板图片/图片文件/纯文本一键贴到屏幕（默认 `F3`）。
- **丰富交互控制**：滚轮以光标为中心无级缩放、`Ctrl+滚轮` 调节透明度、`R` 顺时针旋转、水平/垂直翻转、双击折叠为紧凑胶囊栏、`F2` 重命名。
- **鼠标穿透与会话恢复**：支持启用鼠标穿透并通过按住 `Ctrl+Alt` 临时解锁移动，支持退出自动持久化保存与启动恢复。
- **贴图静默 OCR 划词**：自动在贴图表面构建字符选区层，鼠标悬停文字变 `I` 型光标，可直接拖拽划词、`Ctrl+C` 复制文字、`Ctrl+A` 全选。

### 3. 完全离线 OCR 与二维码识别 (`Ppinpix.Ocr`)
- **双引擎离线识别**：内置 `RapidOCR (PP-OCRv4 ONNX)` 与 `Windows.Media.Ocr` 系统引擎切换，全程零联网。
- **版面段落重组**：基于行高、行距与水平对齐度自动聚类段落，保持多栏隔离。
- **二维码/条形码**：集成 `ZXing.Net`，识别后在贴图右键菜单或 OCR 窗口提供一键复制与打开链接。

### 4. 大模型原位覆盖贴图翻译 (`Ppinpix.Translate` & `Ppinpix.LlmGateway`)
- **默认原图原位贴图呈现**：截图点击「翻译」或按 `Ctrl+Shift+T` 后不弹出独立大窗口，直接在选区位置生成贴图，流式解析段落并原位渲染覆盖译文（自适应周边中位背景色/高斯模糊托底、明暗极性文字配色与二分最大可读字号）。
- **原图对比与快捷切换**：在已翻译贴图上**按住 `Alt` 键**（或通过右键菜单「对比：显示原图」）可随时预览原图，右键支持快速切换翻译目标语言并原地重译、复制原图/译图，或按需转入上下分栏「对照窗口」。
- **多协议网关与安全存储**：支持 `OpenAI 兼容`、`Anthropic Messages`、`Google Gemini` 协议及 `HTTP` / `SOCKS5` 代理；API Key 与代理密码全部采用本机 `DPAPI (CurrentUser)` 加密存储，日志自动脱敏。

---

## 项目结构

```text
Ppinpix/
├─ Directory.Build.props          # 统一编译配置 (.NET 10, x64, Nullable, PerMonitorV2)
├─ Ppinpix.sln                    # 解决方案文件
├─ docs/                          # 需求、架构、视觉、接口、隐私与测试文档
├─ tools/
│  └─ make-icon.ps1               # 应用图标生成脚本
├─ src/
│  ├─ Ppinpix.Core/               # 几何、显示器 DPI、热键、窗口枚举、DPAPI 加密与配置存储（纯基础库，零 UI/网络依赖）
│  ├─ Ppinpix.Theme/              # 暗色/亮色主题资源字典、令牌与矢量图标
│  ├─ Ppinpix.Capture/            # 屏幕捕获、选区覆盖层、放大镜、标注编辑器与图像编码输出
│  ├─ Ppinpix.Ocr/                # 离线 OCR (RapidOCR / Windows OCR)、段落聚类与二维码解码
│  ├─ Ppinpix.Pin/                # 悬浮贴图窗口、贴图管理器、文字划选层、全局钩子与会话持久化
│  ├─ Ppinpix.LlmGateway/         # 唯一允许联网的程序集：OpenAI / Anthropic / Gemini 流式客户端与代理
│  ├─ Ppinpix.Translate/          # 翻译服务、用户触发令牌 (UserInitiatedToken)、增量 JSON 解析与原位重绘引擎
│  └─ Ppinpix.App/                # 托盘宿主应用、原位贴图翻译协调器、对照翻译窗、OCR 面板与设置中心
└─ tests/
   ├─ Ppinpix.Core.Tests/         # 几何、吸附引擎、快捷键解析、DPAPI 与设置迁移测试
   ├─ Ppinpix.Theme.Tests/        # 主题令牌与资源完整性测试
   ├─ Ppinpix.Capture.Tests/      # 选区状态机、截图会话交互、标注命令栈与图像编解码测试
   ├─ Ppinpix.Ocr.Tests/          # 段落重组、图像预处理与条码识别测试
   ├─ Ppinpix.LlmGateway.Tests/   # 多协议请求构造、SSE 流式解析与连通性测试
   ├─ Ppinpix.Translate.Tests/    # 语种识别、原位排版渲染、流式覆盖翻译与令牌过期测试
   └─ Ppinpix.ArchTests/          # PE 元数据架构边界与零越权联网强制校验测试
```

---

## 默认快捷键

| 功能 | 默认快捷键 | 说明 |
|---|---|---|
| 区域截图 | `F1` | 启动智能窗口高亮与框选标注工具栏 |
| 贴剪贴板内容 | `F3` | 将剪贴板中的图片、图片文件或文本转为屏幕贴图 |
| 截图并原位翻译 | `Ctrl+Shift+T` | 框选后直接在原图位置生成贴图并原位覆盖译文 |
| 截图并提取文字 (OCR) | `Ctrl+Shift+O` | 框选后打开离线 OCR 与二维码识别结果面板 |
| 显示 / 隐藏全部贴图 | `Ctrl+Shift+H` | 一键切换桌面上所有贴图的可见状态 |
| 暂停 / 恢复全局热键 | `Ctrl+Alt+F12` | 临时挂起全部全局热键，避免与其他软件冲突 |

---

## 构建、测试与运行

### 环境要求
- Windows 10 (1809+) / Windows 11 x64
- .NET 10 SDK (`10.0.100` 或更高版本)

### 编译与测试

```powershell
# 编译 Release 版本
dotnet build Ppinpix.sln -c Release

# 运行全套单元测试与架构边界测试
dotnet test Ppinpix.sln -c Release --no-build
```

### 启动应用

```powershell
& .\src\Ppinpix.App\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\Ppinpix.exe
```

---

## 隐私与架构边界约束

项目通过 `Ppinpix.ArchTests` 在每次构建测试时直接扫描编译后程序集的 PE Metadata，强制执行以下约束：
1. **唯一网络出口**：除 `Ppinpix.LlmGateway.dll` 外，任何程序集不得引用 `System.Net.Http`、`System.Net.Sockets`、`System.Net.WebSockets` 等网络 API。
2. **离线模块零耦合**：`Core`、`Theme`、`Capture`、`Ocr`、`Pin` 模块严格禁止依赖 `LlmGateway` 或 `Translate`。
3. **显式用户触发**：所有远程翻译流式接口必须携带 10 秒有效期的 `UserInitiatedToken`，杜绝后台静默调用。
4. **密钥不落明文**：配置模型不含明文 `ApiKey` / `Password` 属性，磁盘仅保存 `dpapi:` 密文，导出配置默认剥离密钥。

# Ppinpix M1 里程碑：智能截图与标注（3 周）

> **目标**：实现可替代系统截图的基础工具，完美支持混合 DPI 多屏，提供窗口识别、放大镜取色、复制保存功能。

---

## 1. 里程碑概览

**周期**：3 周（15 个工作日）  
**交付日期**：2025-11-30（预估）  
**状态**：🚧 进行中  
**当前进度**：代码约 40%，文档已完成

---

## 2. 功能范围

### 2.1 ✅ 包含功能（必须实现）

- [x] **全局热键截图**（默认 `F1`）
- [x] **多显示器支持**（混合 DPI，负坐标，热插拔）
- [x] **窗口/控件智能识别**（自动高亮，滚轮切换层级）
- [x] **手动选区**（拖拽矩形，自动吸附边缘）
- [x] **选区调整**（8 个手柄，方向键微调）
- [x] **像素放大镜**（15×15 @×8，像素网格，坐标显示）
- [x] **颜色拾取**（HEX/RGB，按 `C` 复制）
- [x] **复制到剪贴板**（PNG 格式）
- [x] **保存为文件**（PNG/JPG，可配置路径和文件名模板）
- [x] **托盘图标与菜单**
- [x] **设置窗口**（常规、快捷键、截图设置）
- [x] **单实例运行**
- [x] **PerMonitorV2 DPI 支持**

### 2.2 ❌ 不包含功能（后续里程碑）

- ❌ 标注工具（矩形、箭头、文字等） → **M2**
- ❌ 撤销/重做 → **M2**
- ❌ 贴图功能 → **M2**
- ❌ OCR 文字识别 → **M3**
- ❌ 翻译功能 → **M4**
- ❌ WebP 格式导出 → **M3**

---

## 3. 技术架构

### 3.1 解决方案结构（M1 实现）

```
Ppinpix.sln
├── src/
│   ├── Ppinpix.App/              ← M1 新建
│   │   ├── App.xaml/cs           应用入口、全局异常处理
│   │   ├── Tray/                 托盘图标、菜单（WinForms NotifyIcon）
│   │   ├── Settings/             设置窗口（WPF）
│   │   ├── Assets/app.ico        应用图标（已生成）
│   │   └── app.manifest          PerMonitorV2 DPI 清单
│   │
│   ├── Ppinpix.Core/             ← 已完成 80%
│   │   ├── Display/              DisplayService（已完成）
│   │   ├── Hotkeys/              HotkeyManager（已完成）
│   │   ├── Input/                GlobalPointer（已完成）
│   │   ├── Geometry/             PxRect, DpiScale（已完成）
│   │   ├── Windowing/            WindowEnumerator, SnapEngine（已完成）
│   │   ├── Capture/              GdiScreenCapture, ScreenFrame（已完成）
│   │   ├── Settings/             AppSettings, SettingsStore（已完成）
│   │   ├── Security/             SecretStore（已完成，M4 使用）
│   │   ├── Diagnostics/          Log, Paths（已完成）
│   │   └── Interop/Win32.cs      Win32 P/Invoke（已完成）
│   │
│   ├── Ppinpix.Capture/          ← 已完成 90%
│   │   ├── Session/              CaptureSession, SelectionState（已完成）
│   │   ├── Overlay/              OverlayWindow, SelectionSurface（已完成）
│   │   ├── Overlay/              MagnifierWindow, ToastWindow（已完成）
│   │   ├── Overlay/              CaptureToolbar（已完成，M1 仅显示部分按钮）
│   │   ├── Output/               CaptureOutput（已完成）
│   │   └── Interop/              WindowChrome（已完成）
│   │
│   └── Ppinpix.Theme/            ← 已完成
│       ├── Tokens.cs             颜色、尺寸、字体常量
│       ├── Theme.xaml            WPF 样式资源
│       └── ThemeLoader.cs        资源加载器
│
└── tests/                        ← M1 新建
    └── Ppinpix.Core.Tests/       单元测试
        ├── Geometry/             PxRect 测试
        ├── Windowing/            SnapEngine 测试
        └── Settings/             SettingsStore 测试
```

### 3.2 核心技术点

| 技术点 | 实现方式 | 状态 |
|--------|----------|------|
| **DPI 感知** | PerMonitorV2，所有坐标用物理像素 | ✅ 已实现 |
| **多屏覆盖** | 每个显示器一个 `OverlayWindow` | ✅ 已实现 |
| **截图** | GDI `BitBlt` + `CAPTUREBLT` + DIBSection | ✅ 已实现 |
| **窗口识别** | `EnumWindows` + `DwmGetWindowAttribute` | ✅ 已实现 |
| **控件识别** | UI Automation（30ms 超时降级） | 📋 待实现 |
| **全局热键** | `RegisterHotKey` + 消息窗口 | ✅ 已实现 |
| **托盘** | WinForms `NotifyIcon` + 自绘深色菜单 | 📋 待实现 |
| **设置页** | WPF 窗口 + JSON 存储 | 📋 待实现 |
| **单实例** | 命名互斥量 `Global\Ppinpix` | 📋 待实现 |

---

## 4. 开发任务清单

### Week 1（Day 1-5）：基础设施 + 截图核心

#### Day 1-2：项目搭建与基础设施
- [x] 创建 `Ppinpix.sln` 解决方案文件
- [ ] 创建 `Ppinpix.App` 项目（WPF + .NET 10）
- [ ] 配置 `app.manifest`（PerMonitorV2 + longPathAware）
- [ ] 实现 `App.xaml.cs`：
  - [ ] 单实例检查（命名互斥量）
  - [ ] 全局异常处理（捕获 + 日志 + Toast）
  - [ ] `SetProcessDpiAwarenessContext`
  - [ ] 加载 Theme.xaml
  - [ ] 初始化托盘图标
- [ ] 实现托盘图标（`TrayIcon.cs`）：
  - [ ] WinForms `NotifyIcon` + 蓝色图标
  - [ ] 自定义 `ToolStripRenderer`（深色菜单）
  - [ ] 菜单项：截图 / 设置 / 退出
- [ ] 单元测试框架搭建（xUnit）

**验收**：
- ✅ 运行程序后显示托盘图标
- ✅ 双击打开时提示已运行
- ✅ 托盘菜单深色主题

#### Day 3-4：截图流程集成
- [ ] 实现 `CaptureService.cs`（App 层）：
  - [ ] 热键回调 → 启动 CaptureSession
  - [ ] 托盘菜单点击 → 启动截图
- [ ] 补充 UI Automation 控件识别（`WindowEnumerator.cs`）：
  - [ ] `UIA_ElementFromPoint` 预取
  - [ ] 30ms 超时降级
  - [ ] 过滤不可见/已销毁元素
- [ ] 完善 `CaptureToolbar`：
  - [ ] M1 仅显示：保存 / 复制 / 取消 / 完成
  - [ ] 隐藏标注/贴图/OCR/翻译按钮（Features 门控）
- [ ] 完善 `CaptureOutput`：
  - [ ] JPG 编码支持（`JpegBitmapEncoder`）
  - [ ] 文件名模板展开（`{yyyy-MM-dd}` 等）
  - [ ] 剪贴板重试 5 次（100ms 间隔）

**验收**：
- ✅ `F1` 触发截图，覆盖层瞬间显示
- ✅ 鼠标移动时窗口高亮正确
- ✅ 滚轮切换层级（窗口 → 子窗口 → 控件）
- ✅ 单击确认选区，显示工具栏
- ✅ `Enter` 复制成功，`Ctrl+S` 保存成功

#### Day 5：DPI 与多屏测试
- [ ] 测试单屏 100% / 125% / 150% / 200%
- [ ] 测试双屏混合 DPI（100% + 150%）
- [ ] 测试负坐标主屏（主屏在右）
- [ ] 测试热插拔显示器
- [ ] 修复发现的坐标错位问题
- [ ] 单元测试：`PxRect` 计算、`SnapEngine` 吸附

**验收**：
- ✅ 所有 DPI 场景下选区边框 1 物理像素
- ✅ 跨屏选区无缝
- ✅ 拔插显示器不崩溃

---

### Week 2（Day 6-10）：设置窗口 + 完善交互

#### Day 6-7：设置窗口（常规 + 快捷键）
- [ ] 创建 `SettingsWindow.xaml`：
  - [ ] 左侧导航：常规 / 快捷键 / 截图 / 关于
  - [ ] 右侧内容区域（`ContentControl` + 数据模板）
- [ ] **常规页**（`GeneralSettingsPanel.xaml`）：
  - [ ] 开机自动启动（注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`）
  - [ ] 截图保存位置（文件夹选择器）
  - [ ] 文件名模板（TextBox + 预览）
  - [ ] 图片格式（PNG / JPG 单选）
  - [ ] 延迟截图秒数（0/1/2/3/5 下拉）
  - [ ] 捕获鼠标指针（CheckBox）
- [ ] **快捷键页**（`HotkeySettingsPanel.xaml`）：
  - [ ] 截图热键录制框
  - [ ] 冲突检测与提示
  - [ ] 「恢复默认」按钮
- [ ] 设置保存与加载（`SettingsStore`）

**验收**：
- ✅ 托盘菜单「设置…」打开设置窗口
- ✅ 修改设置后立即生效（热键重新注册）
- ✅ 开机自启动注册表写入正确

#### Day 8-9：截图设置页 + 放大镜完善
- [ ] **截图设置页**（`CaptureSettingsPanel.xaml`）：
  - [ ] 拖动时隐藏放大镜（CheckBox，默认开启）
  - [ ] 自动吸附窗口边缘（CheckBox，默认开启）
  - [ ] 吸附灵敏度（4/8/12px 滑块）
  - [ ] 颜色格式默认（HEX / RGB 单选）
  - [ ] 复制后自动关闭（CheckBox，默认开启）
  - [ ] 保存后同时复制（CheckBox，默认关闭）
- [ ] 完善 `MagnifierWindow`：
  - [ ] 根据设置隐藏/显示
  - [ ] 边缘避让（翻转到对侧）
  - [ ] 性能优化（减少 `WriteableBitmap.WritePixels` 频率）
- [ ] 完善选区调整：
  - [ ] 方向键 1px 移动/调整
  - [ ] `Shift+方向键` 调整尺寸
  - [ ] 手柄拖拽流畅度优化

**验收**：
- ✅ 设置项修改后截图行为立即变化
- ✅ 放大镜在屏幕边缘正确翻转
- ✅ 方向键微调 1 物理像素精确

#### Day 10：关于页 + 托盘完善
- [ ] **关于页**（`AboutPanel.xaml`）：
  - [ ] 版本号（从 AssemblyInfo 读取）
  - [ ] 开源许可说明
  - [ ] 「查看日志文件夹」按钮
  - [ ] 「清空所有设置」按钮（二次确认）
- [ ] 托盘菜单完善：
  - [ ] 右键菜单自绘（深色主题）
  - [ ] 左键双击打开设置窗口（M2 改为显示/隐藏贴图列表）
  - [ ] 退出时清理资源
- [ ] 日志记录完善：
  - [ ] 启动日志（版本、DPI、显示器信息）
  - [ ] 截图日志（耗时、选区大小）
  - [ ] 异常日志（堆栈脱敏）

**验收**：
- ✅ 关于页信息正确
- ✅ 清空设置后恢复默认值
- ✅ 日志文件包含完整上下文

---

### Week 3（Day 11-15）：测试、优化、文档

#### Day 11-12：性能优化与稳定性
- [ ] **性能测试**：
  - [ ] 热键到覆盖层出现 < 150ms（双 4K）
  - [ ] 拖动选区保持 60fps（`PresentationTraceSources` 检测）
  - [ ] 常驻内存 < 80MB（Task Manager 验证）
- [ ] **性能优化**：
  - [ ] 截图线程池复用
  - [ ] 窗口枚举结果缓存（100ms TTL）
  - [ ] 减少 `OnDpiChanged` 重复触发
  - [ ] GC 压力分析（dotMemory）
- [ ] **稳定性测试**：
  - [ ] 1000 次连续截图不崩溃
  - [ ] 热插拔显示器 50 次不崩溃
  - [ ] 改缩放 + 截图 20 次不崩溃
  - [ ] 快速按 `F1` + `Esc` 100 次不崩溃
- [ ] **异常处理**：
  - [ ] 所有 Win32 调用检查返回值
  - [ ] 截图失败时 Toast 提示
  - [ ] 设置加载失败时使用默认值
  - [ ] 热键注册失败时托盘气泡提示

**验收**：
- ✅ 性能指标达标
- ✅ 稳定性测试全部通过
- ✅ 无未处理异常

#### Day 13：单元测试
- [ ] **Core.Tests**：
  - [ ] `PxRect` 几何计算（100% 覆盖）
  - [ ] `SnapEngine.Snap` 边缘吸附（10+ 用例）
  - [ ] `HotkeyGesture.TryParse` 解析（20+ 用例）
  - [ ] `SettingsStore` 序列化/反序列化
  - [ ] `Log.Redact` 脱敏正则
- [ ] **Capture.Tests**：
  - [ ] `SelectionState.Resize` 手柄调整
  - [ ] `CaptureOutput.ExpandTemplate` 文件名展开
- [ ] **Theme.Tests**：
  - [ ] 所有颜色 Token 存在
  - [ ] 所有样式可加载

**目标**：
- ✅ 核心模块测试覆盖率 > 80%

#### Day 14：验收测试
- [ ] **功能验收**（参照 REQUIREMENTS.md FR-1.*）：
  - [ ] 所有 P0 功能可用
  - [ ] 所有 P1 功能可用
- [ ] **DPI 验收矩阵**：
  - [ ] 单屏：100% / 125% / 150% / 200%
  - [ ] 双屏：100%+150% 左右 / 上下 / 负坐标
  - [ ] 三屏：混合 DPI
  - [ ] 运行中改缩放
  - [ ] 热插拔
- [ ] **键盘鼠标验收**（参照 KEYBOARD_MOUSE.md）：
  - [ ] 所有快捷键正常
  - [ ] 所有鼠标操作正常
- [ ] **隐私验收**（参照 PRIVACY.md）：
  - [ ] Wireshark 测试：零网络连接
  - [ ] 防火墙测试：阻止后所有功能正常

**验收文档**：
- [ ] 填写 `M1_ACCEPTANCE.md`（验收报告）

#### Day 15：文档与发布准备
- [ ] 更新 `README.md`（M1 使用说明）
- [ ] 更新 `CHANGELOG.md`（v0.1.0-M1 发布日志）
- [ ] 截图与录屏演示（GIF）
- [ ] 编写《M1 已知问题与限制》
- [ ] 打包发布：
  - [ ] 自包含单文件发布（`dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`）
  - [ ] 压缩为 `Ppinpix-v0.1.0-M1-win-x64.zip`
  - [ ] 计算 SHA256 校验和

**交付物**：
- ✅ `Ppinpix.exe`（单文件，约 80MB）
- ✅ `README.md`
- ✅ 演示 GIF
- ✅ 验收报告

---

## 5. 风险与应对

| 风险 | 概率 | 影响 | 应对措施 |
|------|------|------|----------|
| UI Automation 超时拖慢截图启动 | 中 | 中 | 异步预取 + 30ms 超时降级为窗口级 |
| 某些应用窗口识别失败（Electron/游戏） | 高 | 低 | 已预期，手动拖框仍可用 |
| 混合 DPI 坐标错位 | 中 | 高 | 已有完整测试矩阵，逐项验证 |
| WinForms 托盘在 WPF 中集成问题 | 低 | 中 | 参考 ShareX / Greenshot 实现 |
| 性能不达标（< 150ms） | 低 | 高 | 已实现 GDI 快速截图，预计 50-80ms |

---

## 6. 验收标准（M1 交付门槛）

### 6.1 必须通过（P0）
- [ ] ✅ 热键到覆盖层 < 150ms（双 4K）
- [ ] ✅ 窗口识别成功率 > 90%（常见应用）
- [ ] ✅ 1 物理像素边框无错位（所有 DPI）
- [ ] ✅ 复制/保存功能正常
- [ ] ✅ 零网络连接（Wireshark 验证）
- [ ] ✅ 无崩溃（1000 次截图）

### 6.2 应该通过（P1）
- [ ] ✅ 控件级识别成功率 > 60%
- [ ] ✅ 放大镜 60fps
- [ ] ✅ 设置持久化正常
- [ ] ✅ 快捷键冲突检测准确

### 6.3 可选通过（P2）
- [ ] 延迟截图功能
- [ ] 捕获鼠标指针功能
- [ ] 单元测试覆盖率 > 80%

---

## 7. 下一步（M2 预告）

M1 完成后，立即启动 M2：
- 标注工具（矩形、箭头、文字、马赛克等 8 种）
- 撤销/重做（Command 模式）
- 贴图窗口（缩放、透明、折叠、穿透）
- 剪贴板贴图（`F3`）
- 退出保存/恢复贴图

**预计工期**：3-4 周  
**交付时间**：2025-12-31

---

## 8. 资源需求

- **开发人员**：1 人全职
- **测试设备**：
  - 笔记本（150% 缩放）
  - 外接显示器（100% 缩放）
  - 4K 显示器（200% 缩放）
- **开发工具**：
  - Visual Studio 2022 / Rider
  - .NET 10 SDK
  - Git
- **测试工具**：
  - Wireshark（网络监控）
  - dotMemory（内存分析）
  - PresentationTraceSources（WPF 性能）

---

## 9. 进度跟踪

| 日期 | 计划任务 | 实际完成 | 状态 | 备注 |
|------|----------|----------|------|------|
| Week 1 | 基础设施 + 截图核心 | — | 📋 待开始 | — |
| Week 2 | 设置窗口 + 完善交互 | — | 📋 待开始 | — |
| Week 3 | 测试 + 优化 + 文档 | — | 📋 待开始 | — |

**更新频率**：每日更新

---

## 10. 成功标准

M1 成功的标志：
1. ✅ 可替代 Windows 自带截图工具（Win+Shift+S）
2. ✅ 日常使用无明显卡顿、错位、崩溃
3. ✅ 混合 DPI 下体验完美（1px 边框不糊）
4. ✅ 内部至少 5 人试用反馈正面
5. ✅ 零隐私问题（Wireshark 验证）

达到以上标准后，宣布 **M1 完成**，进入 M2 开发。

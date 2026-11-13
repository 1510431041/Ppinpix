# Ppinpix M2 里程碑：标注工具与悬浮贴图（3-4 周）

> **目标**：实现完整的标注编辑器和悬浮贴图功能，对标 PixPin 核心体验。

---

## 1. 里程碑概览

**前置依赖**：M1 完成  
**周期**：3-4 周（18 个工作日）  
**交付日期**：2025-12-31（预估）  
**状态**：📋 计划中  

---

## 2. 功能范围

### 2.1 ✅ 包含功能（必须实现）

#### A. 标注工具（8 种）
- [ ] **矩形/椭圆**：可设线宽（2/3/5px）、颜色、实心/空心
- [ ] **箭头**：可设线宽、颜色，实心三角箭头
- [ ] **直线**：可设线宽、颜色、实线/虚线
- [ ] **画笔**：自由绘制，可设粗细、颜色
- [ ] **递增序号**：圆形徽标 ①②③，可设大小（20/24/32px）、颜色、起始数字
- [ ] **文字**：多行输入，可设字号、颜色、加粗、描边
- [ ] **马赛克**：矩形区域打码，可设块大小
- [ ] **荧光笔**：半透明高亮画笔

#### B. 标注编辑
- [ ] **选中标注**：点击或框选，显示虚线包围框 + 手柄
- [ ] **移动标注**：拖拽内部
- [ ] **调整大小**：拖拽手柄
- [ ] **删除标注**：`Delete` 键或右键菜单
- [ ] **修改样式**：选中后工具栏显示属性条（线宽、颜色、字号）
- [ ] **撤销/重做**：`Ctrl+Z` / `Ctrl+Y`，无次数限制，Command 模式

#### C. 工具栏完善
- [ ] 所有标注工具按钮（M1 已隐藏的启用）
- [ ] 工具属性条（线宽 3 档圆点、8 色色块、字号选择）
- [ ] 快捷键提示（悬停 Tooltip）
- [ ] 「贴图」按钮跳转贴图功能

#### D. 悬浮贴图
- [ ] **创建贴图**：截图工具栏「贴图」按钮、`F3` 贴剪贴板
- [ ] **移动贴图**：左键拖动
- [ ] **缩放贴图**：滚轮（10%~1000%，以光标为中心）
- [ ] **调整透明度**：`Ctrl+滚轮`（10%~100%）
- [ ] **折叠/展开**：双击折叠为缩略条，再双击展开
- [ ] **隐藏贴图**：按 `H` 隐藏，托盘菜单「显示所有贴图」恢复
- [ ] **关闭贴图**：`Esc` 或右键菜单
- [ ] **贴图管理**：`PinManager` 负责所有贴图生命周期

#### E. 贴图右键菜单
- [ ] 复制/保存/编辑标注
- [ ] 旋转/翻转（顺时针 90°、水平翻转、垂直翻转）
- [ ] 始终置顶开关
- [ ] 鼠标穿透开关（M2 后期可选）
- [ ] 隐藏/关闭

#### F. 设置页新增
- [ ] **贴图设置页**：
  - 退出时保存贴图（默认关闭）
  - 贴图默认缩放行为（保持物理像素/保持视觉尺寸）
  - 贴图阴影开关（默认开启）
- [ ] **标注默认值**：每种工具的默认线宽、颜色、字号

### 2.2 ❌ 不包含功能（后续里程碑）
- ❌ 贴图上文字划选 → **M3**（需 OCR）
- ❌ 橡皮擦工具 → **M2 后期可选**
- ❌ 高斯模糊工具 → **M2 后期可选**
- ❌ 标注对象分组/层级调整 → **低优先级**

---

## 3. 技术架构

### 3.1 新增模块

```
src/
├── Ppinpix.Capture/
│   └── Annotation/              ← M2 新建
│       ├── Shapes/
│       │   ├── IShape.cs        标注对象接口
│       │   ├── RectangleShape.cs
│       │   ├── EllipseShape.cs
│       │   ├── ArrowShape.cs
│       │   ├── LineShape.cs
│       │   ├── FreehandShape.cs （画笔）
│       │   ├── NumberBadgeShape.cs （序号）
│       │   ├── TextShape.cs
│       │   ├── MosaicShape.cs
│       │   └── HighlightShape.cs （荧光笔）
│       │
│       ├── Commands/
│       │   ├── ICommand.cs      命令接口（撤销/重做）
│       │   ├── AddShapeCommand.cs
│       │   ├── DeleteShapeCommand.cs
│       │   ├── MoveShapeCommand.cs
│       │   ├── ResizeShapeCommand.cs
│       │   └── ModifyStyleCommand.cs
│       │
│       ├── AnnotationLayer.cs   标注层（WPF Canvas）
│       ├── AnnotationEditor.cs  编辑器逻辑
│       ├── CommandStack.cs      撤销/重做栈
│       └── ShapeRenderer.cs     标注渲染器
│
├── Ppinpix.Pin/                 ← M2 新建
│   ├── PinWindow.xaml/cs        贴图窗口
│   ├── PinManager.cs            贴图管理器
│   ├── PinSettings.cs           贴图配置
│   └── PinStorage.cs            贴图持久化（退出保存/恢复）
│
└── Ppinpix.App/
    └── Tray/
        └── TrayIcon.cs          ← 新增：显示/隐藏所有贴图菜单项
```

### 3.2 核心设计

#### 标注对象模型

```csharp
public interface IShape
{
    Guid Id { get; }
    PxRect Bounds { get; set; }
    ShapeStyle Style { get; set; }
    void Render(DrawingContext dc, DpiScale dpi);
    bool HitTest(PxPoint point);
    IShape Clone();
}

public record ShapeStyle(
    Color Color,
    double LineWidth,
    bool Filled,
    double FontSize,
    bool Bold
);

public class RectangleShape : IShape
{
    public PxRect Bounds { get; set; }
    public ShapeStyle Style { get; set; }
    
    public void Render(DrawingContext dc, DpiScale dpi)
    {
        var rect = Bounds.ToDip(dpi);
        var pen = new Pen(new SolidColorBrush(Style.Color), Style.LineWidth / dpi.Scale);
        var brush = Style.Filled ? new SolidColorBrush(Style.Color) : null;
        dc.DrawRectangle(brush, pen, rect);
    }
}
```

#### 命令模式（撤销/重做）

```csharp
public interface ICommand
{
    void Execute();
    void Undo();
}

public class AddShapeCommand : ICommand
{
    private readonly AnnotationEditor _editor;
    private readonly IShape _shape;
    
    public void Execute() => _editor.Shapes.Add(_shape);
    public void Undo() => _editor.Shapes.Remove(_shape);
}

public class CommandStack
{
    private readonly Stack<ICommand> _undoStack = new();
    private readonly Stack<ICommand> _redoStack = new();
    
    public void Execute(ICommand command)
    {
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear(); // 新操作后清空重做栈
    }
    
    public void Undo()
    {
        if (_undoStack.Count == 0) return;
        var command = _undoStack.Pop();
        command.Undo();
        _redoStack.Push(command);
    }
    
    public void Redo()
    {
        if (_redoStack.Count == 0) return;
        var command = _redoStack.Pop();
        command.Execute();
        _undoStack.Push(command);
    }
}
```

#### 贴图窗口样式

```csharp
// 置顶、无边框、工具窗口（不进任务栏）
var style = WS_POPUP | WS_VISIBLE;
var exStyle = WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_LAYERED;

// 可选透明度
SetLayeredWindowAttributes(hwnd, 0, (byte)(opacity * 255), LWA_ALPHA);

// 阴影（DWM 实现）
var margins = new MARGINS { cyTopHeight = 1 };
DwmExtendFrameIntoClientArea(hwnd, ref margins);
```

---

## 4. 开发任务清单

### Week 1（Day 1-5）：标注工具基础

#### Day 1-2：标注对象模型
- [ ] 创建 `Ppinpix.Capture/Annotation/` 目录
- [ ] 实现 `IShape` 接口与 `ShapeStyle`
- [ ] 实现 5 种基础形状：
  - [ ] `RectangleShape` / `EllipseShape`
  - [ ] `LineShape` / `ArrowShape`
  - [ ] `FreehandShape`（画笔，记录路径点）
- [ ] 实现 `ShapeRenderer.Render(IShape, DrawingContext, DpiScale)`
- [ ] 单元测试：`HitTest` 命中检测

#### Day 3-4：标注编辑器
- [ ] 实现 `AnnotationLayer.xaml`（WPF Canvas）
- [ ] 实现 `AnnotationEditor.cs`：
  - [ ] `Shapes` 集合（ObservableCollection）
  - [ ] `SelectedShape` 当前选中
  - [ ] `CurrentTool` 当前工具（矩形/箭头/...）
  - [ ] `OnMouseDown/Move/Up` 绘制逻辑
- [ ] 实现选中标注：
  - [ ] 点击命中测试
  - [ ] 显示虚线包围框（1px Accent 虚线）
  - [ ] 显示 8 个手柄（四角+四边）
- [ ] 实现移动标注：拖拽内部
- [ ] 实现调整大小：拖拽手柄

**验收**：
- ✅ 可绘制矩形、椭圆、箭头、直线、画笔
- ✅ 点击选中标注，显示手柄
- ✅ 拖动标注移动，拖动手柄调整大小

#### Day 5：属性面板与撤销重做
- [ ] 实现工具属性条（`ToolPropertiesBar.xaml`）：
  - [ ] 线宽选择（3 档圆点：2/3/5px）
  - [ ] 8 色调色板（18×18 圆形色块）
  - [ ] 字号选择（12/16/20/24 下拉）
- [ ] 选中工具后在工具栏下方显示属性条
- [ ] 修改属性时已绘制标注同步更新（绑定 `SelectedShape.Style`）
- [ ] 实现命令模式：
  - [ ] `ICommand` 接口
  - [ ] `AddShapeCommand` / `DeleteShapeCommand` / `MoveShapeCommand`
  - [ ] `CommandStack.Execute/Undo/Redo`
- [ ] 绑定快捷键：`Ctrl+Z` / `Ctrl+Y`

**验收**：
- ✅ 属性条修改立即生效
- ✅ 撤销/重做无次数限制
- ✅ 删除标注后可撤销恢复

---

### Week 2（Day 6-10）：高级标注工具 + 贴图基础

#### Day 6-7：高级标注工具
- [ ] 实现 `NumberBadgeShape`（递增序号）：
  - [ ] 圆形实心徽标 + 白色数字
  - [ ] 起始数字可配置（默认 1）
  - [ ] 删除后可选自动重排（设置项）
- [ ] 实现 `TextShape`（文字标注）：
  - [ ] 双击输入区域弹出文本框
  - [ ] 多行输入（`TextBox.AcceptsReturn`）
  - [ ] 支持加粗、描边（2px 白色外轮廓）
  - [ ] 渲染使用 `FormattedText`
- [ ] 实现 `MosaicShape`（马赛克）：
  - [ ] 矩形区域像素块化
  - [ ] 块大小可调（8/12/16 物理像素）
  - [ ] 从原始冻结帧采样（不叠加已有标注）
  - [ ] 使用 `ScreenFrame.Mosaic` 方法
- [ ] 实现 `HighlightShape`（荧光笔）：
  - [ ] 半透明路径（Alpha 50%）
  - [ ] 粗笔触（12px）

**验收**：
- ✅ 序号自动递增 ①②③
- ✅ 文字标注多行输入正常
- ✅ 马赛克不叠加其他标注（从原图采样）
- ✅ 荧光笔半透明效果正确

#### Day 8-9：贴图窗口
- [ ] 创建 `Ppinpix.Pin` 项目
- [ ] 实现 `PinWindow.xaml`：
  - [ ] 无边框 WPF 窗口
  - [ ] `WindowStyle=None`, `ResizeMode=NoResize`
  - [ ] `Topmost=True`, `ShowInTaskbar=False`
  - [ ] 内容区显示图片（`Image` 控件）
  - [ ] 1px 描边 + 阴影（见 3.2 窗口样式）
- [ ] 实现 `PinWindow.xaml.cs` 交互：
  - [ ] 左键拖动：`DragMove()`
  - [ ] 滚轮缩放：`ScaleTransform` + 原点设为光标位置
  - [ ] `Ctrl+滚轮` 透明度：`Window.Opacity`
  - [ ] 双击折叠：缩小为 160×28 胶囊条（显示缩略图 + 标题）
  - [ ] `Esc` 关闭：`Close()`
- [ ] 实现缩放/透明度徽标：
  - [ ] 中心显示 `Surface` 徽标「150%」
  - [ ] 800ms 后淡出（`BeginAnimation` Opacity）
- [ ] Win32 窗口样式设置：
  - [ ] `SetWindowLong` 设置 `WS_EX_TOPMOST | WS_EX_TOOLWINDOW`
  - [ ] DWM 阴影（`DwmExtendFrameIntoClientArea`）

**验收**：
- ✅ 贴图窗口可拖动、缩放、透明
- ✅ 双击折叠/展开正常
- ✅ 徽标显示 800ms 后淡出

#### Day 10：贴图管理器
- [ ] 实现 `PinManager.cs`：
  - [ ] `ObservableCollection<PinWindow> Pins` 所有贴图
  - [ ] `CreatePin(BitmapSource image)` 创建贴图
  - [ ] `ShowAll()` / `HideAll()` 批量显示/隐藏
  - [ ] `CloseAll()` 关闭所有贴图
  - [ ] `SavePins()` / `LoadPins()` 持久化（见下）
- [ ] 集成到 `CaptureSession`：
  - [ ] 工具栏「贴图」按钮 → `PinManager.CreatePin`
- [ ] 实现 `F3` 热键：
  - [ ] 读取剪贴板（`Clipboard.GetImage()`）
  - [ ] 若为文本 → 渲染为白底黑字图片
  - [ ] 若为文件路径 → 加载图片
  - [ ] 创建贴图
- [ ] 托盘菜单新增：
  - [ ] 「贴剪贴板 F3」
  - [ ] 「显示/隐藏所有贴图 Ctrl+Shift+H」

**验收**：
- ✅ 截图后点「贴图」成功创建贴图
- ✅ `F3` 贴剪贴板图片/文本正常
- ✅ 托盘菜单显示/隐藏所有贴图正常

---

### Week 3（Day 11-15）：贴图完善 + 持久化

#### Day 11-12：贴图右键菜单
- [ ] 实现 `PinWindow` 右键菜单（`ContextMenu`）：
  - [ ] 复制（`Clipboard.SetImage`）
  - [ ] 保存（弹出文件对话框）
  - [ ] 编辑标注（重新进入截图标注模式）
  - [ ] 旋转 90°（`RotateTransform`）
  - [ ] 水平翻转（`ScaleTransform.ScaleX = -1`）
  - [ ] 垂直翻转（`ScaleTransform.ScaleY = -1`）
  - [ ] 始终置顶开关（切换 `Topmost`）
  - [ ] 隐藏（`Hide()`，托盘菜单恢复）
  - [ ] 关闭（`Close()`）
- [ ] 菜单样式：深色主题，参照 `UI_VISUAL.md` 7.7 规范
- [ ] 快捷键绑定：
  - [ ] `Ctrl+C` 复制
  - [ ] `Ctrl+S` 保存
  - [ ] `H` 隐藏
  - [ ] `Esc` 关闭

**验收**：
- ✅ 所有菜单项功能正常
- ✅ 旋转/翻转不影响缩放和透明度

#### Day 13：贴图持久化（可选功能）
- [ ] 实现 `PinStorage.cs`：
  - [ ] 保存格式：`%APPDATA%\Ppinpix\pins\pin_{guid}.json`
    ```json
    {
      "id": "uuid",
      "image": "pin_{guid}.png",
      "bounds": {"x": 100, "y": 100, "width": 300, "height": 200},
      "scale": 1.0,
      "opacity": 1.0,
      "topmost": true
    }
    ```
  - [ ] `SaveAllPins()` 遍历 `PinManager.Pins`，写 JSON + PNG
  - [ ] `LoadAllPins()` 读取目录下所有 JSON，重建 `PinWindow`
- [ ] 设置页新增「贴图设置」：
  - [ ] 「退出时保存贴图」CheckBox（默认关闭）
  - [ ] 「启动时恢复贴图」CheckBox（默认关闭）
- [ ] 集成到 `App.xaml.cs`：
  - [ ] `OnStartup` → `PinManager.LoadPins()`（如果设置启用）
  - [ ] `OnExit` → `PinManager.SavePins()`（如果设置启用）

**验收**：
- ✅ 启用保存后，退出重启恢复贴图位置、缩放、透明度
- ✅ 未启用时不保存

#### Day 14：鼠标穿透（可选功能，M2 后期）
- [ ] 实现鼠标穿透：
  - [ ] 右键菜单「鼠标穿透」切换
  - [ ] 启用时设置 `WS_EX_TRANSPARENT`
  - [ ] 窗口右上角显示穿透图标徽标（14×14 `Surface` 底）
- [ ] 实现临时解除穿透：
  - [ ] 安装低级键盘钩子（`SetWindowsHookEx` WH_KEYBOARD_LL）
  - [ ] 检测 `Ctrl+Alt` 按住
  - [ ] 按住时移除 `WS_EX_TRANSPARENT`，松开恢复
  - [ ] 仅在存在穿透贴图时安装钩子（性能考虑）
- [ ] 全局热键恢复穿透贴图：
  - [ ] 遍历所有贴图，移除 `WS_EX_TRANSPARENT`
  - [ ] 10 秒后自动恢复穿透

**验收**：
- ✅ 穿透时点击贴图不响应，穿透到下层窗口
- ✅ 按住 `Ctrl+Alt` 临时解除穿透

#### Day 15：贴图 DPI 与跨屏
- [ ] 处理贴图 `WM_DPICHANGED`：
  - [ ] 检测设置：「保持物理像素」或「保持视觉尺寸」
  - [ ] 保持物理像素：缩放到新 DPI 下相同物理尺寸
  - [ ] 保持视觉尺寸：缩放到新 DPI 下相同 DIP 尺寸
- [ ] 跨屏拖动测试：
  - [ ] 从 100% 屏拖到 150% 屏，贴图自动调整
- [ ] 贴图边界限制：
  - [ ] 不允许完全拖出屏幕（至少保留 32px 可见）
  - [ ] 多屏环境下可跨屏

**验收**：
- ✅ 跨屏拖动贴图大小/清晰度正确
- ✅ 拖动时不会完全消失

---

### Week 4（Day 16-18）：测试、优化、文档

#### Day 16：集成测试
- [ ] **标注测试**：
  - [ ] 所有 8 种工具绘制正常
  - [ ] 撤销/重做 50 次不崩溃
  - [ ] 修改样式立即生效
  - [ ] 删除序号后重排正确
- [ ] **贴图测试**：
  - [ ] 创建 20 个贴图不崩溃
  - [ ] 缩放到 10% / 1000% 正常
  - [ ] 透明度 10% / 100% 正常
  - [ ] 折叠/展开正常
  - [ ] 右键菜单所有项可用

#### Day 17：性能与稳定性
- [ ] **性能测试**：
  - [ ] 标注 50 个对象后拖动仍 60fps
  - [ ] 常驻内存 < 100MB（5 个贴图）
- [ ] **稳定性测试**：
  - [ ] 连续标注 + 撤销 1000 次不崩溃
  - [ ] 贴图拖动跨屏 100 次不崩溃
  - [ ] 退出保存/恢复 10 次正常

#### Day 18：文档与发布
- [ ] 更新 `README.md`（M2 功能说明）
- [ ] 更新 `CHANGELOG.md`（v0.1.0-M2）
- [ ] 录制演示视频（标注 + 贴图）
- [ ] 编写《M2 已知问题》
- [ ] 打包发布 `Ppinpix-v0.1.0-M2-win-x64.zip`

---

## 5. 风险与应对

| 风险 | 概率 | 影响 | 应对措施 |
|------|------|------|----------|
| 马赛克从原图采样性能差 | 中 | 低 | 预计算马赛克，缓存结果 |
| 撤销栈内存占用过大 | 低 | 中 | 只存几何差量，不存位图 |
| 低级键盘钩子被杀软拦截 | 中 | 低 | 鼠标穿透功能标记为可选（P2） |
| 贴图数量过多影响性能 | 低 | 低 | 限制同时存在贴图数量（设置项） |

---

## 6. 验收标准（M2 交付门槛）

### 6.1 必须通过（P0）
- [ ] ✅ M1 所有功能保持正常
- [ ] ✅ 8 种标注工具可用
- [ ] ✅ 撤销/重做无次数限制
- [ ] ✅ 贴图缩放/透明/折叠正常
- [ ] ✅ 贴图跨屏拖动无错位
- [ ] ✅ 零网络连接（M1 基础上）

### 6.2 应该通过（P1）
- [ ] ✅ 标注 50 个对象后仍流畅（60fps）
- [ ] ✅ 退出保存/恢复贴图正常
- [ ] ✅ 右键菜单所有项功能正常

### 6.3 可选通过（P2）
- [ ] 鼠标穿透功能
- [ ] 橡皮擦工具
- [ ] 高斯模糊工具

---

## 7. 下一步（M3 预告）

M2 完成后，启动 M3：
- 本地 OCR 集成（RapidOCR）
- 二维码/条码识别（ZXing.Net）
- OCR 结果面板
- 贴图文字划选复制

**预计工期**：2-3 周  
**交付时间**：2026-01-31

---

## 8. 成功标准

M2 成功的标志：
1. ✅ 对标 PixPin 核心截图+标注+贴图体验
2. ✅ 日常使用标注工具顺手（撤销重做流畅）
3. ✅ 贴图功能实用（缩放/透明/折叠/跨屏无问题）
4. ✅ 内部至少 10 人试用反馈正面
5. ✅ 无性能/稳定性问题

达到以上标准后，宣布 **M2 完成**，进入 M3 开发。

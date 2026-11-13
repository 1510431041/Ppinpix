# Ppinpix 文档目录

## 项目概览

**Ppinpix** 是一款对标 PixPin 的内部免费桌面端截图与翻译工具，优先支持 Windows 平台，常驻系统托盘。

## 核心文档

### 设计与规范
- **[DESIGN.md](DESIGN.md)** - 技术架构、模块设计、里程碑规划
- **[UI_VISUAL.md](UI_VISUAL.md)** - 界面视觉规范（颜色、字体、尺寸、动效）
- **[REQUIREMENTS.md](REQUIREMENTS.md)** - 完整功能需求说明
- **[KEYBOARD_MOUSE.md](KEYBOARD_MOUSE.md)** - 键盘鼠标交互映射表

### 实施计划
- **[PHASE1.md](PHASE1.md)** - M1 里程碑：智能截图与标注
- **[PHASE2.md](PHASE2.md)** - M2 里程碑：悬浮贴图与标注工具
- **[PHASE3.md](PHASE3.md)** - M3 里程碑：离线 OCR 与二维码
- **[PHASE4.md](PHASE4.md)** - M4 里程碑：大模型截图翻译

### API 与集成
- **[LLM_API.md](LLM_API.md)** - 大模型接口对接规范
- **[PRIVACY.md](PRIVACY.md)** - 隐私保护与零外联承诺

### 测试与质量
- **[TESTING.md](TESTING.md)** - 测试策略与用例清单
- **[CHANGELOG.md](CHANGELOG.md)** - 版本更新日志

## 快速链接

- 当前版本：**1.0.0**
- 目标平台：Windows 11 / Windows 10 (1809+)
- 技术栈：.NET 10, C# 13, WPF, PerMonitorV2 DPI
- 许可：内部使用

## 开发状态

| 里程碑 | 状态 | 说明 |
|--------|------|------|
| M1 智能截图 | ✅ 已完成 | 多屏 PerMonitorV2 捕获、窗口/控件吸附、放大镜取色、剪贴板与文件保存 |
| M2 贴图标注 | ✅ 已完成 | 9 种矢量标注工具、悬浮贴图、缩放/旋转/折叠、Ctrl+Alt 穿透、会话持久化 |
| M3 离线 OCR | ✅ 已完成 | RapidOCR / Windows OCR 双引擎、段落聚类、贴图划词复制、ZXing 二维码 |
| M4 AI 翻译 | ✅ 已完成 | 默认贴图原位覆盖翻译、Alt 原图对比、对照浮窗、OpenAI/Anthropic/Gemini 网关 |

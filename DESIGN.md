---
name: TraceTime
description: Windows WPF 本地时长工具，支持持久化明暗模式与材质选择
colors:
  accent-seed: "#24777B"
  blue-seed: "#3564C0"
  purple-seed: "#7956B3"
  rose-seed: "#A64E6E"
  amber-seed: "#A56820"
  green-seed: "#3F805C"
  light-canvas: "#F5F7F9"
  light-soft-canvas: "#F3F4F6"
  light-surface: "#FFFFFF"
  light-ink: "#202D39"
  light-muted: "#596A79"
  light-border: "#BDCBD5"
  light-divider: "#DEE5EA"
  dark-canvas: "#11171D"
  dark-surface: "#1C252E"
  dark-field: "#18212A"
  dark-ink: "#F1F5F8"
  dark-muted: "#A8B7C5"
  dark-border: "#475867"
  dark-divider: "#34414D"
typography:
  display:
    fontFamily: "Segoe UI, Microsoft YaHei UI"
    fontSize: "35"
    fontWeight: 600
  headline:
    fontFamily: "Segoe UI, Microsoft YaHei UI"
    fontSize: "28"
    fontWeight: 600
  title:
    fontFamily: "Segoe UI, Microsoft YaHei UI"
    fontSize: "18"
    fontWeight: 600
  body:
    fontFamily: "Segoe UI, Microsoft YaHei UI"
    fontSize: "14"
    fontWeight: 400
  hint:
    fontFamily: "Segoe UI, Microsoft YaHei UI"
    fontSize: "12"
    fontWeight: 400
rounded:
  panel: "16"
  panel-minimal: "6"
  control: "9"
  control-minimal: "5"
spacing:
  tight: "8"
  group: "16"
  section: "18"
  panel-padding: "22"
components:
  button:
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: "14,9"
    height: "38 minimum"
  panel:
    rounded: "{rounded.panel}"
    padding: "22"
  choice:
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: "14,9"
---

# Design System: TraceTime 0.2

## Overview

沿用 Windows 桌面操作习惯，以清晰的数据行、可区分的双时长和轻量导航呈现一天的应用使用。用户可以独立选择明暗模式、材质和强调色；默认首次跟随系统并使用液态玻璃，之后恢复已保存的选择。界面使用简体中文。

本文记录最终 WPF 实现，不是网页设计规范。尺寸均为 WPF DIP，双值 Padding 为水平、垂直。颜色 frontmatter 中 `*-seed` 为用户选择的原始颜色，不是所有主题中的固定呈现色。运行时语义画刷以 `ThemeManager.cs` 为准；材质和动态色规则另记于 `.impeccable/design.json`。0.1 设计已归档至 `docs/design-v0.1.md`。

提取依据：`MainWindow.xaml`、`Themes/Controls.xaml`、`ThemeManager.cs`、`MainWindow.Appearance.cs`、`TimelineView.cs`；核对最终演示截图 `artifacts/qa/release-0.2.0-99190c32`。独立审查复核已通过，范围及未验证项见 `docs/VALIDATION.md`。

## Colors

### Primary

预设为湖泊青、远山蓝、鸢尾紫、玫瑰粉、琥珀金、森林绿，也可输入六位十六进制色值或打开系统颜色选择器。主题引擎在浅色下向黑、深色下向白逐步调整原始颜色，使 `Accent` 与主表面对比度达到目标 4.8:1；原始选择保持存储不变。主要按钮使用 `AccentFill` 与自动选择的 `OnAccent`。

**选中项可读性规则：** `Choice` 选中态使用 `Ink` 文字、`AccentSoft` 底色和 `Accent` 边框，不把 `Accent` 当作所有彩色底上的小字号文字颜色。已验证 54 组主表面及实际选中控件组合。

### Secondary

排行与时间线按应用键的确定性哈希选择应用色，同一应用保持一致；与用户强调色分别管理。浅色应用色为青、靛蓝、赭、紫、绿、玫瑰和蓝；深色使用对应较亮色。颜色不代表分类或生产力，可能有不同应用共享颜色。

### Neutral

`Ink` 用于正文，`Muted` 用于说明和次级标签，`Border` 用于控件边缘，`Divider` 用于分区。`Field`、`Surface`、`Panel`、`Sidebar` 区分输入、普通表面、材质面板与导航。时间线另外使用 `Track`、`Idle` 和 `Gap`，配合文字说明。暂停及错误使用 `Warning` / `WarningSurface`，删除使用 `Danger`。

Windows 高对比度下，主题引擎替换为系统窗口、文本和强调色，并停用透明材质；不改变已保存的材质选择。该分支有实现，未通过真实系统设置切换的完整验收。

## Typography

正文与控件采用 Segoe UI，中文回退 Microsoft YaHei UI。页标题 28 / SemiBold；品牌 21 / SemiBold；分区 18 / SemiBold；正文 14；说明 12。总时长 35 / SemiBold，通过仅缩小的 Viewbox 适配窄列，不截断时长。时间线刻度使用 Segoe UI 10。

时长与占比右对齐，时长加粗；应用名和时间片名称可截断，保留完整工具提示。普通 TextBlock 继承所属控件前景，避免覆盖主要按钮及选中日期的文字颜色。不引入网页字体或假定固定行高。

## Layout

主窗口默认 1160 × 860，最小 900 × 620。左栏宽 200，内容区边距 30,26,22,18。顶部标题、记录状态和暂停按钮常驻；底部托盘提醒与主题摘要在滚动区之外。三页分别是每日概览、外观与主题、设置与数据。

每日概览按日期、两个并列时长面板、排行、可展开时间线排列。排行列包含序号、应用图标与名称、相对最长应用的比例条、时长和占比。数据条随可用列宽缩放；排行最大高度 310。时间片列表完整提供并虚拟化，最大高度 320，不截断记录数量。

外观页将明暗、材质、调色盘依次分组。三种风格各自有原生几何预览；明暗选择与材质选择分别互斥。较矮窗口通过垂直滚动访问下部内容，没有折叠侧栏或移动端断点。

## Elevation & Depth

没有自定义投影。玻璃通过内部半透明面板、细高光边缘和受强调色影响的背景渐变呈现层次；不读取桌面，不实现 Apple 平台的光学折射或系统背景模糊。正文所在面板保持较高不透明度。

柔和风格使用不透明表面和无面板边框；极简使用不透明表面、细分隔边缘与较小圆角。应用标题栏保留原生窗口行为，使用 DWM 颜色设置协调明暗。文件、颜色和确认对话框保留系统原生外观。

## Shapes

玻璃与柔和面板圆角使用 `panel`，极简使用 `panel-minimal`。按钮、选项与列表行按材质使用 `control` 或 `control-minimal`。主要时长面板等宽，面板统一内边距 22。应用比例条高 4、圆角 2。时钟与导航图标为 WPF 几何路径，不使用 emoji 作为图标。

## Components

- **按钮：** 默认 Field / Ink、1 DIP 边框，最小高度 38；悬停 Hover、按下 Pressed，键盘焦点强调边框 2 DIP，禁用降低透明度。主要按钮正常状态使用 OnAccent，悬停恢复 Ink。
- **选项：** RadioButton 保留原生互斥与键盘语义；选中同时改变背景、边框、字重，避免只靠色相。预设色提供颜色名称和工具提示。
- **输入：** 自定义 TextBox 与 DatePicker 模板，共用主题画刷。日期选择保留星期标题及月/年模式。非法色值在外观页就近提示，Enter 可应用；保存失败有说明。
- **导航：** 当前页以 AccentSoft 背景标记，三个页面可直接到达。侧栏提供快速浅/深切换；它会保存明确模式，若希望继续跟随系统可在外观页重新选择。
- **时间线：** 按真实日界和时间片绘制，空闲保留所属应用细线。点选排行应用过滤详情；清除选择恢复全部。空闲、暂停/中断和未记录均有说明。
- **外观存储：** `appearance.json` 位于当前数据目录；先保存到临时文件并替换，再应用视图。系统明暗事件会刷新跟随系统模式；坏配置安全回退默认。
- **动画：** 非演示模式在页面切换时使用 140ms、0.7→1 的轻淡入，遵循 Windows ClientAreaAnimation 设置；无持续动画。

## Do's and Don'ts

- **Do** 保留前台停留与扣除空闲后两种口径及回扣解释；主题变化不改写使用记录。
- **Do** 使用动态语义资源，让新增控件随主题变化；基于实际文字/背景组合检查对比度。
- **Do** 保留中文说明、键盘焦点、滚动可达性、完整时间片和对应空状态。
- **Don't** 把扣除空闲后的时长称为工作时长、专注评分或生产力。
- **Don't** 把窗口标题、网页地址、文档名、按键内容或截图写入用户使用记录。
- **Don't** 自动启用开机启动，也不要为主题测试改变用户的锁屏、电源或系统时间。
- **Don't** 将样例预览配色、原生系统对话框或未完整实测的系统主题行为写成全量已验证设计能力。

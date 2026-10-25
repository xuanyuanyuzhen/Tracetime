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

# Design System: TraceTime 0.4

## Overview

沿用 Windows 桌面操作习惯，以清晰的数据行、可区分的双时长和轻量导航呈现应用使用。0.3 增加应用每日额度、近 7/30 天本地分析和用户手动发起的可选 AI 回顾；0.4 统一应用图标，简化AI配置并标记有记录的日期。用户可以独立选择明暗模式、材质和强调色；默认首次跟随系统并使用液态玻璃，之后恢复已保存的选择。界面使用简体中文。

本文记录最终 WPF 实现，不是网页设计规范。尺寸均为 WPF DIP，双值 Padding 为水平、垂直。颜色 frontmatter 中 `*-seed` 为用户选择的原始颜色，不是所有主题中的固定呈现色。运行时语义画刷以 `ThemeManager.cs` 为准；材质和动态色规则另记于 `.impeccable/design.json`。0.1 设计已归档至 `docs/design-v0.1.md`。

设计依据：`MainWindow.xaml`、`Themes/Controls.xaml`、`ThemeManager.cs`、`MainWindow.Appearance.cs`、`TimelineView.cs`。0.3 局部更新另依据 `LimitReminderWindow.cs`、`AnalysisChart.cs`、`MainWindow.Analysis.cs`；沿用已完成的发布验证证据 `artifacts/qa/release-0.3.0-d704fd3f`。独立审查唯一历史应用名称问题已修复并复核 resolved / ship，结论限该次界面审查；本次文档更新不重新运行审查或测试。实际验证范围及限制见 `WORKLOG.md` 和 `docs/VALIDATION.md`。 0.4补充依据AppIconCache、AiConfigWindow、RecordedCalendar及实际发布证据 `artifacts/qa/release-0.4.0-4151246d`；40项场景通过，独立复核遗漏时间片图标的问题已resolved/ship。真实服务仍未实连。

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

主窗口默认 1160 × 860，最小 900 × 620。左栏宽 200，内容区边距 30,26,22,18。顶部标题、记录状态和暂停按钮常驻；底部托盘提醒与主题摘要在滚动区之外。五页依次为每日概览、应用时长限制、时长分析、外观与主题、设置与数据。

每日概览按日期、两个并列时长面板、排行、可展开时间线排列。排行列包含序号、应用图标与名称、相对最长应用的比例条、时长和占比。数据条随可用列宽缩放；排行最大高度 310。时间片列表完整提供并虚拟化，最大高度 320，不截断记录数量。

外观页将明暗、材质、调色盘依次分组。三种风格各自有原生几何预览；明暗选择与材质选择分别互斥。较矮窗口通过垂直滚动访问下部内容，没有折叠侧栏或移动端断点。

应用时长限制页先编辑应用、每日分钟数、统计口径与提醒强度，再显示今天的额度卡片。时长分析页先选择近 7/30 天和截止日，下方以互斥选项切换每日趋势、应用排行、本地摘要；可选 AI 回顾单独置于下部面板。两页均使用垂直滚动，输入及操作分组可换行。

## Elevation & Depth

没有自定义投影。玻璃通过内部半透明面板、细高光边缘和受强调色影响的背景渐变呈现层次；不读取桌面，不实现 Apple 平台的光学折射或系统背景模糊。正文所在面板保持较高不透明度。

柔和风格使用不透明表面和无面板边框；极简使用不透明表面、细分隔边缘与较小圆角。应用标题栏保留原生窗口行为，使用 DWM 颜色设置协调明暗。文件、颜色和确认对话框保留系统原生外观。

## Shapes

玻璃与柔和面板圆角使用 `panel`，极简使用 `panel-minimal`。按钮、选项与列表行按材质使用 `control` 或 `control-minimal`。主要时长面板等宽，面板统一内边距 22。应用比例条高 4、圆角 2。时钟与导航图标为 WPF 几何路径，不使用 emoji 作为图标。

## Components

- **按钮：** 默认 Field / Ink、1 DIP 边框，最小高度 38；悬停 Hover、按下 Pressed，键盘焦点强调边框 2 DIP，禁用降低透明度。主要按钮正常状态使用 OnAccent，悬停恢复 Ink。
- **选项：** RadioButton 保留原生互斥与键盘语义；选中同时改变背景、边框、字重，避免只靠色相。预设色提供颜色名称和工具提示。
- **输入：** 自定义 TextBox 与 DatePicker 模板，共用主题画刷。日期选择保留星期标题及月/年模式。额度使用带应用名称模板的 ComboBox，只列已有使用记录的应用，长名称提供工具提示；无记录时显示打开目标应用并刷新列表的指引。API 密钥在独立配置弹窗中使用原生 PasswordBox，关闭后清空输入。非法色值在外观页就近提示，Enter 可应用；保存失败有说明。
- **导航：** 当前页以 AccentSoft 背景标记，五个页面可直接到达。侧栏提供快速浅/深切换；它会保存明确模式，若希望继续跟随系统可在外观页重新选择。底部说明本地记录及 AI 由用户发起。
- **时间线：** 按真实日界和时间片绘制，空闲保留所属应用细线。点选排行应用过滤详情；清除选择恢复全部。空闲、暂停/中断和未记录均有说明。
- **应用额度：** 每日额度输入范围为 1–1440 分钟；前台停留与扣除空闲后使用互斥选项，轻度、中度、严重各有文字说明。规则卡片提供应用名、提醒强度、数值进度、5 DIP 高进度条、状态，以及编辑、暂停/启用、今天不再提醒和删除规则。进入额度页加载历史应用名称；不把今天没有使用的应用误标为已删除。
- **提醒窗口：** 轻度使用托盘通知和提示音。中度与严重使用原生 WPF 独立窗口，宽 480、最小高 340，随内容增高且最大不超过工作区高度；内容可滚动，内边距 28。中度置顶但不主动激活，严重请求前台焦点，被 Windows 阻止时闪烁任务栏。窗口显示应用、口径、已用时长与额度，须通过“知道了，今天不再提醒”或“再用 10 分钟提醒”结束；不会关闭或封锁目标应用。
- **本地分析：** 每日趋势为 WPF 绘图，高 210，以 Border 宽条表示前台停留、Accent 细条表示扣除空闲后；轴单位为小时，图例、悬浮数值和可展开的每日数值表共同解释数据。应用排行可切换口径；本地摘要为可选取的只读文本。没有新增图表库，无记录不解释为没有使用。
- **AI 回顾：** 主页面只显示当前服务摘要与“配置 AI 服务”。配置使用独立原生窗口，默认680×760、最小540×520、受工作区高度限制；保存/取消固定底部，表单可滚动。先选OpenAI官方、本机或兼容/自定义服务，再选基础地址或完整接口，模型可以显式获取列表或手填。名称可选，密钥使用PasswordBox与当前用户DPAPI，跨服务不自动延用旧密钥，取消不保存。
- **分析模板与数据：** 内部模板固定，用户配置/预览/复制不显示或提供修改提示词。默认匿名应用名，发送前只预览用户汇总，核对接口并同意后发送；配置或日期变化使预览失效并取消旧请求。模型列表请求只发送鉴权，不发送使用记录；用户运行包不提供单独的模板文件。
- **应用图标：** 概览和分析排行、额度选择/规则、提醒、普通与空闲时间片共用AppVisual/AppIconView；在名称旁放28DIP图标容器，图像24DIP。真实图标来自本地EXE资源，以32pxPNG缓存；后台单队列提取、内存与磁盘各256项、30天尝试刷新，缺失保留统一首字占位及名称。gap等非应用时间片不显示应用图标，图标资源不混入使用记录或AI数据。
- **记录日历：** 四个DatePicker一致使用HasRecord状态；有前台记录（包含空闲）用AccentSoft底色、Ink粗字和3DIP实心点，无记录用Muted但仍可选择。选中日继续使用AccentFill/OnAccent，今天保留边框；记录点和tooltip/automation help独立说明有无记录。跨日按本地日界、右端排除处理，新增/删除后刷新，非应用暂停空档不计为有使用记录。

- **外观存储：** `appearance.json` 位于当前数据目录；先保存到临时文件并替换，再应用视图。系统明暗事件会刷新跟随系统模式；坏配置安全回退默认。
- **动画：** 非演示模式在页面切换时使用 140ms、0.7→1 的轻淡入，遵循 Windows ClientAreaAnimation 设置；无持续动画。

## Do's and Don'ts

- **Do** 保留前台停留与扣除空闲后两种口径及回扣解释；主题变化不改写使用记录。
- **Do** 使用动态语义资源，让新增控件随主题变化；基于实际文字/背景组合检查对比度。
- **Do** 保留中文说明、键盘焦点、滚动可达性、完整时间片和对应空状态。
- **Do** 将 AI 外发保持为用户核对预览并明确同意后的手动动作；说明目标接口，保留取消和文本导出。
- **Don't** 把扣除空闲后的时长称为工作时长、专注评分或生产力。
- **Don't** 把窗口标题、网页地址、文档名、按键内容或截图写入用户使用记录。
- **Don't** 将额度提醒实现成结束或封锁目标应用，也不要把 AI 建议自动应用为额度规则。
- **Don't** 自动启用开机启动，也不要为主题测试改变用户的锁屏、电源或系统时间。
- **Don't** 将样例预览配色、原生系统对话框或未完整实测的系统主题行为写成全量已验证设计能力。

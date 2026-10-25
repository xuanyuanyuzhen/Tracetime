---
name: TraceTime
description: Windows 本地应用时长工具的现有 WPF 设计系统
colors:
  accent: "#24777B"
  accent-ink: "#174F52"
  navigation-ink: "#1E6569"
  navigation-selected: "#E7F1F1"
  control-hover: "#E8F1F2"
  control-pressed: "#D4E7E8"
  row-hover: "#EBEFF4"
  row-selected: "#E1EDEE"
  ink: "#1F2D3D"
  muted: "#59687A"
  version-ink: "#67778B"
  chevron: "#405266"
  canvas: "#F5F7FA"
  surface: "#FFFFFF"
  control-border: "#D6DFE7"
  field-border: "#C3CFD9"
  sidebar-border: "#DFE6ED"
  divider: "#DCE4EB"
  icon-tint: "#E8EEF3"
  paused: "#A87829"
  warning-surface: "#FFF0DA"
  warning-ink: "#784B09"
  destructive-ink: "#963C32"
  timeline-track: "#EDF1F5"
  timeline-gap: "#C8D1DA"
  timeline-idle: "#CBD5DB"
  timeline-tick: "#AAB7C3"
  app-indigo: "#576CA8"
  app-ochre: "#9B692F"
  app-purple: "#7F65A0"
  app-green: "#3F7F62"
  app-rose: "#A85D61"
  app-blue: "#386F98"
typography:
  display:
    fontFamily: "Segoe UI, Microsoft YaHei UI"
    fontSize: "36"
    fontWeight: 600
  headline:
    fontFamily: "Segoe UI, Microsoft YaHei UI"
    fontSize: "27"
    fontWeight: 600
  brand:
    fontFamily: "Segoe UI, Microsoft YaHei UI"
    fontSize: "20"
    fontWeight: 600
  title:
    fontFamily: "Segoe UI, Microsoft YaHei UI"
    fontSize: "18"
    fontWeight: 600
  date:
    fontFamily: "Segoe UI, Microsoft YaHei UI"
    fontSize: "16"
    fontWeight: 400
  body:
    fontFamily: "Segoe UI, Microsoft YaHei UI"
    fontSize: "14"
    fontWeight: 400
  label:
    fontFamily: "Segoe UI, Microsoft YaHei UI"
    fontSize: "12"
    fontWeight: 400
  caption:
    fontFamily: "Segoe UI, Microsoft YaHei UI"
    fontSize: "11"
    fontWeight: 400
  timeline-axis:
    fontFamily: "Segoe UI"
    fontSize: "10"
    fontWeight: 400
rounded:
  bar: "1"
  timeline: "4"
  row: "5"
  button: "6"
spacing:
  tight: "4"
  small: "8"
  control: "12"
  group: "16"
  section: "22"
  expanded-section: "24"
  large-section: "28"
  content-inline: "34"
components:
  button:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    typography: "{typography.body}"
    rounded: "{rounded.button}"
    padding: "16,9"
  button-hover:
    backgroundColor: "{colors.control-hover}"
  button-pressed:
    backgroundColor: "{colors.control-pressed}"
  button-primary:
    backgroundColor: "{colors.accent}"
    textColor: "{colors.surface}"
    rounded: "{rounded.button}"
    padding: "16,9"
  button-primary-hover:
    backgroundColor: "{colors.control-hover}"
    textColor: "{colors.accent-ink}"
  navigation:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.button}"
    padding: "16,12"
  navigation-selected:
    backgroundColor: "{colors.navigation-selected}"
  ranking-row:
    rounded: "{rounded.row}"
    padding: "12,10"
  ranking-row-selected:
    backgroundColor: "{colors.row-selected}"
  input:
    backgroundColor: "{colors.surface}"
    padding: "10,8"
  warning:
    backgroundColor: "{colors.warning-surface}"
    textColor: "{colors.warning-ink}"
    rounded: "{rounded.row}"
    padding: "12,9"
  timeline:
    backgroundColor: "{colors.timeline-track}"
    height: "112"
---

# Design System: TraceTime

## Overview

本文件记录 2026-10-25 已实现的 Windows WPF 界面。视觉基调为冷白背景、深蓝灰文字、青绿色交互与清晰的数据行；界面文案使用简体中文。原生窗口、日期控件、滚动条与系统对话框保留 Windows 行为。

**单位约定：** YAML 中尺寸字符串与本文尺寸均为 WPF 设备无关像素（DIP）；不是物理像素或网页 CSS。双值 Padding 按 WPF 的“水平,垂直”解释。字体粗细 600 对应 SemiBold，未自定义行高、字距或字体特性。

提取来源：`src/TraceTime.App/App.xaml`、`MainWindow.xaml`、`TimelineView.cs`、`MainWindow.xaml.cs` 中的 Palette 与状态逻辑；方向依据 `PRODUCT.md`、`docs/overview-design.md`。已对照 `artifacts/qa/overview.png`、`overview.settings.png`、`overview.details.png` 的实际渲染。运行验证范围见 `WORKLOG.md`。

## Colors

### Primary

- `accent` 用于主要操作、扣除空闲后的总计、记录状态点、键盘焦点和时钟标记。
- `control-hover`、`control-pressed` 表示按钮悬停与按下；主要按钮悬停时文字使用 `accent-ink`。
- `navigation-selected` 标记当前导航，`row-selected` 标记选中的应用。当前“每日概览”导航文字固定使用 `navigation-ink`；“设置与数据”沿用正文颜色，选中状态通过背景显示。

### Secondary

- 应用颜色依次为 `accent`、`app-indigo`、`app-ochre`、`app-purple`、`app-green`、`app-rose`、`app-blue`。Palette 按应用键的确定性哈希选择颜色，同一应用在排行条与时间线上保持一致；颜色不是固定类别标签，也不保证不同应用颜色唯一。
- `paused` 用于暂停状态点；状态同时有文字说明。
- `warning-surface` / `warning-ink` 用于错误提示条；`destructive-ink` 用于删除操作文字。

### Neutral

- `canvas` 为主内容背景，`surface` 为侧栏与普通控件背景。正文使用 `ink`，辅助说明、列头与时间标签使用 `muted`。
- 控件、输入框、侧栏与分隔线分别使用相应 border / divider 颜色。
- 时间轴使用 `timeline-track` 作为底轨，`timeline-idle` 标记空闲，`timeline-gap` 标记已知中断。空闲段顶部仍保留对应应用的细色线；未记录区间保留底轨。

## Typography

窗口字体为 Segoe UI，回退为 Microsoft YaHei UI。时间轴绘制的数字刻度直接使用 Segoe UI。字号令总时长最突出，说明文字保持次级层次。

| 角色 | 用途 |
| --- | --- |
| display | 两个总时长，SemiBold |
| headline | 每日概览、设置与数据的页标题 |
| brand | 左上角 TraceTime 名称 |
| title | 应用排行、设置分区、空状态标题 |
| date | 当前日期说明 |
| body | 按钮、应用名、总计标签及主要表单文字 |
| label | 辅助说明、状态、排行列头和时间片详情 |
| caption | 页脚、时间线图例、完整列表提示 |
| timeline-axis | 每日时间轴刻度 |

应用时长采用 SemiBold 并右对齐，占比保持常规字重。应用名称和时间片标签允许省略号，并提供完整文本的工具提示；长说明与路径允许换行。

## Layout

主窗口声明尺寸为 1080 × 810，最小尺寸为 900 × 620。左侧导航固定宽 196，右侧填充剩余空间；未实现移动端、断点切换或折叠侧栏。缩小窗口时通过内容区垂直滚动保留可达性。

侧栏内边距为左 16、上 28、右 16、下 20。主内容外边距为左 34、上 26、右 34、下 20。页标题、记录状态和暂停按钮位于固定顶部区域；底部托盘提醒位于滚动区之外。

两个总计使用等宽列，右列左侧留 28。分区通过留白与一像素分隔线区分。排行应用列弹性伸缩，时长列宽 108，占比列宽 65；图标列宽 42。排行列表最大高 312。

时间线位于可展开区域，展开后包含应用选择说明、时间轨、图例及完整时间片列表。详情列表最大高 320，范围列宽 155、时长列宽 90、应用标签列填充。概览与设置各自使用 ScrollViewer。

## Elevation & Depth

应用自定义界面没有阴影、渐变或浮起动效。层次来自白色侧栏、浅色主背景、细边界和选中态底色。控件状态立即变化；日期弹窗、系统消息框及托盘菜单采用 Windows 自身的呈现。

## Shapes

按钮采用轻圆角，排行行、图标底板和错误条采用稍小圆角。排行条高度为 3，图标底板为 28 × 28，应用图标为 24 × 24。时间轨高度为 32，底轨圆角由 `rounded.timeline` 指定，各时间片用矩形绘制。

普通按钮和输入框边框为 1；按钮键盘焦点将边框变为青绿色并加粗到 2。排行行键盘焦点使用 1 像素青绿色边框。状态点为直径 7 的圆形。未统一改造原生输入控件的圆角。

## Components

### Buttons

普通按钮为白底深色文字，最小高 36；主要按钮用于“保存偏好”。悬停、按下和键盘焦点采用 frontmatter 与 Shapes 中的状态。禁用按钮透明度为 0.5。导航按钮无常态边框，文字左对齐；“清除选择”为最小高 26、内边距 8,3 的紧凑按钮。

### Inputs / Fields

空闲阈值使用宽 80 的 TextBox，旁边显示分钟单位；验证反馈在保存按钮下方。DatePicker 保留原生外观和短日期格式，最小高 36；概览日期控件宽 142，数据范围控件宽 150。统计口径使用两个原生 RadioButton，启动偏好使用 CheckBox。未实现额外的自定义输入焦点或错误描边。

### Navigation

左侧仅有“每日概览”和“设置与数据”。选中页使用浅青底色。关闭主窗口后隐藏到托盘；托盘菜单顺序为“打开 TraceTime”“设置”“暂停记录 / 恢复记录”、分隔线和“退出”。托盘“设置”入口直接显示设置页。

### Application ranking

应用行包含图标或首字回退、名称、短比例条、时长和占比。比例条最大宽 200，以当前排行的最大时长归一化；占比以当日当前口径的全部应用总时长计算。选择应用后，时间线突出相应区间并筛选详情；清除选择恢复全部应用。

### Empty states

排行总时长为零时隐藏列表与列头，显示居中的标题和可换行说明。保留日期、口径及时间线入口。

| 情况 | 标题与行为 |
| --- | --- |
| 今天没有记录 | “今天的记录，从现在开始”；正常记录时提示自动累积，暂停时提示恢复后继续累积 |
| 历史日期没有记录 | “这一天没有记录”；说明未运行的时段无法补记，并提示换日期或回到今天 |
| 前台有记录，扣除空闲后为零 | “扣除空闲后，暂无时长”；说明已记录前台时间均判为空闲，可切换到前台停留查看 |

### Daily timeline and details

时间轴映射所选本地日期的实际日界，约每三个小时绘制刻度；末端标记 24:00。选中应用后，其他区间以 0.2 透明度显示。指针悬停提供时间范围、应用或中断标签与时长；图例明确彩色、浅灰、灰块和未记录的含义。

完整详情使用启用 Recycling 虚拟化的 ListBox，按开始时间倒序显示全部匹配时间片，不截断为固定条数。可滚动并使用方向键、Home / End 浏览；每行的自动化名称包含开始、结束、标签、空闲说明和时长。列表下方显示完整条数；无时间片时显示“暂无时间片记录。”。时间轴悬浮信息可通过该文本列表查看。

## Do's and Don'ts

- **Do** 延续上述字体、数据列对齐、青绿色状态与轻圆角，新增原生控件时保留 Windows 操作习惯。
- **Do** 始终分开显示前台停留与扣除空闲后的时长，并保留空闲判定的解释。
- **Do** 给图表状态配文字图例，给可截断的名称保留完整文本入口，给长列表保留键盘与滚动访问。
- **Do** 根据今天、历史日期及全空闲情况使用对应空状态。
- **Don't** 把空闲等同于未工作，或把扣除空闲后的指标改名为生产力、工作或专注评分。
- **Don't** 在普通使用记录或界面详情中增加窗口标题、网址、文档名、键盘内容或截图。
- **Don't** 通过固定截断时间片数量来缩短页面；使用现有滚动与虚拟化布局。
- **Don't** 将本文件的 DIP 数值直接视为物理屏幕像素，或声称已经实现未提供的深色主题、移动布局和动画系统。

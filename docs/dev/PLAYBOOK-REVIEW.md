# TraceTime Playbook 可行性核实

日期：2026-11-25。结论：**可以继续优化，但应先纠正现状假设，再按小范围实施。暂不应直接重写计时核心或启动完整 UI / 双进程改造。**

实施更新：用户随后授权开始修改。AOCI已完成129条首次索引；首批WS1/WS2/WS4的就绪切片已进入本地交付，场景增至118项，详情以WORKLOG、VALIDATION、PERFORMANCE为准。下文“本轮不改产品/索引尚未构建”等描述仅属于原评估阶段。时间语义现状与未解决的回拨重叠见[ADR 001](TIME-MODEL-ADR.md)。该批切片不等于WS1–WS8全部完成。

发布更新（2026-11-26）：首批切片与0.8.2启动项修复已随`fe429e6`公开为v0.8.3（Latest）。提交后118/118、正式包隔离回归和匿名附件下载校验通过；GitHub CI未出现运行记录，手动触发HTTP 502，不能视为云端通过。下表未完成范围继续有效。

## 0.8.3发布前复核（2026-11-25）

重新对照原Playbook、当前差异及验证证据：**此前确定的首批切片已完成，可以作为0.8.3独立发布；整份Playbook没有全部完成。** 用户本次明确授权核实后提交并发布，原附件的“不要推送main、不要创建Release”不覆盖这次授权。

| 范围 | 当前状态与证据 | 尚未完成的原文范围 |
| --- | --- | --- |
| WS0 | 11项假设已用源码核实；完整AOCI现为135条；本地118场景和隔离包通过 | 全页100%/150% DPI矩阵及长期基线 |
| WS1首批 | build.ps1默认locked-mode且run/publish禁隐式恢复；Windows CI和Dependabot配置存在；本次执行提交、CI核实和手动Release交付 | tag自动草稿流水线、许可证决定、安装器ADR/虚拟机验收、签名、winget、README整体重排 |
| WS2首批 | 保留已有UTC/单调样本；6组新增固定规则场景和TIME-MODEL-ADR；不修改真实时钟 | 固定发生时偏移、历史时区归属、回拨重叠解决及迁移；未引入FsCheck，使用固定种子的200组场景 |
| WS3 | 源码已核实既有子窗口解析、有限进程查询和Unknown保留 | 稳定身份规则、别名与可撤销合并、dry-run迁移及对应UI |
| WS4首批 | BuildSummary/GetDaySummary共用聚合，CheckLimits接入；4组等价/更新/提醒/分配场景；90天合成查询分配下降21.8% | 持久化汇总表与RebuildSummaries、迁移框架、自动快照、启动quick_check；短测未证明常驻资源改善 |
| WS5–WS7 | 保留现有六页WPF功能与默认值 | 新UI、双进程原型/关卡、空闲标签及分类未实施 |
| WS8首批 | README、性能、验证、ADR、规划、日志与索引随实际范围更新 | 英文README、UI资源化、FlaUI、手动性能workflow |

发布前依据：`artifacts/qa/version-0.8.3-20261125/`中118/118、版本、隔离包和包校验记录；正式提交重建及远端证据另存`artifacts/qa/release-0.8.3-20261125/`。没有把前期配置存在等同于远端CI通过，也没有把纯汇总读取说成已消除区间查询。

## 范围与依据

- 输入：`C:\Users\xuanyuan\Downloads\TraceTime-Agent-Playbook.md`。原文说明作者只读过 README 与路线图，其实现判断是待验证假设。本报告保留原 WS 编号，但建议不等于已授权的产品变更。
- 代码基线：HEAD `1b1505d3a7a5699d1151df07bfeb4f1893642b9c` 加现有 0.8.2 未提交修改。版本和交付状态以 [WORKLOG](../../WORKLOG.md) 为准；本轮没有查询或更改 TraceTime 的远端发布状态。
- 已读项目规则、规划、产品/设计资料、性能/验证记录，并对采集、核心、存储、提醒及现有测试逐项定位。`.impeccable/design.json` 是设计归档，不应删改。
- 本轮完成 AOCI 初始化与本地接入诊断，详见 [AOCI 接入](AOCI.md)。语义索引尚未构建，以下判断来自源码和本轮测试，不能称作 AOCI 完整认知结论。

## 附录 A 的 11 项假设

| # | Playbook 假设 | 核实结果 | 实际证据 |
| --- | --- | --- | --- |
| 1 | 区间用本地 DateTime 存储并直接相减 | 不成立。区间为 DateTimeOffset，写库为 UTC ticks；引擎由单调时间推算区间终点，汇总再对已保存的 UTC 边界求差 | [Models](../../src/TraceTime.Core/Models.cs)、[TrackingEngine](../../src/TraceTime.Core/TrackingEngine.cs) `At/Observe`、[UsageDatabase](../../src/TraceTime.Storage/UsageDatabase.cs) `InsertUsage/Stamp` |
| 2 | 没有单调时钟或时钟抽象 | 核心假设不成立。采集使用 Stopwatch；`TrackingSample` 显式携带 UTC、MonotonicMs 和 IdleMs，测试可注入。虽没有名为 IClock 的接口，不等于不可测试 | [ForegroundReader](../../src/TraceTime.App/Windows/ForegroundReader.cs) `Read`、[TrackingEngine](../../src/TraceTime.Core/TrackingEngine.cs)；偏差超过 2 秒或观测间隔超过 15 秒会切换 scope |
| 3 | 应用身份按完整路径区分 | 基本成立。通常以小写完整路径为 key；路径不可用时回退 `process:<name>`。含版本号的路径更新后可能拆成不同应用 | [ForegroundReader](../../src/TraceTime.App/Windows/ForegroundReader.cs) `ReadApp` |
| 4 | UWP 统一显示 ApplicationFrameHost | 不成立为普遍判断。已枚举可见子窗口查找不同 PID，再解析实际进程；无 AUMID 兜底，失败情形仍值得补测 | 同上，ApplicationFrameHost 分支 |
| 5 | 取不到受保护进程路径就丢弃 | 不成立。已使用 `OpenProcess(0x1000)`，失败后尝试进程名，异常返回 Unknown；核心仍为该身份累计 | 同上 `ProcessPath/ReadApp`、[Models](../../src/TraceTime.Core/Models.cs) `AppIdentity.Unknown` |
| 6 | 趋势、日历、排行读取区间表 | 成立，但不等于每次都做全表扫描。查询有日期范围和索引；Analyze 一次加载范围后逐日构建，日历有缓存；没有持久化汇总表 | [UsageDatabase](../../src/TraceTime.Storage/UsageDatabase.cs) `GetDay`、[Limits](../../src/TraceTime.Storage/UsageDatabase.Limits.cs) `Analyze`、[Calendar](../../src/TraceTime.Storage/UsageDatabase.Calendar.cs)、[RecordedCalendar](../../src/TraceTime.App/RecordedCalendar.cs) |
| 7 | 活动区间只在内存，崩溃全丢 | 不成立。每 5 次 Tick 尝试 Flush，常规约 5 秒；同 id upsert，重要状态边界也刷新。不是强实时或掉电保证 | [TrackerController](../../src/TraceTime.App/Windows/TrackerController.cs) `Tick/ApplyMode/Flush`、[TrackingEngine](../../src/TraceTime.Core/TrackingEngine.cs) `Flush`、[Program](../../tests/TraceTime.Tests/Program.cs) 检查点测试 |
| 8 | 只有手动备份 | 部分成立。常规备份由用户触发，但恢复前已有自动安全备份；备份用读事务固定 WAL 快照。没有周期自动快照策略 | [Archives](../../src/TraceTime.Storage/UsageDatabase.Archives.cs) `CreateHistoryArchiveCore/RestoreHistoryArchive` |
| 9 | 采集、提醒、邮件、托盘同一 WPF 进程 | 成立。托盘使用 WinForms NotifyIcon，单实例使用命名互斥量；Core/Storage 可独立复用 | [App](../../src/TraceTime.App/App.xaml.cs)、[MainWindow](../../src/TraceTime.App/MainWindow.xaml.cs)、[ReminderEmailService](../../src/TraceTime.App/ReminderEmailService.cs) |
| 10 | 前台检测只有轮询，没有窗口事件 | 不成立。已有 `SetWinEventHook(3,3,...)`，另以 1 秒定时器兜底 | [TrackerController](../../src/TraceTime.App/Windows/TrackerController.cs) 构造函数 |
| 11 | 图表自绘而非第三方库 | 成立。使用 WPF DrawingContext 等原生绘制 | [TimelineView](../../src/TraceTime.App/TimelineView.cs)、[AnalysisChart](../../src/TraceTime.App/AnalysisChart.cs)、[HoverChart](../../src/TraceTime.App/HoverChart.cs) |

## 按 WS 修订实施方向

| WS | 建议 | 原因与验收重点 |
| --- | --- | --- |
| WS0 基线 | 假设核实和当前构建已完成；全套基线尚未完成 | 现有测试是 108 项，不是 98。需在真正改 UI/性能前补齐 100%/150% DPI × 明暗全页面，以及分析页、隐藏 10 秒和长期资源数据 |
| WS1 工程 | 优先准备 Windows CI、锁定恢复与独立包验证 | 已有 global.json、四份 packages.lock.json、构建/包验证脚本；缺 `.github` 工作流。CI 应使用锁定恢复，避免脚本随后普通 restore 破坏意图。WPF 自动验证在托管 runner 的能力须先验证；安装器/签名/许可证另作决策 |
| WS2 时间 | 补缺失场景和时间语义 ADR，保留既有 UTC/单调核心 | 已有前跳、跨午夜、春季 23 小时测试。补秋季 25 小时、回拨 1 小时、小幅 NTP 回拨、时区切换及休眠叠加。当前查询按传入时区、UI 按当前 Local 分日，改为“发生时偏移固定归属”会改变历史展示，必须先决定 |
| WS3 身份 | 优先可撤销的别名/查询映射，再评估版本路径规则 | 跨版本路径拆分是实际缺口。`产品名 + exe名` 仍可能碰撞，不能视为天然唯一键；不自动重写旧键，不自动合并。UWP 改为补充失败路径和可模拟测试，不重复实现已有子窗口解析 |
| WS4 汇总与保护 | 先测提醒汇总读路径，再决定缓存或汇总表 | 启用额度后，隐藏窗口仍会通过 `CheckLimits` 读取今日完整报告。先覆盖纯汇总读路径、空闲回扣和失效机制；若引入表，再完善迁移与前置快照。现有约 5 秒检查点不应放慢成 30–60 秒 |
| WS5 UI | 先单页试点和功能映射；完整替换为待确认方向 | 当前六个入口及玻璃/柔和/极简为既有产品约定。四页导航、删除材质和图表属于产品选择。可先处理布局/无障碍和按需视图，需真实界面检查，不能仅按文档宣布视觉问题 |
| WS6 双进程 | 后置，先测原型再作 go/no-go | IPC、提醒拉起、启动项、安装升级、写入归属会同时变化。现有约 94 MiB 是 0.8.1 某次短测值，不能当本轮结果或保证拆进程节省 30% |
| WS7 可选功能 | 后续独立需求 | 空闲标签必须不改两种时长。分类、目标不是当前性能或正确性问题的前置条件 |
| WS8 文档与测试 | 随实际切片同步 | 复用已有确定性测试与应用内验证；本地化可从文案资源准备起步。FlaUI 不是已有测试体系的替代，新增依赖需证明价值 |

### 需要特别修正的技术判断

1. **时间存储无需重新从本地时间迁到 UTC。** 真实缺口是是否持久化发生时偏移，以及回拨后的时间线重叠/日归属语义。现有检测会重新锚定墙钟；不能仅凭“检测到跳变”推断所有回拨场景已验证。先用模拟样本测量，不改系统时间。
2. **已有 schema 版本号，但没有完整版本迁移框架。** `InitializeLimits` 按列补齐后写 `PRAGMA user_version=3`，不是按旧版本逐步迁移并自动快照。后续改 schema 时应补高版本拒绝、事务和失败恢复，不能宣称当前完全没有版本号。
3. **后台计时和提醒不应随隐藏窗口停掉。** 当前视图刷新已经按可见性跳过，但采集、休息和额度仍需运行。Playbook 的“隐藏后没有任何定时刷新”要限定为视图/动画/资源采样；否则会破坏后台行为。
4. **“隐藏 10 秒回到托盘基线 ±5 MiB”尚无实现或测量依据。** WPF 窗口与资源仍可能保留。先有按需创建/释放的原型与功能对照，不用强制 GC 或裁剪工作集制造数字。
5. **升级启动项问题已在本地 0.8.2 修复。** WS1 安装器方案应复用现有接续语义，不能再描述成“必须重新勾选”；真实重启行为仍按既有验证边界保留。

## 建议的首轮切片

1. **工程验证自动化**：先让 Windows CI 跑锁定恢复、108 项及新场景；隔离包验证通过后，再准备草稿发布流水线。无需先决定安装器或许可证，也不自动公开 Release。
2. **时间回归补强**：固定时区/模拟时钟覆盖上述缺口，输出 ADR；不改 schema，先把现状和期望差异测清楚。
3. **提醒汇总性能试点**：合成 90 天数据，分别测无规则/有规则的托盘、可见和最小化；统计 GetDay 调用、分配、CPU 与读写量。优先单独的汇总查询或缓存，只有收益成立再设计持久化汇总表。

三项都要维持原有双口径和空闲回扣。应用身份映射随后单独切片，避免与统计缓存同时改动造成归因困难。完整 UI 和双进程留在前述关卡之后。

## 待决项

这些是实施相应功能之前的决策点，不阻塞本轮工具接入与评估：

- 历史日期是否随当前时区显示，还是固定到记录发生时；影响 WS2、汇总桶、归档和趋势。
- 是否替换四页导航、移除已承诺材质/图表；影响 WS5 的设计范围。
- 许可证、安装器、签名投入、更新检查；影响 WS1 的分发方案。
- 自动快照的默认开关与保留量；新增常驻周期任务需测量。
- 双进程的投入上限与 go/no-go 阈值；仅在原型数据齐备后决定。

## 本轮验证与边界

- `scripts/build.ps1 -Test` 已完成：0 警告、0 错误、108/108，通过日志为 `artifacts/qa/aoci-20261125/build-test.txt`。
- 独立目录发布构建已完成：`scripts/build.ps1 -Publish -Offline -ReleaseRoot artifacts/release/aoci-review-20261125`。用于本轮验证，不代表发布了新版本。
- `scripts/verify-release.ps1 -ReleaseRoot artifacts/release/aoci-review-20261125` 已通过：8 项采集流程、单实例、主题/导航、模拟 AI/音频/邮件、历史恢复、休息、性能行为、配置重启与启动项迁移。日志 `artifacts/qa/aoci-20261125/verify-release.txt`；详细结果 `artifacts/qa/release-0.8.2-596557e0`。只验证既有行为，不代表性能提升或完成截图视觉审查。
- 文件哈希检查确认原有 `src/`、`tests/`、`scripts/` 和构建项目内容未改变；原 README、PRODUCT、VALIDATION 的未提交内容也原样保留。新文档相对链接与 `git diff --check` 通过。
- 本轮不改产品代码、不调整默认功能、不迁移真实数据库、不切换系统时区/DPI、不锁屏或休眠，不提交、推送或创建 Release。
- 未执行完整 DPI/明暗截图矩阵、90 天性能试点、长期常驻或真实重启测试。因此本报告是 **WS0 的源码核实与局部基线**，不是 WS0 或 WS1–WS8 的完成声明。

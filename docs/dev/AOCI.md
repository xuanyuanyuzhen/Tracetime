# AOCI 工作区接入

日期：2026-11-25。此工具用于开发时维护代码认知，不是 TraceTime 的运行依赖，不进入应用发布包。

## 当前状态

- 本机使用 `E:\aoci\aoci_0.1.1_windows_amd64\aoci.exe`，版本 0.1.1，提交 `a9b803fa47ac2a9c3a2da48209c9030db513612d`。
- 已从官方 v0.1.1 Release 重新获取校验清单；本地 ZIP SHA-256 为 `3c9eb072d43ba19184cab3fb1ff704290d88c6398f34f8d3cee10c8ff480b9b6`，解压 EXE 与 ZIP 内容一致。完成 basic 校验，未验证 Cosign 签名或构建 provenance。
- 项目级 Codex 配置已写入且服务已在本工作区会话加载；实际 MCP 收据报告 `0.1.1`，仓库根为 `E:\AI_Code\Tracetime`。独立握手诊断与当前工具调用均已成功。
- 首次完整语义索引已构建：**129/129 个可索引文本对象**，覆盖应用、核心、存储、测试、脚本、配置与文档。26 张二进制截图按规则跳过，不计入文本覆盖率；未建立用户数据库连接。
- Playbook首批优化后索引增量更新为**134/134个可索引文本对象**，新增CI/Dependabot、时间ADR与两份回归测试。19个变更对象按完整机器批次15+4提交，Verify→Check→Guide已证明对齐；最终文档收尾证据位于`artifacts/qa/optimization-20261125/aoci-*-final.json`。
- 已依次通过 Verify、Aggregate Check 与 Guide：结构有效、治理对齐，missing/stale/unbaselined/orphan 均为零，Guide 返回 `complete=true`、`next_action=none`。129 条规模标签全部匹配，预算健康。文档同步后的最终收尾证据另存 `index-*-final.json`。
- 额外核对了260条代码关系引用，修正5处错误文件路径后全部指向现存文件。此检查证明路径存在，不代替修改前阅读相关源码。源码与测试等148个原有文件的字节哈希保持不变。

## 资产与边界

| 路径 | 用途 | Git |
| --- | --- | --- |
| `aoci.txt` / `aoci.meta.txt` / `aoci.code.txt` | Root、规则字典、文件级语义索引 | 保留入库资格 |
| `.aoci/config.json` / `.aoci/baseline.json` / `.aoci/.gitignore` | 团队策略、扫描基线和资产白名单 | 保留入库资格 |
| `AGENTS.md` 的 AOCI 托管区块 | 后续 Agent 的工具入口与维护约定 | 保留入库资格 |
| `.gitattributes` | 初始化器生成的 LF 检出约定，稳定基线的原始字节；本轮未批量转换文件 | 保留入库资格 |
| `.codex/config.toml` | 本机 EXE 与仓库的绝对路径 | 忽略，不跨机器提交 |
| `.aoci` 其他运行状态、`artifacts/qa/aoci-20261125/` | 本机诊断、运行记录和验证输出 | 忽略 |

选择 `full` 范围以覆盖测试和设计文档。此配置不能只依赖 Git ignore：首次预览曾纳入本地 SDK 和生成目录，已在建立基线前通过 AOCI 配置显式排除：

- `.tools`、`.packages`、`bin`、`obj`、`.vs`、`.codex`；原有根目录 `artifacts` 等排除继续保留。
- 数据库/WAL/SHM、`.ttarchive`、`.env`、证书、服务设置、本机用户配置，以及 `scripts/save-global-preference.ps1`。
- `.impeccable/design.json` 保留，是现有设计归档，未删除或改写。

`ai.enabled=false`，没有增加模型端点或 API 密钥。TraceTime 使用 SQLite；本轮只从源码了解 schema，没有为用户 `usage.db` 配置 AOCI 数据库连接，也没有读取其中的使用记录。

## 日常接续

本工作区已可直接使用 AOCI。新会话先建立与当前仓库、服务及索引身份相符的认知；如果工具未加载，重新打开工作区或重启宿主，再核对项目配置。

给接手会话的指令：

> 先读 WORKLOG.md、PLAN.md 和 AGENTS.md。确认本工作区的 AOCI 工具已加载，调用 aoci_rules 与完整 aoci_overview，按原样游标完成交付并确认、作答。上下文压缩后按 AGENTS 的 context_compaction 规则重新读取，不用旧摘要替代当前认知。保留现有 0.8.2 未提交改动；后续优化依据 docs/dev/PLAYBOOK-REVIEW.md。

纯读取不需要维护。受管理文件修改到最终稳定状态后，按当前 Guide 调用 `aoci_maintain`，阅读绑定源码并提交完整机器批次；有剩余则继续下一批。最后依次 Verify、Check、Guide。不要对已对齐且无新变化的索引重复维护。索引条目的语义由模型理解真实证据后编写，不能按路径或模板生成。

日常接入诊断命令（在仓库根执行）：

```powershell
$taskAoci = 'E:\aoci\aoci_0.1.1_windows_amd64\aoci.exe'
& $taskAoci --repo . doctor
& $taskAoci --repo . index agent guide --agent codex --json
codex mcp get aoci --json
```

换机器时，将验证过的 AOCI 放到稳定位置，再用该机器的路径运行 `init --agent codex`。不要复制本机 `.codex/config.toml`。已有基线的范围修改按实时 Guide/Scope 流程处理，不用 `scan --force` 清除状态。

## 证据与来源

本机证据位于 `artifacts/qa/aoci-20261125/`：`verification.json`、`scan.json`、`doctor.json`、`codex-mcp.json`、`mcp-handshake.json`、`guide.json`。`init.json` 和 `doctor.json` 是 CLI 原样输出，虽然使用了 `--json`，该版本仍返回人读文本，不应当作 JSON 解析。

索引构建证据：`index-verify-initial.json`、`index-check-initial.json`、`index-guide-initial.json`；关系修正后为 `index-*-relations.json`；文档最后同步后为 `index-*-final.json`。`index-relation-audit.json` 保存引用存在性结果，`index-workspace-check.json` 保存148个原有文件的哈希比对。产品未因索引构建而改动，沿用此前108/108场景与隔离发布包验证，不把它们冒充本轮重跑结果。

核对过的官方来源：

```text
https://github.com/aoci-spec/aoci-code
https://github.com/aoci-spec/aoci-code/releases/tag/v0.1.1
https://github.com/aoci-spec/aoci-code/blob/v0.1.1/docs/install.md
https://github.com/aoci-spec/aoci-code/blob/v0.1.1/docs/managed-scope-and-budget.md
https://developers.openai.com/codex/mcp/
https://modelcontextprotocol.io/specification/2025-06-18/basic/transports
```

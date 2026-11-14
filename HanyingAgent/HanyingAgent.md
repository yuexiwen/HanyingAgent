# HanyingAgent

## 功能与定位

面向学习的 Agent Runtime，采用单个 .NET 10 Console Project、单进程的模块化单体结构，通过明确契约为后续远程适配保留边界。

当前已定义 Session、Task、User、Conversation、Memory、Tools、LanguageModels 与 Context 契约；
已实现工作记忆、Context Builder、Ollama 适配器，以及 Generic Host / DI 驱动的本地会话、有界多轮对话和后台 Direct 任务。
WorkingMemory 已建立内部存储端口、正式 xUnit 测试项目、测试用可控时钟，并实现进程内存 Store 和工作记忆 Manager。
当前已分别验证 Store 与 Manager 的独立行为，并增加真实组合的跨层边界与并发测试。
五步现已完成，内存版 WorkingMemory MVP 此前已通过统一验收：252 个正式测试全部通过，构建 0 警告、0 错误。
模块可供后续调用方通过公共接口使用，但未在程序入口接入记忆链路。
数据不持久化，自动提取与评分、身份授权、容量治理和长期记忆仍未实现。

Context 五步最小版已完成：接口、纯文本请求组装、明确归属的工作记忆接入、总输入字符上限和真实组件组合验收，
共 57 项独立测试与 20 项真实组合测试通过。字符上限默认 32,000，可配置；精确 Token 预算仍未实现。
Context 阶段的历史统一验收：全项目 329 项测试通过、0 失败、0 跳过，解决方案构建 0 警告、0 错误。

Ollama 适配器实现 ILanguageModelRuntime，使用 HttpClient 调用原生 /api/chat，支持纯文本非流式请求，
映射结束原因与 Token 用量，工具及非法协议明确拒绝，不自动重试、下载或启动模型服务。
2026-11-10 针对性验收：89 项模型适配测试与 77 项 Context 回归共 166 项通过，
构建 0 警告、0 错误，并通过实际适配器调用本机 qwen2.5:1.5b 验证合成问题；其余 252 项历史测试本轮未重跑。
模型调用能力通过任务编排、对话协调和 DirectExecutor 接入 Console；已有会话历史和最小任务生命周期，
尚无 Agent 工具循环、等待用户或恢复。
使用方式与限制见 [模型适配器](./SRC/Infrastructure/LanguageModels/LanguageModels.md)。

## 本地运行

先准备本地 Ollama 服务及模型，再从仓库根目录运行：

```powershell
ollama pull qwen2.5:1.5b
dotnet run --project .\HanyingAgent\HanyingAgent.csproj
```

启动时创建可信本地用户与 Default 会话；正常输入立即返回 TaskId，当前会话的结果完成后自动显示，无需 `/wait`。
其他会话完成时显示带会话和任务标识的通知，切换回该会话后自动展示未读更新。
后台运行时可以查询、取消任务或切换会话；同一会话最多一个未结束任务。
`/exit`、EOF 或 Ctrl+C 退出，Host 请求取消所有运行任务并等待收尾，期限为 10 秒；
不支持退出后继续运行。所有数据只在本次进程内存在，重启不会恢复。
入口统一使用 UTF-8 输入与输出，支持中文；通过管道输入时，发送端也需要使用 UTF-8。
Windows PowerShell 5.1 的管道默认编码可能丢失中文，可先执行 `$OutputEncoding = [System.Text.UTF8Encoding]::new($false)`。
每轮包含系统指令、选中的历史和一次当前输入，不自动读取或写入 WorkingMemory。
默认最多携带最近 10 个正常完成问答片段；受字符预算限制时淘汰更早完整片段，但保留原始历史记录。
截断、未知、拒绝、过滤和未回答的旧输入不进入后续模型上下文；最近最多扫描 200 条记录。
系统指令与当前输入本身超限则明确失败，不截断当前问题。
模型返回 Length、过滤、拒绝、工具调用或未知结束原因时明确显示状态，不伪装成完整回答。
连接、协议或输入错误会显示失败并允许继续输入；超时与用户主动取消分别处理。

### 会话命令

| 命令 | 功能 |
|---|---|
| `/new 名称` | 创建并选择新会话。 |
| `/session [完整ID]` | 查询指定或当前会话详情，不改变当前选择；显示任务状态及是否结束。 |
| `/sessions` | 列出本地用户的会话、任务状态及是否结束，星号标识当前会话。 |
| `/use 完整ID` | 切换到指定会话并显示详情，不自动执行或发送历史。 |
| `/history` | 查看当前会话最近 20 条消息及实际结束原因。 |
| `/history 游标` | 查看提示中指定的更早一页。 |
| `/tasks [游标]` | 当前会话的最近任务，默认 20 条，可向更早记录翻页。 |
| `/task [完整TaskId]` | 查询状态、版本及已保存结果。 |
| `/wait [完整TaskId]` | 等待结果；已展示的结果只显示状态，重看正文用 `/task`。 |
| `/cancel [完整TaskId]` | 请求取消，不保证返回时已经停止。 |
| `/close` | 关闭当前空闲会话并取消选择，历史保留；运行中拒绝关闭。 |
| `/exit` | 退出并请求取消全部后台任务。 |

命令不调用模型；关闭后可选择查看历史，但不能继续提问。关闭后用 `/new` 或 `/use` 选择可写会话。
会话详情显示是否选中、创建及最近活动时间。详情和列表优先显示当前任务，无当前任务则显示最近任务；
没有提交过任务时明确显示 Task: none。`Finished: yes` 包含 Completed、Failed、Cancelled，
只代表任务已结束；成功需要状态为 Completed。Cancelling 仍是未结束，Session.Idle 也不代表任务成功。
查询返回快照；运行中的状态会继续变化，可再次 `/session` 或 `/sessions` 查询。
`/task`、`/wait`、`/cancel` 省略 ID 时操作最近提交的任务，切换会话不会改变该 ID。
`/wait` 期间不读取新命令；不等待时可以用 `/cancel` 请求取消，再用 `/wait` 确认终态。
自动通知与命令通过单一输出循环显示，通知另起行并恢复输入提示；输入仍使用标准 ReadLine，
保留正在等待的输入，但不实现完整输入行重绘。日志写入 stderr，仍可能与同一终端的交互交错。
Delivered 且回复保存成功映射为 Completed；截断、未知、拒绝、过滤和不支持工具映射为 Failed，
保留已有文本与明确原因。依赖超时是 Failed，不是 Cancelled。详细政策见 [Orchestration](./SRC/Runtime/Orchestration/Orchestration.md)。
这是可信本地单用户模式，UserId 来自本地上下文；没有登录、多用户远程授权或跨进程任务锁。
输入保存后调用失败会留下未回答的用户记录；回复保存失败明确报错，不自动重新调用模型。

[appsettings.json](./appsettings.json) 随构建与发布复制到输出目录，Host 从应用输出目录读取配置，
不依赖启动终端的当前目录。支持 Generic Host 标准环境变量与命令行覆盖，例如：

```powershell
$env:Ollama__ModelName = "qwen2.5:1.5b"
dotnet run --project .\HanyingAgent\HanyingAgent.csproj -- --Ollama:TimeoutSeconds=180 --Agent:MaxOutputTokens=128
```

Ollama 配置包括 BaseAddress、ModelName、ContextWindowTokens、TimeoutSeconds；
Agent 配置包括 SystemInstruction、MaxInputCharacters、MaxOutputTokens、Temperature、MaxHistoryTurns（1–100，默认 10）。
非法配置在启动时失败。地址必须为末尾带 `/` 的 HTTP(S) URL，不含凭据、查询或片段。
字符上限与模型 Token 窗口独立；仅裁剪可选的旧历史，不自动估算 Token、生成摘要或预留输出空间。

日志写入标准错误，交互与回答写入标准输出；任务流程关联 TaskId 和 SessionId，
嵌套执行、Context 与模型日志同时携带 ExecutionId。
默认不记录输入正文、系统指令、记忆正文或完整回复。日志级别可在 Logging:LogLevel 中设置。
详细边界见 [Host](./SRC/Host/Host.md)、[Execution](./SRC/Runtime/Execution/Execution.md)
和[日志说明](./SRC/Infrastructure/Observability/Observability.md)。

2026-11-11 本轮验收：全套 439 项测试通过、0 失败、0 跳过，构建警告按错误处理并通过。
真实 Console 经 Context 与 Ollama 调用本机 qwen2.5:1.5b，`2 + 3` 返回 `5`、结束原因 Stop；
已检查实际执行、Context 与模型日志使用同一 ExecutionId。真实服务联调不计入离线测试数。

2026-11-12 会话阶段：67 项会话 / 执行 / Console 测试通过、0 失败、0 跳过，Release 构建以警告为错误通过。
真实 Ollama 两轮中文联调：先提供姓名，第二轮正确回答“岳席文”，第二轮实际发送 4 条消息；
`/history` 可查看 4 条记录，新建会话历史为空。Debug 输出被旧进程占用，本轮未停止该进程，验证使用 Release。

2026-11-14 任务阶段：新增 21 项生命周期与并发测试，Release 全套 506 项通过、0 失败、0 跳过，
构建以警告为错误通过。覆盖后台接受、会话占用、关闭竞态、取消确认、查询取消隔离、
历史保存失败、终态不可覆盖、稳定分页及 Host 退出期限。
真实 Ollama 中文联调通过：两个任务均 Completed，第二个请求包含 4 条消息，正确回忆测试口令“松树”。
真实服务联调不计入自动化测试数。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [SRC](./SRC/SRC.md) | 按契约、交互入口、运行控制、业务能力与基础设施组织应用模块。 |
| [TEST](./TEST/TEST.md) | 正式 xUnit 测试项目，验证记忆、Context、模型、执行、会话与历史。 |

## 根目录与设计边界

- [HanyingAgent.csproj](./HanyingAgent.csproj) 定义项目构建，并排除 TEST 子树，防止测试源码混入应用。
- [HanyingAgent.slnx](./HanyingAgent.slnx) 组织应用与测试项目，便于编辑器发现和统一构建。
- [Program.cs](./Program.cs) 负责 Host 启停；[ApplicationHost](./SRC/Host/ApplicationHost.cs) 集中配置 DI、Options 与日志，不承载具体业务规则。
- bin、obj 是生成目录，不编写模块文档，也不作为业务模块维护。
- 目录分组不等于微服务划分；业务模块通过契约协作，具体实现由入口组合。

整体设计背景见[设计初稿](<../.docs/Mini Agent Runtime 设计初稿.md>)。
模块边界与端到端执行过程见[模块职责与运行流程](<../.docs/HanyingAgent 模块职责与运行流程.md>)。
工作记忆的本期范围、验收标准和扩展方向见[WorkingMemory 最小实现与演进计划](<../.docs/WorkingMemory 最小实现与演进计划.md>)。

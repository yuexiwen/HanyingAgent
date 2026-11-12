# Observability

## 功能与定位

让运行过程可观察，支持定位模型调用、Context 选择、工具执行、状态变化和持久化行为。

当前使用 Microsoft.Extensions.Logging 标准接口，由 [ApplicationHost](../../Host/ApplicationHost.cs) 统一配置，
本目录不引入重复的 Logger 包装、全局单例或自建日志框架。

## 当前日志链路

- DirectExecutor 每次执行建立包含 ExecutionId 的 BeginScope，正常异步调用自动继承，独立并发执行使用不同 ID。
- 执行器记录开始、返回及耗时；失败、主动取消、超时分别记录，未知结束原因不标记为任务完成。
- 执行返回日志分别记录 Conclusion 与模型 FinishReason；Delivered 使用 Information，
  截断、未知、拒绝、过滤和不支持动作使用 Warning，不把模型返回误记为成功交付。
- Context Builder 记录构建开始、最终消息数、记忆数、UTF-16 字符数和耗时。
- Ollama 适配器记录模型名、消息数、结束原因、Token 用量与耗时。
- 两个现有组件新增可选 ILogger 参数，原构造方式仍可使用 NullLogger；DI 路径注入真实日志。
- SimpleConsole 显示时间与 Scope，日志统一写入 stderr；stdout 保留交互与回答。
- 默认级别为 Information，Microsoft 与 HTTP 客户端内部日志为 Warning；Logging 配置可覆盖。
- 不记录请求正文、记忆内容、系统指令或完整回复。已知异常只在执行边界记录一次，Console 提示不重复打印堆栈。

对话协调建立真实 SessionId Scope，内部 DirectExecutor 再嵌套 ExecutionId。
历史选择和记录事件携带 SessionId，执行、Context 与模型事件同时携带两者。
仍不伪造尚未建立的 TaskId；Scope 是日志关联，不是分布式 Trace。
文件滚动、Activity / OpenTelemetry、指标和事件总线仍未实现。没有将日志作为对话、任务状态或恢复依据。

## 日志事件编号

跨模块事件统一定义于 [LogEvents](../../Diagnostics/LogEvents.cs)，仅包含标准 EventId，
不引入日志包装或运行逻辑；Runtime 和模型适配器共享此定义，不互相依赖实现。

| 编号 | 事件名 | 级别与含义 |
|---|---|---|
| 1001 | ExecutionStarted | Information：一次执行开始。 |
| 1002 | ExecutionReturned | Delivered 为 Information，其余结论为 Warning；不代表 Task 已完成。 |
| 1003 | ExecutionFailed | Error：已知执行异常，附异常信息并继续向上抛出。 |
| 1004 | ExecutionCancelled | Information：调用方取消已被观察到。 |
| 1005 | ExecutionTimedOutOrDependencyCancelled | Warning：超时或依赖取消，调用方没有请求取消。 |
| 2001 | ContextBuildStarted | Information：开始构建上下文。 |
| 2002 | ContextBuildCompleted | Information：上下文构建完成。 |
| 2003 | HistoryWindowSelected | Information：历史片段、扫描数量、字符数与停止原因。 |
| 3001 | ModelInvocationStarted | Information：开始发送模型请求。 |
| 3002 | ModelInvocationReturned | Information：模型响应解析通过，不代表执行策略正常交付。 |
| 4001 | DialogueInputRecorded | Information：输入已记录，携带 MessageId 与 Sequence。 |
| 4002 | DialogueResponseRecorded | Information：实际回复已记录，携带结论与顺序。 |
| 4003 | DialogueFailed | Error：会话访问、历史选择或记录失败，注明阶段，不自动重试。 |
| 4004 | DialogueCancelled | Information：非模型执行阶段观察到取消。 |
| 4005 | SessionCommandFailed | Warning：会话命令或没有当前会话时的操作失败。 |
| 4006 | DialogueResponseNotRecorded | Warning：不支持的动作没有伪装成普通文本历史。 |

1000 段用于执行事件，2000 段用于 Context，3000 段用于模型调用，4000 段用于对话协调与会话命令。
事件编号和名称应保持稳定；新增不同语义的事件使用新编号，不复用旧编号。
框架自己的日志不在本表管理范围内。

EventId 标识事件类型，日志 Category 标识来源组件，ExecutionId 关联一次执行，LogLevel 表达严重程度。
同一个 1002 事件通过 Conclusion 区分交付结果，不为每种结论和级别组合创建新编号。
SimpleConsole 自动显示数字编号，例如：

```text
info: HanyingAgent.Runtime.Execution.DirectExecutor[1001] => ExecutionId:... Execution started
warn: HanyingAgent.Runtime.Execution.DirectExecutor[1002] => ExecutionId:... Execution returned with Incomplete, ...
```

事件名作为 EventId.Name 提供给日志 Provider；当前 SimpleConsole 只在方括号里显示数字。
失败和取消仍只在执行边界记录，不为了编号在 Context 或模型适配器重复记录相同异常。

## 后续组件规划

| 组件 | 一句话概述 |
|---|---|
| AgentEventHub | 发布与订阅按 Session 关联的运行事件。 |
| RuntimeMetrics | 记录调用次数、耗时、失败和 Token 等指标。 |
| 日志与追踪适配 | 将结构化事件关联到 Task、Tool Call 和一次执行链路。 |

## 设计边界

可观测性记录事实，不负责业务状态转换，也不是持久化事实来源。
不得默认记录完整敏感 Prompt、私人记忆、凭据或大型工具原始输出。
事件订阅的取消不等于任务取消；交付、缓冲与慢订阅者策略需在后续实现时明确。

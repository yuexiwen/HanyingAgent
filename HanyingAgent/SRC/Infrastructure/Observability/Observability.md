# Observability

## 功能与定位

让运行过程可观察，支持定位模型调用、Context 选择、工具执行、状态变化和持久化行为。

当前使用 Microsoft.Extensions.Logging 标准接口，由 [ApplicationHost](../../Host/ApplicationHost.cs) 统一配置，
本目录不引入重复的 Logger 包装、全局单例或自建日志框架。

## 当前日志链路

- DirectExecutor 每次执行建立包含 ExecutionId 的 BeginScope，正常异步调用自动继承，独立并发执行使用不同 ID。
- 执行器记录开始、返回及耗时；失败、主动取消、超时分别记录，未知结束原因不标记为任务完成。
- Context Builder 记录构建开始、最终消息数、记忆数、UTF-16 字符数和耗时。
- Ollama 适配器记录模型名、消息数、结束原因、Token 用量与耗时。
- 两个现有组件新增可选 ILogger 参数，原构造方式仍可使用 NullLogger；DI 路径注入真实日志。
- SimpleConsole 显示时间与 Scope，日志统一写入 stderr；stdout 保留交互与回答。
- 默认级别为 Information，Microsoft 与 HTTP 客户端内部日志为 Warning；Logging 配置可覆盖。
- 不记录请求正文、记忆内容、系统指令或完整回复。已知异常只在执行边界记录一次，Console 提示不重复打印堆栈。

目前只有 ExecutionId，不伪造尚未建立的 TaskId / SessionId；Scope 是日志关联，不是分布式 Trace。
文件滚动、Activity / OpenTelemetry、指标和事件总线仍未实现。没有将日志作为对话、任务状态或恢复依据。

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

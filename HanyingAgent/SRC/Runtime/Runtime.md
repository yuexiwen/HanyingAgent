# Runtime

## 功能与定位

将运行管理与 Agent 实际工作分开，通过明确契约组合，不把目录分组当作独立服务。

当前 Context 已完成纯文本请求组装、工作记忆接入、总输入字符上限及真实组件组合验收；
Execution 已实现 DirectExecutor；Users、Sessions 及 Capabilities 中的 Conversation 已接入 Console，
通过有界历史窗口支持同会话多轮对话。Tasks 已实现后台 Direct 任务生命周期，Orchestration 编排一轮对话流程；
工具循环、等待用户与恢复仍未实现。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Management](./Management/Management.md) | 管理用户、会话及业务任务生命周期，安排并跟踪执行。 |
| [Agent](./Agent/Agent.md) | 编排实际工作，组织执行策略、统一 Context 和业务能力。 |

## 设计边界

Runtime 使用模型、工具和记忆契约，不直接选择数据库、HTTP 传输或 Provider SDK。
历史读写由 [Agent/Capabilities/Conversation](./Agent/Capabilities/Conversation/Conversation.md) 提供；
Runtime 的 Context 决定历史如何进入模型输入，Orchestration 决定何时读写。
编排不应吸收所有算法成为万能对象；执行位置、已知信息和模型上下文分别由不同模块管理。
底层线程调度由 .NET 运行时负责，TaskManager 保存的 Work 是后台执行链的完成凭据。
旧执行进度规划保留在 Agent/Execution/State；InMemoryAgentState 仍在 Infrastructure/Persistence，
两者不是同一个概念，本次不引入新的执行进度实现。

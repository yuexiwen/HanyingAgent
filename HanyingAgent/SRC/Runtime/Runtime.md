# Runtime

## 功能与定位

负责 Agent 任务如何被协调和推进，将外部能力组合成可观察、可取消、可恢复的执行过程。

当前 Context 已完成纯文本请求组装、工作记忆接入、总输入字符上限及真实组件组合验收；
Execution 已实现 DirectExecutor；Users、Sessions 及 Capabilities 中的 Conversation 已接入 Console，
通过有界历史窗口支持同会话多轮对话。Tasks 已实现后台 Direct 任务生命周期，Orchestration 编排一轮对话流程；
工具循环、等待用户与恢复仍未实现。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Tasks](./Tasks/Tasks.md) | 管理任务接受、后台执行、查询、取消、结果保存及退出收尾。 |
| [Orchestration](./Orchestration/Orchestration.md) | 编排一轮对话的输入保存、历史选择、执行和回复保存。 |
| [Execution](./Execution/Execution.md) | 按 Direct、Prompt Chain 或 ReAct 策略推进任务。 |
| [Context](./Context/Context.md) | 组装纯文本消息和明确归属的工作记忆，生成本轮 ModelInvocation。 |
| [Users](./Users/Users.md) | 管理本地用户资料与可信单用户身份上下文。 |
| [Sessions](./Sessions/Sessions.md) | 管理本地会话和归属，与任务存储原子协调单会话占用。 |
| [State](./State/State.md) | 管理任务执行位置、合法状态转换和恢复所需进度。 |

## 设计边界

Runtime 使用模型、工具和记忆契约，不直接选择数据库、HTTP 传输或 Provider SDK。
历史读写由 [Capabilities/Conversation](../Capabilities/Conversation/Conversation.md) 提供；
Runtime 的 Context 决定历史如何进入模型输入，Orchestration 决定何时读写。
编排不应吸收所有算法成为万能对象；执行位置、已知信息和模型上下文分别由不同模块管理。

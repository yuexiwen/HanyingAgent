# Runtime

## 功能与定位

负责 Agent 任务如何被协调和推进，将外部能力组合成可观察、可取消、可恢复的执行过程。

当前 Context 已完成纯文本请求组装、工作记忆接入、总输入字符上限及真实组件组合验收的五步最小版；其余子模块仍为目录骨架，未接入 Agent 执行链路。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Orchestration](./Orchestration/Orchestration.md) | 协调 Session 与任务生命周期并连接执行、状态和恢复流程。 |
| [Execution](./Execution/Execution.md) | 按 Direct、Prompt Chain 或 ReAct 策略推进任务。 |
| [Context](./Context/Context.md) | 组装纯文本消息和明确归属的工作记忆，生成本轮 ModelInvocation。 |
| [Conversation](./Conversation/Conversation.md) | 预留消息追加与历史读取管理，供界面和模型上下文使用。 |
| [State](./State/State.md) | 管理任务执行位置、合法状态转换和恢复所需进度。 |

## 设计边界

Runtime 使用模型、工具和记忆契约，不直接选择数据库、HTTP 传输或 Provider SDK。
编排不应吸收所有算法成为万能对象；执行位置、已知信息和模型上下文分别由不同模块管理。

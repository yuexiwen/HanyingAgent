# Agent

## 功能与定位

组织实际处理用户问题的流程与能力，不承担业务 Task 的生命周期管理，也不实现线程池调度。

| 子模块 | 职责 |
|---|---|
| [Orchestration](./Orchestration/Orchestration.md) | 决定一次对话的步骤顺序、协调平级模块。 |
| [Execution](./Execution/Execution.md) | 控制模型调用策略并解释结果，当前只有 DirectExecutor。 |
| [Context](./Context/Context.md) | 在统一预算内选择候选信息，组装本次模型输入。 |
| [Capabilities](./Capabilities/Capabilities.md) | 提供对话、记忆、技能和工具等信息与操作能力。 |

```text
Management/Tasks
  → Orchestration
      → Conversation / Memory：读取或保存数据
      → Execution：推进完整执行策略
          → Context：选择与组装模型输入
          → 模型：推理并返回响应
```

Capabilities 不只是数据结构：Tools 也包含执行能力，Memory 包含管理规则。
它们可以被 Console 等其他合法调用方通过接口使用，放在 Agent 下不代表只有编排器可访问。
Contracts 不引用本目录的实现，Provider 客户端及具体 Store 仍由 Infrastructure 提供。
公共协议见 [Contracts/Agent](../../Contracts/Agent/Agent.md)。

当前没有 ReAct 工具循环、自动记忆提取或长期整合；
Orchestration 获取初始历史和记忆快照，Execution 从第一轮开始调用 ContextBuilder。
未来多轮策略可追加本次任务的执行记录后重建 Context，不默认每轮重新读取历史与 Memory；
构造算法仍由 Context 模块维护，不在执行器中复制。
Execution 下的 [State](./Execution/State/State.md) 仅保留未来执行进度规划，不新增状态机。

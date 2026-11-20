# SRC

## 功能与定位

承载应用的模块源码，以角色分组减少顶层平铺；目录层次表达职责，不要求每组拆成独立项目或服务。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Contracts](./Contracts/Contracts.md) | 定义模块之间的能力接口、标识和数据协议。 |
| [Host](./Host/Host.md) | 将用户命令和输入适配成 Runtime 操作，并展示结果与事件。 |
| [Runtime](./Runtime/Runtime.md) | 分为 Management 运行管理与 Agent 实际工作流程及能力。 |
| [Infrastructure](./Infrastructure/Infrastructure.md) | 实现模型访问、存储和可观测性等技术适配。 |
| [Diagnostics](./Diagnostics/LogEvents.cs) | 共享跨模块日志事件编号与名称，不承载日志实现或业务逻辑。 |

## 依赖边界

- Contracts 不依赖业务实现或基础设施实现。
- Runtime/Management 负责身份、会话与 Task Lifecycle Flow，Runtime/Agent 负责 Agent Execution Flow。
- Agent 下分为 Orchestration、Execution、Context 和 Capabilities；能力通过契约协作，不直接创建数据库或 HTTP 客户端。
- Capabilities 不反向依赖 Agent 的编排实现，可声明实现所需的内部端口。
- Infrastructure 实现公共契约或内部端口，项目根入口负责选择具体适配器。
- Contracts 按 Management 与 Agent 分组；数据归属 ID 可以跨组引用，分组不是进程或授权边界。
- 当前已实现工作记忆、统一 Context 预算、Ollama 适配、用户会话和后台 Direct 任务；工具循环仍待实现。

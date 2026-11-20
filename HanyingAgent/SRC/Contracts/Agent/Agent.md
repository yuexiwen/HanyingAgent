# Agent

定义完成 Agent 工作所需的协议，既包括内容数据，也包括处理能力。

| 子模块 | 职责 |
|---|---|
| [Context](./Context/Context.md) | 上下文构建接口与候选输入请求。 |
| [Execution](./Execution/Execution.md) | 执行交付结论与结果，不代替业务 Task 状态。 |
| [LanguageModels](./LanguageModels/LanguageModels.md) | 模型消息、调用参数、预算能力及生成接口。 |
| [Capabilities](./Capabilities/Capabilities.md) | 对话、记忆和工具的公共协议。 |

LanguageModels 表达 Agent 所需的能力，其 Ollama 等技术实现仍位于 Infrastructure，
不因实现位置而将业务接口归入基础设施。
Orchestration 暂无需要公开的独立能力契约，不为镜像实现目录而创建空协议层。
Skills 暂无实际契约，不预建空目录。

对应工作流程见 [Runtime/Agent](../../Runtime/Agent/Agent.md)。

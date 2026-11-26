# Capabilities

定义 Agent 使用的信息与操作能力，不实现存储、执行循环或 Provider 调用。

| 子模块 | 职责 |
|---|---|
| [Conversation](./Conversation/Conversation.md) | 类型化对话记录、追加、查询和分页协议。 |
| [Memory](./Memory/Memory.md) | 记忆模型与工作记忆、长期记忆访问协议。 |
| [Tools](./Tools/Tools.md) | 工具定义、调用、结果和 Observation 协议。 |
| [Skills](./Skills/Skills.md) | Skill 元数据发现与完整方法说明加载协议。 |
| [Prompts](./Prompts/Prompts.md) | 参数化本地 Prompt 的定义发现与文本渲染协议。 |

Tools 不只是 Context 的数据源，也定义实际动作的调用协议；
调用前的工具说明和调用后的工具结果可以参与上下文构建。
这些协议可引用 Management 下的 UserId、SessionId、TaskId 表达归属。
Skills 不定义执行步骤状态机，也不授予工具权限。

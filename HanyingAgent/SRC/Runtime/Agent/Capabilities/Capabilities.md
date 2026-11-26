# Capabilities

## 功能与定位

承载 Agent 使用的信息与操作能力，关注能力本身的规则，不控制整个 Agent 执行循环。

当前已实现本地 Conversation 历史管理、工作记忆和可扩展本地工具入口（含 UTC 时间工具）；
Skills 已实现本地文件的校验、发现与快照加载，Prompts 已实现模板发现与参数渲染，
两者尚未接入 Agent Context，模型工具循环尚未实现。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Conversation](./Conversation/Conversation.md) | 管理交互历史的追加、顺序、归属和读取，为运行流程提供数据。 |
| [Memory](./Memory/Memory.md) | 管理工作记忆与长期记忆的业务规则及访问协调。 |
| [Skills](./Skills/Skills.md) | 管理并加载任务方法说明，提供名称与描述发现，不负责自动选择。 |
| [Prompts](./Prompts/Prompts.md) | 管理本地 Prompt 模板，发现参数定义并渲染文本，不调用模型。 |
| [Tools](./Tools/Tools.md) | 管理工具定义、校验、执行约束和结果标准化。 |

## 设计边界

公共协议位于 Contracts，内部存储或检索端口可由能力模块声明，技术实现放在 Infrastructure。
能力实现不依赖 Agent 的 Orchestrator；本地与远程适配通过入口组合替换。
Skills 与 Prompts 共用内部 [LocalMarkdownFiles](./LocalMarkdownFiles.cs)，
只复用本地文档读取和 front matter 解析，不合并两个模块的业务契约。
Capability 不等于暴露给模型的 Tool；历史读取仍需遵守归属与访问校验。
Conversation 当前仍使用 Runtime.Management.Users.LocalUserContext，本次迁移不改变身份实现。
目录归入 Agent 不限制合法调用方；Console 可以通过业务接口查看历史或显式写入记忆。

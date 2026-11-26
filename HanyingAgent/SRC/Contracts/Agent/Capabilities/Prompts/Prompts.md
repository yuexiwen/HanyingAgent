# Prompts

定义本地参数化 Prompt 的发现和渲染契约，不是 MCP Prompts 协议。
不依赖 Skill，接口也不暴露本地路径或文件流，可由远程实现替换。

| 文件 | 职责 |
|---|---|
| [PromptDefinition](./Models/PromptDefinition.cs) | 名称、描述及有序参数定义，不包含模板正文。 |
| [PromptArgumentDefinition](./Models/PromptArgumentDefinition.cs) | 参数名称与描述；本期所有参数均为必填字符串。 |
| [RenderedPrompt](./Models/RenderedPrompt.cs) | Prompt 名称与最终渲染文本。 |
| [IPromptManager](./Operations/IPromptManager.cs) | ListDefinitionsAsync 列举定义，RenderAsync 根据名称和参数生成文本。 |

ListDefinitionsAsync 返回按名称 Ordinal 排序的只读列表，没有配置时为空。
RenderAsync 仅接受与定义完全一致的参数名集合，拒绝遗漏、额外参数或 null 值，
允许空字符串。名称与参数名区分大小写；未知 Prompt 抛出 KeyNotFoundException。
取消和校验失败明确传播，不返回部分文本或空的成功结果。

返回内容没有模型消息角色，不自动成为系统指令。渲染不执行表达式、函数或工具，
不调用模型、不写入历史、不决定 Context 的信任层级和预算。
用户提供的参数可能是不可信数据，文本替换不是提示注入防护。
本地文件格式、长度限制与初始化见 [Runtime Prompts](../../../../Runtime/Agent/Capabilities/Prompts/Prompts.md)。

# Models

## 功能与定位

定义一次模型调用所需的数据协议，与外层 LanguageModels 能力名称区分。

当前无子目录，包含：

| 类型 | 功能 |
|---|---|
| [ModelMessage](./ModelMessage.cs) | 抽象消息基类，声明 JSON role 与四种具体消息的映射。 |
| [ModelSystemMessage](./ModelSystemMessage.cs) | 携带系统规则文本。 |
| [ModelUserMessage](./ModelUserMessage.cs) | 携带用户请求或补充文本。 |
| [ModelAssistantMessage](./ModelAssistantMessage.cs) | 携带可空 Text 与非 null 的工具调用列表，允许两者同时存在。 |
| [ModelToolMessage](./ModelToolMessage.cs) | 携带 Tools 模块的完整 ToolObservation，保留原调用与结果关联。 |
| [ModelOptions](./ModelOptions.cs) | 定义可选模型名称、Temperature 和最大输出 Token 数。 |
| [ModelFinishReason](./ModelFinishReason.cs) | 区分停止、工具调用、长度限制、内容过滤、拒绝与未知原因。 |
| [ModelUsage](./ModelUsage.cs) | 表达本次调用的可空输入和输出 Token 数，不用零伪装未知。 |

## 消息约定

- ModelInvocation.Messages 按对话顺序排列，以基类序列化时使用 role = system / user / assistant / tool。
- System 与 User 的 Content 非空白；Assistant 至少有有效文本或一个完整工具调用，纯文本时 ToolCalls 为空列表。
- 工具调用复用 ToolCallRequest，ID 有效、名称非空白、参数为符合 Schema 的完整 JSON 对象。
- ModelToolMessage.Observation 不为 null，CallId、ToolName 与此前尚未回答的 Assistant 工具调用匹配，不能重复回答同一调用。
- 一个 Assistant 可提出多个调用；下一轮模型请求前必须补齐相应工具结果，未执行或被拒绝的调用也须显式处理，不伪造成功。
- Observation 保留状态、摘要、错误和 ArtifactReference；Provider 适配器映射工具角色与调用 ID，并在消息内容中保留必要结果信息。
- 不是把 Observation 当成普通 User 消息，也不直接把它当成 Provider SDK 的原生消息。
- 当前只支持这四种角色；未知 JSON role 显式报错，不默认当作 User。

## 选项与用量

- ModelName 为 null 时使用明确配置的默认模型，缺少可用配置显式报错；指定名称不能空白。
- Temperature 为 null 时不覆盖配置或 Provider 默认，显式值必须有限且符合目标模型的支持范围。
- MaxOutputTokens 显式设置时大于零；不支持的参数或能力显式报错，不静默忽略或改用其他模型。
- ModelUsage 的已知计数非负，0 表示确实报告为零；某一项未知用 null，全部未知可令响应 Usage 为 null。
- 不把本地预算估算冒充 Provider 实际报告用量。

## 结束原因

Stop=0、ToolCalls=1、Length=2、ContentFiltered=3、Refused=4、Unknown=5，保持固定值。
Stop 只表示本轮模型正常结束，不表示 Task 已完成；Length 表示输出被截断，不能当作完整工具指令执行。
Unknown 表示无法映射或未报告结束原因，不等于网络失败；实际调用异常必须显式传播。

JsonRequired 只强制核心字段存在，不承诺 DTO 自动完成全部验证。
具体适配器负责所支持子集的入口校验；Ollama 实现已校验纯文本与生成选项，完整工具关联仍待后续实现。

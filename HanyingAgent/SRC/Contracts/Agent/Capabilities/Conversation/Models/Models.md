# Models

## 功能与定位

定义历史记录的身份、归属、顺序和内容，区别于本轮模型输入协议。

当前无子目录，包含：

| 类型 | 功能 |
|---|---|
| [MessageId](./MessageId.cs) | 只读 record struct，显式构造拒绝空白，JsonConstructor 支持只读 Value 往返。 |
| [ConversationMessage](./ConversationMessage.cs) | 表达 Id、SessionId、可空 TaskId、Sequence、Content 和 CreatedAt。 |
| [ConversationContent](./ConversationContent.cs) | 通过 JSON role 判别 user、assistant 和 tool 的抽象内容基类。 |
| [UserConversationContent](./UserConversationContent.cs) | 保存用户真实提交的 Text。 |
| [AssistantConversationContent](./AssistantConversationContent.cs) | 保存文本、工具调用及可空标准化和 Provider 原始结束原因。 |
| [ToolConversationContent](./ToolConversationContent.cs) | 复用 ToolObservation，保留状态、错误、调用关联和必要输出引用。 |

## 身份、顺序与归属

- MessageId 在应用实例内唯一，首次追加前生成，重试不能重新生成；default ID 仍需入口拒绝。
- Sequence 在同一 Session 内从 1 开始、随新增记录严格递增，由服务端并发安全分配，不承诺无空洞。
- CreatedAt 由服务端记录生成，不能代替 Sequence 决定顺序；工具实际结束时间仍在 Observation 中保留。
- Session 必须存在；提供 TaskId 时，任务必须存在且属于同一 Session。
- 任务内的输入、回答和工具交互须关联 TaskId；可空仅用于合法的会话级记录，不应丢失已知任务归属。
- Tool 内容和包含工具调用的 Assistant 内容必须关联 TaskId，结果必须匹配同 Session、同 Task 中此前的调用。
- Task Version 检查任务是否过期，Sequence 表示历史位置，二者不能互相代替。

## 内容约定

- User.Text 不可空白。
- Assistant.ToolCalls 为非 null 列表，允许文本与多个调用同时存在；通常至少有非空白文本或一个完整调用。
- 模型因 Length、ContentFiltered、Refused 或 Unknown 没有可用输出时，允许只保留明确结束信息，不能丢失实际结果或伪造成功文本。
- Assistant 来自 ModelResponse 时，保留真实 FinishReason 和 ProviderFinishReason，避免把截断或拒绝输出伪装成完整回答。
- 非模型生成的 Assistant 说明或待回答问题允许 FinishReason 为 null；Conversation 不据此决定 Task 状态。
- 只有完整且允许的工具调用才能记录为可执行调用，截断或无效参数不能伪装成有效 ToolCallRequest。
- Tool.Observation 不为 null，工具名与 CallId 须匹配尚未回答的 Assistant 调用；不同消息不能重复回答同一调用。
- 工具结果按实际记录顺序追加，不要求多个并发工具按调用列表顺序完成，关联以 CallId 为准。
- ToolObservation 的失败、取消、结果未知和必要 ArtifactReference 必须保留。

JSON role 缺失或未知时显式失败，不默认为用户文本。JsonRequired 只要求核心字段存在；
字符串、集合、枚举、正序号、任务归属和调用关联仍由后续管理器验证。

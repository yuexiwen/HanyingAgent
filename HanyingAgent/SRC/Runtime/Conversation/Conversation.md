# Conversation

## 功能与定位

管理对话历史，负责消息追加、会话内顺序和历史读取，为 Host 展示及 Context 构建提供数据。

当前已实现 [LocalConversationManager](./LocalConversationManager.cs)、
[IConversationStore](./IConversationStore.cs) 和 [ConversationHistoryReader](./ConversationHistoryReader.cs)；
内存实现位于 [Persistence](../../Infrastructure/Persistence/InMemoryDialogueStore.cs)。

## 当前规则

- 通过本地可信身份精确校验会话归属，不根据请求里的 ID 自动授予权限。
- 首次追加前生成稳定 MessageId，同 ID / Session / Task / 内容重试返回原记录，载荷冲突显式报错。
- Sequence 在 Store 锁内按会话递增分配，与 CreatedAt 分离；输入和输出记录不依赖时间戳排序。
- 默认返回最近 20 条，页内升序；页大小 1–200。游标仅用于向前翻页，绑定会话，
  新消息追加不会改变游标之前的旧页；非法或跨会话游标报错。游标不是授权凭据。
- 当前仅支持 User 和纯文本 Assistant；Assistant 保留 FinishReason / ProviderFinishReason，
  可以只记录结束信息。工具历史明确拒绝；TaskId 必须指向该会话的当前非终态任务。
  任务占用期间禁止追加无 TaskId 的新消息；相同载荷的已有消息重试仍可在任务结束后返回原记录。
- 工具列表独立快照并只读暴露，重试按内容比较而非集合引用比较；查询和分页不暴露底层集合。
- Closed 会话可读取，也可返回已有相同消息的重试；不能新增记录。

## 历史读取与流程边界

[ConversationHistoryReader](./ConversationHistoryReader.cs) 提供面向历史查看的最近 N 轮读取；
maxTurns 为 null 时读取全部，为正整数时按 User 记录计数，包含未回答输入，使用稳定游标翻页后正序返回。
不解析 Console 命令、不渲染输出、不调用模型，也不代替 ConversationWindow 的模型历史筛选。

一轮对话的执行流程由 [DialogueOrchestrator](../Orchestration/DialogueOrchestrator.cs)
编排，不属于历史管理模块。模型历史筛选由
[Context/ConversationWindow](../Context/ConversationWindow.cs) 负责。
Conversation 提供消息读写能力，不依赖执行器、不驱动模型调用；
流程、失败处理和日志边界见 [Orchestration](../Orchestration/Orchestration.md)。

## 后续组件规划

| 组件 | 一句话概述 |
|---|---|
| ConversationManager | 已实现本地版本：消息写入、归属、幂等及顺序。 |
| 对话存储端口 | 已实现：隔离业务管理与具体保存方式。 |
| 历史读取协调 | 为界面分页和模型上下文选择提供必要记录。 |

## 设计边界

- 公共消息及操作协议由 [Contracts/Conversation](../../Contracts/Conversation/Conversation.md) 定义。
- 具体存储实现位于 [Infrastructure/Persistence](../../Infrastructure/Persistence/Persistence.md)。
- 通过 SessionId 精确读取记录，不自行构建搜索索引或调用模型。
- Host 选择会话只加载所需历史；Context 选择模型所需片段，不自动发送整个会话。
- 追加与读取仍需明确权限、并发顺序及工具结果关联，不能仅依赖客户端提交的 SessionId。
- 公共 IConversationManager 已定义，具体实现需按稳定 MessageId 处理重试、冲突并分配会话内 Sequence。
- 读取默认最近 20 条，页内 Sequence 升序，游标继续读取更早历史；不自动将结果当作完整模型上下文。
- 历史内容独立于 ModelMessage，Context 转换时须保留工具关联、实际结束原因和失败信息。

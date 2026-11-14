# Conversation

## 功能与定位

规划历史对话管理实现，协调消息追加、会话内顺序和历史读取，为 Host 展示及 Context 构建提供数据。

当前已实现 [LocalConversationManager](./LocalConversationManager.cs)、
[IConversationStore](./IConversationStore.cs) 和 [DialogueCoordinator](./DialogueCoordinator.cs)；
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

## 对话协调

任务编排将不可变执行配置映射为 [DialogueRequest](./DialogueRequest.cs)，Runtime 不依赖 Console 或 Host 配置类型。

```text
Orchestration 已持有任务占用（独立 RespondAsync 入口使用 Session.Active lease）
  → 保存本轮 User，取得 Sequence
  → ConversationWindow 选择该输入之前的历史，并只加入一次当前输入
  → DirectExecutor
  → 保存实际 Assistant 内容和结束原因
  → 返回 ExecutionOutcome
```

完整和非正常文本响应均保存用于展示，截断 / 未知 / 拒绝 / 过滤不会进入下一轮窗口。
工具动作当前无法在纯文本记录中如实表达，UnsupportedAction 明确记录事件但不保存为普通 Assistant 历史。
连接失败或取消保留已提交的 User，不伪造 Assistant；后续窗口跳过这些未回答的旧输入。
回复保存失败明确报错，标注 SaveAssistant 阶段，不重复调用模型、不伪装成保存成功。
本期无自动重试或恢复接口；消息幂等不等于整个对话或模型调用幂等，用户重新提问是新一轮输入。
异常和取消不回滚已提交记录，也不构成完整事务。

SessionId Scope 覆盖历史选择与执行，DirectExecutor 的 ExecutionId 嵌套其中。
任务路径继承 Orchestration 的 TaskId Scope，消息输入和输出关联同一 TaskId。
执行器已有的错误不重复记录；历史读写和会话访问失败由对话协调边界显式记录并抛出。

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

# Conversation

## 功能与定位

规划历史对话管理实现，协调消息追加、会话内顺序和历史读取，为 Host 展示及 Context 构建提供数据。

当前只有目录说明，没有子目录、管理器或存储实现。

## 后续组件规划

| 组件 | 一句话概述 |
|---|---|
| ConversationManager | 管理消息写入与读取规则，维护归属和确定顺序。 |
| 对话存储端口 | 隔离业务管理与内存、JSON、SQLite 等具体保存方式。 |
| 历史读取协调 | 为界面分页和模型上下文选择提供必要记录。 |

## 设计边界

- 公共消息及操作协议由 [Contracts/Conversation](../../Contracts/Conversation/Conversation.md) 定义。
- 具体存储实现位于 [Infrastructure/Persistence](../../Infrastructure/Persistence/Persistence.md)，当前尚未创建 Conversation Store。
- 通过 SessionId 精确读取记录，不自行构建搜索索引或调用模型。
- Host 选择会话只加载所需历史；Context 选择模型所需片段，不自动发送整个会话。
- 追加与读取仍需明确权限、并发顺序及工具结果关联，不能仅依赖客户端提交的 SessionId。
- 公共 IConversationManager 已定义，具体实现需按稳定 MessageId 处理重试、冲突并分配会话内 Sequence。
- 读取默认最近 20 条，页内 Sequence 升序，游标继续读取更早历史；不自动将结果当作完整模型上下文。
- 历史内容独立于 ModelMessage，Context 转换时须保留工具关联、实际结束原因和失败信息。

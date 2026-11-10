# Conversation

## 功能与定位

定义实际交互历史的公共协议，按 SessionId 关联用户输入、Assistant 输出、工具调用和结果。

当前已定义消息、类型化内容、追加和读取契约；没有 ConversationManager、存储端口或具体存储实现。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Models](./Models/Models.md) | 定义稳定 MessageId、会话内 Sequence 和三种历史内容。 |
| [Operations](./Operations/Operations.md) | 定义幂等追加、按 ID 查询和向更早历史分页的协议。 |

## 设计边界

- 对话是实际发生的交互记录，Memory 是从中提炼和管理的可复用信息，两者不能相互替代。
- SessionInfo 只携带轻量元数据，不包含不断增长的历史消息集合。
- 记录可以关联 TaskId，但一个任务不等于一条消息。
- 工具调用与结果需保留 Call ID 等结构，不能只保存显示文本。
- 本目录不声明存储路径或具体数据库，也不实现访问授权；对应管理规划见 [Runtime/Conversation](../../Runtime/Conversation/Conversation.md)。

ConversationContent 不继承 ModelMessage：历史保存事实和结束信息，Context 再选择、转换为本轮模型消息。
工具调用和 Observation 复用已有 Tools 类型；不把每轮动态系统提示、记忆摘要和 Skill 注入都追加成新聊天记录。
第一版只追加完整记录，没有编辑、删除、分支、全文搜索、流式片段或多模态附件协议。

MessageId 由可信调用方在首次追加前生成，重试复用；服务端分配 Sequence 和 CreatedAt。
首次读取默认最近 20 条，页内 Sequence 升序，使用不透明游标继续读取更早消息。
以上都是后续实现需要兑现的规则，不表示当前 DTO 已实现去重、排序或权限控制。

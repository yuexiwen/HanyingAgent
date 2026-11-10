# Requests

## 功能与定位

定义记录追加与历史分页输入，不暴露存储路径或客户端指定顺序。

当前无子目录，包含：

| 请求 | 功能 |
|---|---|
| [AppendConversationMessageRequest](./AppendConversationMessageRequest.cs) | 提供稳定 Id、SessionId、可空 TaskId 和类型化 Content。 |
| [ListConversationMessagesRequest](./ListConversationMessagesRequest.cs) | 指定 SessionId、默认 20 的 PageSize 和可空 ContinuationToken。 |

## 追加约定

- Id 首次调用前生成，重试保持原 ID 和完整原始载荷；客户端不指定 Sequence 或 CreatedAt。
- TaskId 可空不等于允许忽略任务归属；任务交互须关联任务，工具调用与结果必须有 TaskId。
- JSON 中 Id、SessionId、Content 必须存在，具体 ID 和内容合法性仍需入口校验。
- 工具参数字符串按原文参与重试比较，调用方不应在重试时改变 JSON 格式后仍声称是完全相同载荷。

## 分页约定

- PageSize 必须大于零，不静默截断或改成默认值。
- ContinuationToken 为 null 表示首次读取，空字符串或空白令牌是输入错误。
- 令牌不透明，绑定 Session、PageSize 和遍历边界，后续请求保持相同查询范围；非法或不匹配令牌显式报错。
- 第一页读取最近记录；后续页只返回上一页最小 Sequence 之前的记录，不重叠、不反向改为读取新消息。
- 页内始终按 Sequence 升序。新追加消息序号更大，不插入已经开始的向更早历史遍历。

例如有 100 条记录，PageSize 为 20，首次返回 81..100，下一页返回 61..80，直到最早记录。
这是向更早历史分页，不是实时追踪后续新消息；刷新最新历史需重新发起首次查询。
按 ID 获取只需要 MessageId，不新增空请求包装。

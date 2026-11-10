# Operations

## 功能与定位

定义完整历史记录的追加、单条查询和按会话分页读取，不管理 Task 执行或界面状态。

## 能力接口

[IConversationManager](./IConversationManager.cs) 提供：

| 操作 | 行为约定 |
|---|---|
| AppendAsync | 校验并追加新消息，分配 Sequence 和 CreatedAt；相同 ID 的相同重试返回原记录。 |
| GetAsync | 返回指定消息，不存在返回 null；访问权限仍需验证。 |
| ListBySessionAsync | 首次读取最近一页，页内升序，游标指向更早消息。 |

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Requests](./Requests/Requests.md) | 定义带稳定 ID 的追加输入与按 Session 查询历史的参数。 |
| [Responses](./Responses/Responses.md) | 定义一页记录与读取更早历史的不透明令牌。 |

## 追加与重复处理

- 先校验输入、访问权限和归属；新的记录只允许追加到有效且允许写入的 Session，Closed 拒绝新记录。
- MessageId、SessionId、TaskId 与内容相同的重试返回原消息，不改序号、时间，也不再次记录工具结果。
- 同一 MessageId 的归属或内容不同，显式报冲突，不覆盖旧记录、不生成另一 ID。
- 重试内容按类型和字段值比较，列表按顺序逐项比较，字符串使用 Ordinal，包括 ArgumentsJson 原文；不能使用集合引用相等代替内容比较。
- 重复判断、调用关联校验、序号分配和新记录保存须并发安全协调，不能采用先查再无条件写入。
- 对已提交消息的相同重试，具备记录访问权时可在 Session 关闭后返回原记录，但不允许追加新记录。
- 不自动创建 Session 或 Task，不因追加而隐式恢复任务或修改 Task Version；相关业务状态由 Orchestration 协调。

## 读取与错误

- 默认读取最近 20 条，按 Sequence 升序返回；后续页读取更早记录。
- 未知 Session 显式报不存在错误；存在但无历史时返回空页；Closed 历史仍可读取。
- 非法默认 ID、空请求、空白内容、非法页大小、未知内容类型、非法枚举或工具关联不合法都显式报错。
- 不静默丢弃非法消息，不用空页掩盖权限、游标或存储错误。
- CancellationToken 取消本次调用或等待，不保证撤销已提交记录；超时后重试必须复用原 MessageId。

以上行为尚未实现；目前接口和 DTO 不执行去重、数据库事务、权限判断或消息顺序验证。

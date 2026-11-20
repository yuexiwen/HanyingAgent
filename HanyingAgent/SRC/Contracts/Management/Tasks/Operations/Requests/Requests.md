# Requests

## 功能与定位

定义任务操作输入，区分新工作、当前任务回答和会话历史查询。

当前无子目录，包含以下类型：

| 请求 | 功能 |
|---|---|
| [SubmitTaskRequest](./SubmitTaskRequest.cs) | 指定 SessionId 和最初 Input，申请创建新任务。 |
| [ProvideTaskInputRequest](./ProvideTaskInputRequest.cs) | 指定原 TaskId、ExpectedVersion 和补充 Input，回答当前等待。 |
| [ListSessionTasksRequest](./ListSessionTasksRequest.cs) | 指定 SessionId、可选状态、页大小和可选延续令牌。 |

## 校验与分页约定

- ID 必须有效，输入不可为空白，ExpectedVersion 非负；这些由后续实现校验。
- PageSize 默认 20 且必须大于零；不通过静默截断或默认值掩盖非法输入。
- 首次查询不传 ContinuationToken，后续使用上一页返回的令牌，Session、状态和页大小保持一致。
- 令牌是后端生成的不透明游标，不能当作 SQL 或由调用方自行拼装；非法或不匹配令牌显式报错。
- 历史按 CreatedAt 升序、Id.Value 的 Ordinal 升序排序；每页是读取时快照，不承诺并发变化期间完整遍历同一数据库快照。
- 已存在但没有任务的 Session 返回空页，未知 Session 显式报不存在错误，Closed Session 仍允许读取历史。

按 TaskId 获取、结果查询和取消只需要一个 ID，不创建无意义的请求包装。

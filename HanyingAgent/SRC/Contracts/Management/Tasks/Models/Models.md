# Models

## 功能与定位

定义任务公共身份、归属和进度快照，不混入完整对话、工具输出或本地执行资源。

当前无子目录，包含以下类型：

| 类型 | 功能 |
|---|---|
| [TaskId](./TaskId.cs) | 以只读 record struct 包装任务标识，显式构造拒绝空白并支持 JSON 反序列化。 |
| [TaskInfo](./TaskInfo.cs) | 表达任务身份、所属 Session、原始输入、状态、版本及创建、更新时间和可空结束时间。 |
| [TaskStatus](./TaskStatus.cs) | 区分 Created、Running、三种等待、Cancelling 与三种终态。 |

## 生命周期约定

- Created 表示已接受但尚未推进，已占用 Session，不是跨任务队列。
- Running、WaitingForModel、WaitingForTool、WaitingForUser、Cancelling 都属于未结束。
- Completed、Failed、Cancelled 是终态；进入终态后不重新启动，下一项工作使用新的 TaskId。
- Cancelling 表示已请求取消，不代表已经停止，最终可能因完成与取消竞争而进入其他终态。

## 快照约定

- Id、SessionId、Input、CreatedAt 创建后不变；Input 是最初工作请求，不是全部对话。
- Version 从 0 开始，任务快照发生变更时递增；重复取消没有变化时不递增。
- UpdatedAt 不早于 CreatedAt 且不倒退；未结束时 CompletedAt 为 null，终态时须有完成时间。
- TaskInfo 是查询快照，不保证调用方使用时仍是最新；不能仅凭快照无条件启动或恢复任务。

除 TaskId 构造校验外，这些都是后续实现需要维护的规则，DTO 不自动强制执行。
TaskStatus 与 .NET 的异步任务状态同名，在其他命名空间使用时可采用类型别名避免歧义。

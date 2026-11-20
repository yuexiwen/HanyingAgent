# Responses

## 功能与定位

定义操作特有的任务查询结果，将历史分页和执行结果与 TaskInfo 快照分开。

当前无子目录，包含以下类型：

| 类型 | 功能 |
|---|---|
| [TaskListResponse](./TaskListResponse.cs) | 返回一页 TaskInfo 以及可空的下一页 ContinuationToken。 |
| [TaskResult](./TaskResult.cs) | 返回 TaskId、终态、最终回答或错误，以及完成时间。 |

## 结果约定

- Items 不为 null，数量不超过请求 PageSize；没有下一页时 ContinuationToken 为 null。
- TaskResult 只使用 Completed、Failed、Cancelled，未结束任务不产生终态结果。
- Completed 提供非空白 FinalAnswer，Error 为 null；Failed 提供非空白 Error，FinalAnswer 为 null。
- Cancelled 的 FinalAnswer 与 Error 都为 null，取消不是执行失败。
- TaskResult 的 TaskId、状态和 CompletedAt 与对应终态 TaskInfo 一致。
- 这些约束由后续实现验证，record 本身不执行完整业务校验。

工具产物和完整历史归所属模块管理，当前不在结果中堆积所有工具输出。

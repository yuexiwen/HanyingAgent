# Management

定义运行管理的公共协议，不包含线程池、执行句柄、存储实现或入口框架。

| 子模块 | 职责 |
|---|---|
| [Users](./Users/Users.md) | 用户身份标识、资料快照和资料操作。 |
| [Sessions](./Sessions/Sessions.md) | 会话归属、状态和会话操作。 |
| [Tasks](./Tasks/Tasks.md) | 任务生命周期、结果和提交、查询、取消等操作。 |

对应实现位于 [Runtime/Management](../../Runtime/Management/Management.md)。
归属关系为 User → Session → Task，不表示这些数据对象自行执行工作。
Agent 契约可以引用这里的 ID 表达归属；知道 ID 不等于具有访问权。
本次目录和命名空间调整不改变状态枚举值、JSON 字段或业务取消语义。

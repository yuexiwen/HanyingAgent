# Sessions

## 功能与定位

定义长期会话的公共数据与管理能力，使 Host 和 Runtime 能通过统一协议创建、查询、列举和关闭会话。

当前已定义契约，尚未实现 SessionManager、会话存储或任务调度。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Models](./Models/Models.md) | 定义会话标识、公共信息和 Idle / Active / Closed 生命周期。 |
| [Operations](./Operations/Operations.md) | 定义会话管理接口及调用输入，不混入任务执行或取消能力。 |

## 生命周期与并发约定

- 一个 Session 可以承载多个先后执行的 Task，但同时最多只有一个未结束任务。
- Idle 没有当前任务；Active 必须关联 CurrentTaskId；Closed 没有当前任务且不能重新执行。
- 等待模型、工具或用户时，任务仍未结束，Session 保持 Active，而不是空闲。
- Active 时提交新任务显式报冲突，不排队；当前任务的用户补充不是新任务。
- 当前任务完成、失败或确认取消后，清除关联并回到 Idle；这些是后续执行协调规则。
- 关闭 Active 会话显式报冲突，需先结束当前任务；关闭不等于取消，也不等于删除历史。

## 设计边界

任务接收与会话占用必须在后续实现中原子协调，不能采用先 Get 再无条件启动的竞态流程。
新任务提交、等待输入、取消和历史查询已由 [Tasks](../Tasks/Tasks.md) 的 ITaskManager 定义，事件观察仍待后续契约设计。
SessionInfo 不携带执行 Task、取消源或锁；实际生命周期协调由 Runtime/Orchestration 实现。

## 用户归属

- 每个 Session 必须关联一个有效 UserId，一个 User 可以拥有多个 Session；本轮不设计共享会话或转移归属。
- CreateSessionRequest 指定所属用户，SessionInfo 保存该 UserId，不嵌入完整 UserInfo。
- 创建时后续实现必须验证用户存在、调用方有权为其创建会话；UserId 本身不是访问授权。
- Runtime 查询 Session 后，可按 UserId 调用 IUserManager 获取资料，或构造 User 级 MemoryOwner 召回记忆。
- 用户显示名称变化不修改会话归属或会话名称；UserInfo 不重复保存全部 SessionId。
- 当前 ListAsync 仍列举可见会话，没有新增按用户过滤接口；未来存储可按 UserId 建索引。

创建参数与会话 JSON 均新增必需 UserId。缺少该字段的旧 JSON 显式反序列化失败；
旧调用和旧数据需要明确传入或迁移所属用户，不自动使用默认 ID、当前调用者或临时用户。
JsonRequired 只要求字段存在，不替代有效性、存在性和权限校验。

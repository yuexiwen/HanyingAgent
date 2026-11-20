# Sessions

[LocalSessionManager](./LocalSessionManager.cs) 实现 ISessionManager，提供创建、查询、列举和关闭。
创建仅允许 LocalUserContext 的用户，Store 再确认用户存在；读取、关闭和执行都按保存的 UserId 校验，
知道其他会话 ID 不会获得访问权。不存在的查询返回 null，写操作报错，名称不是身份键。

[ISessionStore](./ISessionStore.cs) 是内部存储端口，内存实现见
[InMemorySessionStore](../../../Infrastructure/Persistence/InMemorySessionStore.cs)。
各 Store 独立实现接口，通过内部共享状态与锁协调会话占用，不通过统一封装类访问。
会话不嵌入消息集合，通过 SessionId 读取独立 Conversation。

正式任务接受时原子设置 Active 与 CurrentTaskId，任务结果保存时原子恢复 Idle 并清空关联；
忙时拒绝新任务和关闭，不排队。任务生命周期见 [Tasks](../Tasks/Tasks.md)。
BeginTurnAsync 保留为可信进程内独立对话入口，使用同一 Session.Active 状态保护，不创建伪造 TaskId。
任务路径不再获取第二份 turn lease，两种入口互斥；独立对话异常和取消也释放占用。
不同会话可以分别执行。这是进程内协调，不是跨进程锁。
Closed 会话允许读取历史、重复关闭，拒绝新输入；关闭不删除历史。
直接追加历史属于可信进程内 API，不用于替代 DialogueOrchestrator 发起执行。

Console 通过 `/new NAME` 创建并选择、`/session [ID]` 查询详情、`/sessions` 列表、
`/use INDEX|ID` 选择会话。列表编号从 1 连续排列，隐藏后重排，不作为 SessionId 保存。
`/delete INDEX` 调用 HideAsync，仅从可见列表隐藏；当前选择被隐藏时取消选择。
隐藏由 Store 校验归属并记录，保留会话、历史、任务与执行；重复隐藏幂等，完整 ID 仍可访问。
隐藏不是 Close，不阻止运行任务正常完成。当前选择仅保存在 Host；查询指定 ID 不会改变选择。
任务摘要由 Host 使用任务查询协议组合，不把任务集合放进 SessionInfo。
详情和列表显示当前或最近任务，以及是否进入 Completed / Failed / Cancelled 终态；
Idle 不等于成功，Cancelling 不等于已经结束，没有任务也不会标成任务完成。

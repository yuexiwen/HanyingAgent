# Sessions

[LocalSessionManager](./LocalSessionManager.cs) 实现 ISessionManager，提供创建、查询、列举和关闭。
创建仅允许 LocalUserContext 的用户，Store 再确认用户存在；读取、关闭和执行都按保存的 UserId 校验，
知道其他会话 ID 不会获得访问权。不存在的查询返回 null，写操作报错，名称不是身份键。

[ISessionStore](./ISessionStore.cs) 是内部存储端口，内存实现见
[InMemoryDialogueStore](../../Infrastructure/Persistence/InMemoryDialogueStore.cs)。
会话不嵌入消息集合，通过 SessionId 读取独立 Conversation。

本期没有正式任务。会话保持 Idle / Closed、CurrentTaskId 为空，不伪造 Active 或 TaskId。
BeginTurnAsync 使用进程内临时 lease 拒绝同一会话重复执行以及执行期间关闭；异常和取消也释放 lease。
不同会话可以分别执行。该保护不是持久化任务状态，也不保证未来跨进程任务协调。
Closed 会话允许读取历史、重复关闭，拒绝新输入；关闭不删除历史。
直接追加历史属于可信进程内 API，不用于替代 DialogueCoordinator 发起执行。

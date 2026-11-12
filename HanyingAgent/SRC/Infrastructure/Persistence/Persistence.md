# Persistence

## 功能与定位

实现存储端口与 Checkpoint 保存机制，隔离内存、文件和数据库等技术差异。

当前已实现工作记忆 Store，以及用户、会话和对话的进程内存 Store；磁盘存储与 Checkpoint 仍待实现。

## 已有实现

[InMemoryWorkingMemoryStore](./InMemoryWorkingMemoryStore.cs) 实现
[IWorkingMemoryStore](../../Capabilities/Memory/IWorkingMemoryStore.cs)，提供新增、获取、原子更新、物理删除和精确 Owner 快照读取。

- 使用实例级 Dictionary 与短锁保护全部集合访问，每个 Store 实例的数据彼此独立。
- 不访问外部资源，内存操作同步完成后返回已完成的 Task；不使用 Task.Run 包装存储操作。
- 重复新增抛 InvalidOperationException；未知读取返回 null；未知更新或删除抛 KeyNotFoundException。
- 更新完整替换允许修改的字段，保留 Id、Owner、Type、CreatedAt，UpdatedAt 取已存时间与候选时间的较晚者。
- 后应用的更新覆盖内容，不按候选时间拒绝更新，也不实现 Version 检查。
- 按 Owner 扫描，在锁内物化为独立数组；记录为不可变数据，不返回底层集合或延迟枚举器。
- 不过滤 Type、状态或有效期，不排序或应用 Limit；这些由工作记忆 Manager 负责。
- 检查 null、默认 ID 和空 Owner 等基础存储入参；内容、分值与枚举等业务校验由工作记忆 Manager 负责。
- 调用开始和取得锁后检查取消；普通 lock 的等待不能被 CancellationToken 中断，取消不回滚已提交变更。
- 进程退出或实例丢弃后数据丢失；不提供持久化、容量淘汰或权限验证。

行为测试见 [InMemoryWorkingMemoryStoreTests](../../../TEST/HanyingAgent.Tests/WorkingMemory/InMemoryWorkingMemoryStoreTests.cs)。

### 本地对话 Store

[InMemoryDialogueStore](./InMemoryDialogueStore.cs) 实现 IUserStore、ISessionStore、IConversationStore。
三个端口共享短锁与集合，使用户存在性、会话归属、关闭检查、消息去重和 Sequence 分配在同一实例内原子执行。
Store 按管理器传入的可信 owner 验证归属；这些内部端口不是远程授权接口。
关闭会话与执行 lease 互斥，但不借此伪造 Task 状态，也不提供跨进程锁。
历史只追加，分页按 Sequence 定位，记录快照不随新消息改变。
内存操作同步完成，不用 Task.Run 伪造异步 IO；锁内检查取消，已提交的操作不回滚。
无容量淘汰与磁盘持久化；窗口淘汰的是发送给模型的历史，不删除 Store 记录。

## 后续组件规划

| 组件 | 一句话概述 |
|---|---|
| User Store | 保存用户标识、显示名称和资料时间，不承担登录或认证。 |
| 工作记忆持久化适配 | 在已有内存实现之外，按需增加 SQLite 等可替换适配器。 |
| 长期记忆存储 | 保存可修正、可删除且可供索引重建的权威记录。 |
| State Store | 保存执行状态和并发版本数据。 |
| Conversation Store | 按 SessionId 和 Sequence 保存历史，支持稳定 MessageId 去重与向更早记录分页。 |
| JsonCheckpointStore | 在安全点持久化可恢复 Session 的快照。 |
| SQLite 适配 | 在需要时提供本地数据库实现，不改变上层能力接口。 |

## 设计边界

存储不决定记忆分类、权限政策或执行策略；这些由对应业务模块管理。
长期搜索索引是派生数据，不能代替权威记录；离线或增量索引更新应可恢复并覆盖更新与删除。
Checkpoint 保存所需工作记忆，不复制整个 User / Global 长期数据集或索引；不序列化锁和取消源。
Conversation Store 的接口与管理边界见 [Runtime/Conversation](../../Runtime/Conversation/Conversation.md)；
可以与其他模块共用 SQLite 数据库，但不把所有历史内容塞进 SessionInfo。
User Store 已提供内存版，公共协议见 [Contracts/Users](../../Contracts/Users/Users.md)。
会话记录必须保存 UserId，未来可为其建立索引；旧记录需要明确迁移归属，不能恢复成默认用户或当前调用者。

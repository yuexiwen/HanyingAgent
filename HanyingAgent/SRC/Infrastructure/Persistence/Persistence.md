# Persistence

## 功能与定位

实现存储端口与 Checkpoint 保存机制，隔离内存、文件和数据库等技术差异。

当前已实现工作记忆 Store，以及用户、会话、对话和任务的进程内存 Store；磁盘存储与 Checkpoint 仍待实现。

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

### 独立的本地 Store

每个 Store 类实现一个存储接口，管理器直接注入所需接口；
不再使用 InMemoryDialogueStore / InMemoryAgentStore 统一实现四个接口，也不增加组合转发入口：

| 文件 | 职责 |
|---|---|
| [InMemoryUserStore.cs](./InMemoryUserStore.cs) | 仅实现 IUserStore，负责用户存储操作。 |
| [InMemorySessionStore.cs](./InMemorySessionStore.cs) | 仅实现 ISessionStore，负责会话存储操作及独立对话 TurnLease。 |
| [InMemoryConversationStore.cs](./InMemoryConversationStore.cs) | 仅实现 IConversationStore，负责消息保存、幂等比较、顺序及历史分页。 |
| [InMemoryTaskStore.cs](./InMemoryTaskStore.cs) | 仅实现 ITaskStore，负责任务状态、结果及任务分页。 |
| [InMemoryAgentState.cs](./InMemoryAgentState.cs) | 内部共享集合、同步锁及跨数据归属校验；不实现存储接口，不持有或转发 Store。 |

DI 分别注册四个 Store 单例，通过工厂为它们注入同一个内部 InMemoryAgentState 单例。
Store 的构造函数为 internal，由组合根创建，不对业务调用方暴露共享可变状态。
不同 Host 容器拥有独立状态，不使用静态集合或全局锁。
Store 之间不相互依赖；所有集合访问及校验都在共享 Gate 内完成。
因此用户存在性、会话归属、关闭检查、消息去重和 Sequence 分配保持原有原子边界。
Store 按管理器传入的可信 owner 验证归属；这些内部端口不是远程授权接口。
关闭会话与 Session.Active 占用互斥；独立对话 lease 与正式任务使用同一状态，不提供跨进程锁。
历史只追加，分页按 Sequence 定位，记录快照不随新消息改变。
内存操作同步完成，不用 Task.Run 伪造异步 IO；锁内检查取消，已提交的操作不回滚。
无容量淘汰与磁盘持久化；窗口淘汰的是发送给模型的历史，不删除 Store 记录。

[任务存储实现](./InMemoryTaskStore.cs) 在同一短锁内创建 Task 并占用 Session，
更新状态与 Version，以及提交终态结果并释放 Session。结果记录只写一次，迟到的旧任务收尾不释放新占用。
任务按会话提交顺序分页，不依赖时钟或随机 ID 排序；消息追加校验 Task 与 Session 当前关联。
锁内不调用模型、等待异步执行或触发取消回调。本地执行句柄和取消源由 Runtime/Tasks 管理。
这是独立实现类的职责拆分，不是独立事务或远程服务：
任务操作仍需关联会话数据，消息追加仍需校验当前任务，相关数据在当前内存适配中属于同一一致性域。
以后独立替换为数据库或远程存储时，需要重新落实跨数据事务或协调协议，不能为某个 Store 随意创建另一份状态。
相关类图见 [TaskModel](../../../../.docs/ClassDiagram/TaskModel.md)。
行为回归见 [TaskTests](../../../TEST/HanyingAgent.Tests/Tasks/TaskTests.cs)，
包含跨 Store 占用与释放、并发关闭与提交，以及跨 Host 数据隔离。

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
Conversation Store 的接口与管理边界见 [Capabilities/Conversation](../../Capabilities/Conversation/Conversation.md)；
可以与其他模块共用 SQLite 数据库，但不把所有历史内容塞进 SessionInfo。
User Store 已提供内存版，公共协议见 [Contracts/Users](../../Contracts/Users/Users.md)。
会话记录必须保存 UserId，未来可为其建立索引；旧记录需要明确迁移归属，不能恢复成默认用户或当前调用者。

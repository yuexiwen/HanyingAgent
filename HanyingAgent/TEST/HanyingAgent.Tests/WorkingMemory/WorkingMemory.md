# WorkingMemory

## 功能与定位

承载工作记忆的独立测试。当前验证已有契约、真实内存 Store、隔离存储依赖的 Manager 业务规则，
以及真实 Manager + Store 的跨层边界和并发行为。

## 当前测试

| 文件 | 覆盖内容 |
|---|---|
| [MemoryIdTests.cs](./MemoryIdTests.cs) | 空值守卫、原值保留、区分大小写的值相等、JSON 往返、default 绕过构造的限制。 |
| [MemoryOwnerTests.cs](./MemoryOwnerTests.cs) | 四种合法归属、缺失或多余 ID、未知 Scope、默认 ID、值相等及 JSON 往返。 |
| [InMemoryWorkingMemoryStoreTests.cs](./InMemoryWorkingMemoryStoreTests.cs) | 新增、读取、更新、删除、归属精确匹配、快照隔离、明确错误、预取消和基本并发访问。 |
| [LocalWorkingMemoryManagerTests.cs](./LocalWorkingMemoryManagerTests.cs) | 请求校验、ID 与时间生成、操作委托、可读状态、到期边界、Type、排序、Limit、取消和错误传播。 |
| [WorkingMemoryBoundaryTests.cs](./WorkingMemoryBoundaryTests.cs) | 真实 Manager + Store 的读写可见性、精确归属、状态转换、到期、时钟回退、快照、取消和并发边界。 |

Store 测试直接创建真实实例，不启动 Agent，也不使用 Mock 库或真实时间等待。
已验证精确 Owner 读取不会混入其他归属，但不验证用户存在性、任务与会话的真实关系或授权。
默认 MemoryId 的 Value 为 null，Store 和 Manager 的 ID 入口均已拒绝默认值。

## Manager 测试边界

- 使用测试文件内的 RecordingStore 记录参数、令牌、调用次数并注入候选或异常，不引入 Mock 库。
- 验证生成不同 ID、Active 初始状态、创建与更新时间一致，原始内容和来源不被隐式修剪。
- 写入与更新均校验空白内容、分值上下界、NaN / Infinity、枚举；拒绝空请求、空 Owner、默认 ID 和非正 Limit。
- 将各 Scope 的完整 Owner 原样交给 Store；实际跨归属存储隔离由 Store 测试验证，不假装已经验证授权。
- 更新接受全部合法状态、过去或当前有效期及 null 清除；返回 Store 实际结果，不自己覆盖 Store 的时间保护。
- 查询验证到期前一个 tick、恰好到期、到期后一个 tick、统一 now、状态与 Type 过滤、Ordinal 排序和先过滤后 Limit。
- 使用异步快照替身确认查询在获取快照后判断时间，并观察等待期间的取消。
- 异常原样传播，预取消不访问 Store，无自动重试；不靠真实时间等待。
- 这些单元测试验证 Manager 的独立职责，真实组合边界由 WorkingMemoryBoundaryTests 补充，不混淆两类证据。

## 第四步：跨层边界与并发

[WorkingMemoryBoundaryTests](./WorkingMemoryBoundaryTests.cs) 中每个测试独立创建真实内存 Store、
真实 Manager 和可控时钟，业务操作通过 IWorkingMemoryManager 发起，必要时读取 Store 核对原始记录。
不以替身代替 Manager 或存储，不启动 Agent，不连接外部服务。

当前包含 50 个用例：

- 写入、更新和遗忘后立即读取，确认业务返回值与真实存储一致。
- Task、Session、User、Global 精确隔离，覆盖同会话不同任务、不同会话相同任务 ID 和 ID 大小写。
- 写入接受过去、当前和未来有效期；到期相等时不可见，但原始状态、时间和底层记录不被查询改动。
- 全部 25 种合法状态转换组合；重新激活不会绕过有效期，完整更新中 null 可以清除有效期。
- 回拨时钟后仍应用新内容，保留身份、归属、分类、创建时间，UpdatedAt 不倒退。
- 过滤后再应用默认 20 条或显式 Limit，更新时间降序、同时间 ID Ordinal 升序，Importance 不影响排序。
- 更新、新增、删除之后，先前已返回的 Manager 结果和 Store 快照仍保持原值。
- 非法更新不产生部分修改；未知或已删除目标报不存在错误，不自动重新创建。
- 四种业务操作预取消不修改 Store；测试时钟在校验后、存储前触发取消，验证写入和更新的令牌确实传递到底层。
- 多线程并发写入与查询不丢失记录、不串归属；同 ID 更新不混合字段，查询看到的 UpdatedAt 不倒退。
- 同 ID 更新与遗忘竞争，允许更新先成功或因删除而报不存在，但最终不复活已删除记录。

并发测试通过 Task.Run 与共同启动信号发起竞争，不通过 Thread.Sleep 或特定线程顺序判定成功。
同 ID 更新测试使用线程安全、逐 tick 推进的测试时钟，验证最终时间等于所有更新结果的最大时间。
这些测试验证可观察行为，不证明所有调度都已穷尽，也不承诺取消中断 lock 等待或回滚已提交变更。

## Store 行为边界

- 完整替换可修改字段，保留身份、归属、分类和创建时间；原记录与旧快照保持不变。
- 候选时间较早、相同或较晚时都更新内容，已存 UpdatedAt 不倒退。
- ExpiresAt 可以清除或保留指定值；候选读取包含不可读状态、已到期项、多种 Type 和超过 20 条的数据。
- 预取消操作抛出取消异常，不修改记录；不宣称测试了锁等待中断或提交后回滚。
- 并发测试使用 Task.Run 与共同启动信号，让多个调用竞争同一 Store；不只对同步完成的 Task 做 WhenAll。
- 验证不同 ID 的并发读写与删除、重复新增只有一个成功、同 ID 更新载荷完整及更新时间不倒退。

## 第五步统一验收与后续边界

2026-11-09 已完成 [WorkingMemory 计划](<../../../../.docs/WorkingMemory 最小实现与演进计划.md>) 的第五步：
统一运行基础契约、测试时钟、Store、Manager 和跨层组合测试，共 252 项通过、0 失败、0 跳过。
解决方案零警告构建，应用入口行为不变，测试源码未进入应用编译。

内存版 MVP 的基本读写、归属过滤、有效期、排序、并发保护和独立测试已验收。
这不代表已完成 Agent 接入、生产级并发保证、持久化、资源容量控制或授权。
新增适配器或接入 Context / Agent 时，应继续复用已有契约并增加相应的适配与调用链测试。

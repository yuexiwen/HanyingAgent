# Memory

## 功能与定位

实现记忆业务规则，区别于 [Contracts/Memory](../../Contracts/Memory/Memory.md) 中的数据与接口声明。

当前已定义内部存储端口并实现工作记忆 Manager，没有子目录，长期记忆 Manager 与 Retriever 尚未实现。
具体内存 Store 已在 [Infrastructure/Persistence](../../Infrastructure/Persistence/Persistence.md) 实现并配套独立测试。
第一版仅实现工作记忆，范围、验收与后续能力见 [WorkingMemory 最小实现与演进计划](<../../../../.docs/WorkingMemory 最小实现与演进计划.md>)；
当前已完成五步：规则与测试基础、内存 Store、工作记忆 Manager、真实组合边界与并发测试、统一验收。
内存版 WorkingMemory MVP 已验收，相关正式测试共 252 项通过；未接入 Agent 入口。

## 已有管理器

[LocalWorkingMemoryManager](./LocalWorkingMemoryManager.cs) 实现公共 IWorkingMemoryManager，
通过构造函数注入 IWorkingMemoryStore 和 TimeProvider；不依赖具体存储技术。

- 写入校验请求后生成 MemoryId，初始 Active，CreatedAt 与 UpdatedAt 使用同一次时钟读取。
- 更新校验完整载荷，将候选时间交给 Store 原子应用；不先 Get 再覆盖，不自行实现状态转换矩阵。
- 遗忘校验 ID 后调用物理删除，不将未知目标伪装成成功。
- 内容与来源拒绝 null 或空白；两个分值必须为有限值且在 [0, 1]；拒绝未定义枚举与默认 ID。
- MemoryOwner 的合法 Scope / ID 组合由其不可变构造模型保证，Manager 额外拒绝 null Owner。
- 查询将完整 Owner 传给 Store，依赖端口的精确归属快照约定；不自动扩展至其他归属。
- 获取快照后只读取一次当前时间，按可选 Type、Active / Confirmed、严格未到期过滤。
- 按 UpdatedAt 降序、Id.Value Ordinal 升序排序，最后应用正数 Limit（默认 20），返回物化数组。
- ExpiresAt 可以为 null、过去、当前或未来时间；到期只影响读取，不自动改状态或删除。
- 所有操作传递 CancellationToken，不吞存储错误或自动重试；查询在异步快照返回后再次检查取消。
- 业务错误使用 ArgumentException / ArgumentOutOfRangeException 等标准异常显式报告；
  未知变更目标等 Store 错误原样传播，不增加独立错误框架。

生产组合应注入 TimeProvider.System 和共享 Store 实例。Manager 单元测试使用手写记录型 Store 替身，
跨层边界测试使用真实 Manager 与内存 Store；均使用可控时钟，不启动 Agent，也不连接真实模型或数据库。

## 调用示例

以下是后续入口组合和调用的示例，不表示当前 Program 已接入记忆链路：

```csharp
using HanyingAgent.Capabilities.Memory;
using HanyingAgent.Contracts.Memory.Models;
using HanyingAgent.Contracts.Memory.Operations;
using HanyingAgent.Contracts.Memory.Operations.Requests;
using HanyingAgent.Infrastructure.Persistence;

IWorkingMemoryStore store = new InMemoryWorkingMemoryStore();
IWorkingMemoryManager memory = new LocalWorkingMemoryManager(store, TimeProvider.System);
var owner = new MemoryOwner(MemoryScope.Global);

var item = await memory.WriteAsync(
    new MemoryWriteRequest(owner, MemoryType.Fact, "Learning Agent development", "example", 0.8, 0.6),
    CancellationToken.None);

var recent = await memory.ListRecentAsync(new MemoryReadRequest(owner), CancellationToken.None);

var updated = await memory.UpdateAsync(
    item.Id,
    new MemoryUpdateRequest("Learning working memory", "example", 0.9, 0.7, MemoryStatus.Confirmed, null),
    CancellationToken.None);

await memory.ForgetAsync(updated.Id, CancellationToken.None);
```

示例分值只是调用方指定的数据，不是系统默认值或校准概率；未设置 ExpiresAt 时没有时间到期限制。
同一记忆模块复用同一个 Store 实例，否则不同实例的数据互不可见。
Global 仅指当前 Agent 实例；真实业务应选择正确的 Task、Session 或 User 归属。
当前数据不持久化，没有资源容量限制、授权、自动提取或评分；验收范围不包含长期记忆与 Agent 编排。

## 已有端口

[IWorkingMemoryStore](./IWorkingMemoryStore.cs) 声明新增、单条获取、原子更新、物理删除和精确 Owner 快照读取。
这是模块内部的架构端口，不是公共远程契约；使用 public 便于基础设施和测试引用。

- Store 负责存储原子性、重复 ID 冲突、未知变更目标错误及脱离底层集合的快照。
- 原子更新保留不可修改字段，UpdatedAt 取已存时间与候选时间的较晚者；并发采用后应用覆盖。
- Manager 负责业务校验、初始 Active、可读状态、有效期、排序和 Limit。
- Store 按 Owner 返回候选，包括不可读或已到期项，不代替 Manager 决定业务可见性。

## 后续组件规划

| 组件                       | 一句话概述                                       |
| -------------------------- | ------------------------------------------------ |
| LocalLongTermMemoryManager | 管理长期权威记忆并协调可替换的相关性检索。       |
| ILongTermMemoryStore       | 声明长期权威记录的读写与核对能力。               |
| ILongTermMemoryRetriever   | 声明索引候选召回能力，隔离具体搜索后端。         |
| 记忆整合流程               | 选择、压缩和去重工作信息后创建带来源的长期记忆。 |

## 设计边界

- 当前校验内容、分值、归属结构与查询数量，处理更新和遗忘；真实身份授权、存储配额与资源容量限制尚未实现。
- 工作读取只返回指定归属下有效项；长期搜索还要核对当前权威记录，过滤陈旧索引命中。
- 默认直接读取按 UpdatedAt 降序、ID Ordinal 升序打破平局，长期搜索按相关性分数降序。
- 存储技术和索引构建不在本模块内硬编码；分词、Embedding 与索引由基础设施适配。
- 当前没有自动记忆提取器，也不自动将用户信息提升为 Global。

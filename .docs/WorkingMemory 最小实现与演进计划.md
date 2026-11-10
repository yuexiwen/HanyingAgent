# WorkingMemory 最小实现与演进计划

## 1. 目标与当前状态

先实现一个支持学习 Agent 开发的简单工作记忆系统，而不是建设生产级记忆平台。
目标是用可预测、可独立测试的读写能力，支持后续 Context 和执行流程开发。

```text
调用方明确提供内容和归属
  → 保存工作记忆
  → 按约定读取
  → 更新或遗忘
```

当前已经定义 Memory 的公共契约，以及第一步的内部存储端口、正式 xUnit 测试项目和测试用可控时钟。
已实现进程内存 Store，并增加其独立读写、归属快照、错误、取消与基本并发测试。
已实现工作记忆 Manager，并用手写 Store 替身与可控时钟验证业务校验、有效期过滤、排序及数量限制。
第四步已增加真实 Manager + Store 的跨层边界与并发测试；第五步已完成统一验收与范围核对。
截至 2026-11-09，内存版 WorkingMemory MVP 的五步均已完成，可供后续模块通过公共接口调用；
尚未接入 Agent 入口，也不是生产级记忆平台。验收结果与证据见第 6 节。
本文同时记录实现范围与演进计划，已确认的 MVP 策略集中列在第 8 节。

已确认第一版使用进程内存，重启清空；保留存储端口，后续可替换 SQLite。
限定单进程、少量数据、可信本地调用，不承诺生产级容量、持久性、认证授权或高可用。

## 2. 范围边界

WorkingMemory 是 Memory 模块的一部分，不是新的顶层模块。

| 维度 | 含义 |
|---|---|
| 工作记忆 / 长期记忆 | 区分使用方式和读取语义。 |
| Task / Session / User / Global | 区分数据归属，不直接等于保存期限。 |
| 进程内存 / SQLite | 具体存储技术，不决定记忆归属或是否属于工作记忆。 |

本期只实现 [IWorkingMemoryManager](../HanyingAgent/SRC/Contracts/Memory/Operations/IWorkingMemoryManager.cs)，
复用 [IMemoryWriter](../HanyingAgent/SRC/Contracts/Memory/Operations/IMemoryWriter.cs) 的变更方法。
不实现长期记忆检索，也不改变现有公共接口。

## 3. 本期必须实现的功能

| 功能 | 最小要求 |
|---|---|
| WriteAsync | 校验请求，生成 MemoryId 和服务端时间，保存新记录，返回 MemoryItem。 |
| UpdateAsync | 完整替换允许修改的字段，保留身份、归属、分类和创建时间，返回更新结果。 |
| ForgetAsync | 物理删除指定记录，后续读取不可见；未知目标显式报不存在错误。 |
| ListRecentAsync | 精确匹配 Owner，按可选 Type、状态和有效期过滤，排序并取前 Limit 条。 |
| 写后可见 | 变更成功后，随后直接读取反映该变更，不引入搜索索引延迟。 |
| 并发保护 | 防止集合损坏、枚举异常及单次变更部分生效，返回记录快照。 |
| 明确错误 | 非法输入、未知变更目标和存储失败显式报错，不伪造成功。 |
| 可独立测试 | 不启动完整 Agent，不连接真实 LLM、数据库、MCP 或用户服务。 |

### 3.1 输入校验

- 请求、Owner 和必需字段不可为 null，默认或无效 ID 必须拒绝。
- Owner 必须符合 Scope 的 ID 组合，Type、Status 必须是已定义枚举值。
- Content、Source 不可空白。
- Confidence、Importance 必须为有限数值，且在 [0, 1]，拒绝 NaN 和 Infinity。
- Limit 必须大于零，不静默改成默认值。
- 未知更新或遗忘目标显式报不存在错误，不自动创建。

现有 MemoryOwner 构造校验只验证结构，不证明 Task 属于 Session，不证明用户存在，也不完成授权。
第一版本地调用应来自可信业务入口，不能将 Owner 匹配测试当作跨用户访问安全已经实现。

### 3.2 数据组织与更新

直接使用 [MemoryItem](../HanyingAgent/SRC/Contracts/Memory/Models/MemoryItem.cs)：

| 分组 | 字段 |
|---|---|
| 身份和归属 | Id、Owner、Type |
| 内容和语义 | Content、Source、Confidence、Importance、Status |
| 时间 | CreatedAt、UpdatedAt、ExpiresAt |

- 新记录初始状态为 Active，不表示内容已证实；CreatedAt 与 UpdatedAt 相同，均由实现生成。
- 更新不修改 Id、Owner、Type、CreatedAt，不通过修改 Scope 实现记忆提升。
- 更新是完整替换，不是部分补丁；ExpiresAt 为 null 表示清除有效期。
- UpdatedAt 不能倒退，不能因并发使用旧整条记录覆盖较新的不可变字段或时间。
- 合法 MemoryStatus 之间允许切换，包括重新激活；不引入复杂转换矩阵。
- 写入和更新允许过去或当前的 ExpiresAt；状态切换不会清除或绕过独立的有效期条件。
- Confidence 和 Importance 由调用方提供，本期不自动评分。
- 不基于相同内容自动合并；不同写入可以生成不同记录，不声称具有语义去重。

具体请求见 [Memory Requests](../HanyingAgent/SRC/Contracts/Memory/Operations/Requests/Requests.md)。

### 3.3 查询规则

```text
校验请求
  → 获取指定 Owner 的记录快照
  → 按可选 Type 过滤
  → 保留 Active 或 Confirmed
  → 排除已到期记录
  → UpdatedAt 降序
  → 相同时按 Id.Value 的 Ordinal 升序
  → 取前 Limit 条
```

- Limit 默认 20。
- 同一次查询使用统一的 now 判断到期。
- ExpiresAt 为 null，或严格晚于 now，才满足有效期条件；恰好到期也不返回。
- Session 归属不会自动包含其下所有 Task 归属；多归属读取由调用方分别发起并合并。
- 不按关键词搜索、不计算相关性分数，也不默认按 Importance 排序。
- 读取不修改 Status、UpdatedAt 或其他业务字段。
- 到期后不返回，不代表底层记录已经物理清除；本期不依赖后台清理保证查询正确。

## 4. 最小实现结构

以下结构中的 Manager 与内存 Store 已分别实现、独立测试并完成真实组合验收；
后续 Agent 接入仍待完成：

```text
调用方
  ↓ IWorkingMemoryManager
LocalWorkingMemoryManager
  ├── 输入校验与业务规则
  ├── 有效期、排序与数量限制
  ├── 可控时钟
  └── IWorkingMemoryStore
        ↓
InMemoryWorkingMemoryStore
```

| 组件 | 建议位置 | 职责 |
|---|---|---|
| 管理器 | [LocalWorkingMemoryManager](../HanyingAgent/SRC/Capabilities/Memory/LocalWorkingMemoryManager.cs) | 已实现公共操作、业务校验与最近有效项查询。 |
| 内部存储端口 | [IWorkingMemoryStore](../HanyingAgent/SRC/Capabilities/Memory/IWorkingMemoryStore.cs) | 已定义新增、获取、原子更新、删除和按归属快照能力。 |
| 内存存储 | [InMemoryWorkingMemoryStore](../HanyingAgent/SRC/Infrastructure/Persistence/InMemoryWorkingMemoryStore.cs) | 已实现实例级 Dictionary、短锁保护和 Owner 候选快照。 |
| 时钟 | 注入管理器 | 使用 .NET TimeProvider 提供可控时间，不依赖等待真实时间。 |
| 测试 | [HanyingAgent.Tests](../HanyingAgent/TEST/HanyingAgent.Tests/HanyingAgent.Tests.md) | 已建立正式 xUnit 项目，通过 ProjectReference 引用应用。 |

### 4.1 存储方式

主结构已使用 Dictionary<MemoryId, MemoryItem>，小数据量下按 Owner 扫描获取候选。
不预先增加 Owner、时间和过期等多套索引，不做通用 Repository 框架。

Store 的集合访问需要保护，不能把可变 Dictionary 或延迟枚举器直接返回给调用方。
单条更新应在存储边界原子完成；不能认为 ConcurrentDictionary 会自动保证多步骤业务一致性。
锁只覆盖短小的内存操作，不覆盖外部调用，也不在普通 lock 中执行 await。
内部更新接口接收 MemoryId、完整 MemoryUpdateRequest 和候选 UpdatedAt；
Store 在同一原子操作中保留不可修改字段，时间取已存 UpdatedAt 与候选时间的较晚者。
同条并发变更按实际应用顺序后者覆盖，不新增 Version，不宣称具有乐观冲突检测。
不向公共契约暴露锁或任意更新委托。

端口约定：AddAsync 遇重复 ID 抛 InvalidOperationException；GetAsync 对未知 ID 返回 null；
UpdateAsync、DeleteAsync 对未知 ID 抛 KeyNotFoundException。
ListByOwnerAsync 只返回 Owner 完全一致的候选快照，包括不可读状态与已到期记录，
不做业务过滤、排序或 Limit。Manager 负责这些规则。
内存实现的每个实例有独立数据；后续组合应为同一记忆模块共享 Store 实例，而不是每次调用新建。
内存操作同步完成并返回已完成的 Task，不用 Task.Run 伪造异步 I/O。

### 4.2 时间与取消

通过 TimeProvider 取得时间，测试用可控时钟覆盖到期和更新时间边界。
不要使用 Thread.Sleep 等待到期，也不要使用本地时间字符串排序。
已提供 [ManualTimeProvider](../HanyingAgent/TEST/HanyingAgent.Tests/Support/ManualTimeProvider.cs)，
支持固定 UTC 时间、精确推进与显式回退；仅控制 GetUtcNow，不虚拟化计时器或 Task.Delay。
每个测试独立创建时钟，管理器已通过构造注入 TimeProvider，生产入口组合时使用 TimeProvider.System。
查询等待 Store 快照后取得一次 now，对所有候选使用同一时刻；写入的创建与更新时间也使用同一次时钟读取。
更新仅将候选时间传给 Store，由存储原子保护 UpdatedAt 不倒退，不在 Manager 中做读后覆盖。

CancellationToken 控制本次调用或等待，不保证撤销已经保存的变更。
WriteAsync 目前没有幂等键，超时后不能把重新写入当作无条件安全重试。
第一版不增加自动重试来掩盖不确定结果。

## 5. 本期不做的功能

| 功能 | 暂不实现的原因 |
|---|---|
| 自动记忆提取 | 需要模型、提示设计和质量评估，先由调用方明确提供内容。 |
| 语义去重、冲突整合、自动纠错 | 属于记忆管理策略，不是最小读写闭环。 |
| 长期记忆、Embedding、向量或全文搜索 | 属于另一套读取能力，避免将 ListRecentAsync 变成搜索接口。 |
| SQLite、备份、崩溃恢复 | 已选择先用进程内存，重启清空是明确限制。 |
| 后台过期清理、自动容量淘汰 | 查询先正确过滤，后续单独设计资源管理。 |
| 多进程、分布式一致性、高可用 | 超出单进程学习目标。 |
| 认证、角色、多租户、外部身份绑定 | 不伪装成已经实现；将来在可信边界或明确授权依赖处接入。 |
| 完整审计与隐私删除链路 | 从本存储遗忘不等于擦除日志、Checkpoint 或其他历史副本。 |

Limit 只限制一次读取数量，不限制存储总量。MVP 不自动淘汰不等于内存无限；
仅适用于少量可控输入，长期运行或对外开放前必须补充容量与访问策略。

## 6. 测试与验收标准

已选择 xUnit，使用 SDK 模板提供的 VSTest 测试项目，不额外引入 Mock 库。
测试不复制生产源码，不依赖真实时间等待或付费外部服务。
当前已加入 ID 校验与 JSON、Owner 结构与值相等、可控时钟的基础测试；
并增加真实 Store 的独立测试，覆盖新增、获取、原子更新、删除、归属快照、预取消、基础输入错误和基本并发访问。
Manager 单元测试已通过手写 Store 替身覆盖输入校验、操作委托、状态、有效期、Type、排序、Limit、
异常原样传播和取消。
第四步新增 [WorkingMemoryBoundaryTests](../HanyingAgent/TEST/HanyingAgent.Tests/WorkingMemory/WorkingMemoryBoundaryTests.cs)，
以真实 Manager、内存 Store 和可控时钟覆盖 50 个跨层用例，包括写后可见性、精确归属、
25 种状态转换、到期相等、更新时间回退保护、排序与 Limit、快照、未知目标、取消以及并发变更。
第五步已统一核对验收范围并运行全部现有正式测试；组合验收不等同于 Agent 已经接入。

| 层次 | 必须覆盖 |
|---|---|
| Manager 单元测试 | 输入校验、Owner 精确匹配、Type 和状态过滤、有效期、排序、Limit、错误传播。 |
| Store 测试 | 新增、读取、原子更新、删除、并发访问、返回快照不暴露底层集合。 |
| 组合测试 | 真实 Manager 与内存 Store 组合，验证写后、更新后和遗忘后的可见性。 |

重点边界：

1. Active / Confirmed 可读，其他状态不返回。
2. 无有效期、未来到期、恰好到期和已过期。
3. 同一更新时间按 ID 稳定排序，Limit 正确生效。
4. 四种 Scope 精确隔离，不自动跨归属读取。
5. 更新保留不可修改字段，清除有效期遵守完整替换语义。
6. 未知目标和非法输入明确报错，不产生半成品记录。
7. 并发写入不丢失不同 ID 的记录，不导致集合读取异常。
8. 存储失败不返回成功；取消检查与已提交变更的不确定性明确。

完成本期的标准是上述规则有正式、可重复运行的行为测试，
而不只是 DTO 能序列化或项目能编译。后续报告应区分单元测试、组合测试和未实现能力。

### 6.1 第五步统一验收结果

验收日期：2026-11-09。环境：.NET SDK 10.0.403、net10.0、xUnit v2 / VSTest。
在 HanyingAgent 项目根目录执行：

```powershell
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --no-restore --nologo --verbosity minimal
dotnet build .\HanyingAgent.slnx --no-restore --nologo -warnaserror
dotnet run --project .\HanyingAgent.csproj --no-build
```

`--no-restore` 适用于已经还原依赖的工作区；新环境首次运行需先还原，或去掉该参数。

| 测试组 | 用例数 | 范围 |
|---|---:|---|
| MemoryIdTests | 10 | ID 基础与 JSON 往返。 |
| MemoryOwnerTests | 31 | 归属结构、值相等与 JSON 往返。 |
| ManualTimeProviderTests | 6 | 测试时钟本身。 |
| InMemoryWorkingMemoryStoreTests | 46 | 真实存储行为。 |
| LocalWorkingMemoryManagerTests | 109 | 使用 Store 替身的 Manager 单元测试。 |
| WorkingMemoryBoundaryTests | 50 | 真实 Manager + Store 的组合边界与并发。 |
| 合计 | 252 | 统一运行全部通过，失败 0，跳过 0。 |

解决方案构建为 0 警告、0 错误；入口仍只输出 HanyingAgent，没有为验收改动启动行为。
实际应用编译项共 67 项，包含 Manager 与 Store，排除 TEST 子树。
本次没有增加业务功能、修改公共契约、安装新依赖或接入 Agent。

### 6.2 MVP 要求与测试证据

| 要求 | 已核对的行为与代表性测试 |
|---|---|
| 基本读写与写后可见 | [组合测试](../HanyingAgent/TEST/HanyingAgent.Tests/WorkingMemory/WorkingMemoryBoundaryTests.cs) 的 Mutations_AreVisibleThroughManagerAndPersistTheReturnedRecord 验证写入、更新、遗忘后通过公共接口读取及真实存储结果。 |
| 归属过滤 | 同文件的 OwnerReads_IsolateExactScopeAndIdsWithoutIncludingRelatedScopes 验证四种 Scope、相邻归属与 ID 大小写，不把结构隔离当作授权。 |
| 状态与有效期 | 同文件的 StatusTransitions_AcceptAllDefinedPairsAndControlVisibility、ExpirationBoundary_ChangesVisibilityWithoutChangingStoredRecord、Reactivation_DoesNotBypassExpirationAndNullUpdateCanClearIt 覆盖全部 25 种转换、恰好到期、重新激活与清除有效期。 |
| 排序与数量 | 同文件的 FilteringAndOrdering_HappenBeforeDefaultAndExplicitLimits，加上 [Manager 测试](../HanyingAgent/TEST/HanyingAgent.Tests/WorkingMemory/LocalWorkingMemoryManagerTests.cs) 的 ListRecentAsync_FiltersThenSortsOrdinallyThenLimits，验证先过滤、再按更新时间与 Ordinal ID 排序、最后取 Limit。 |
| 原子变更与并发保护 | [Store 测试](../HanyingAgent/TEST/HanyingAgent.Tests/WorkingMemory/InMemoryWorkingMemoryStoreTests.cs) 的并发新增、重复 ID、更新和删除，以及组合测试的 ConcurrentUpdatesAndQueries_ExposeWholePayloadsAndMonotonicUpdatedAt、ConcurrentForgetAndUpdate_DoNotResurrectDeletedRecord。 |
| 时间不倒退与快照 | 组合测试的 ClockRollback_LastAppliedContentWinsButUpdatedAtDoesNotRegress、ReturnedSnapshots_DoNotChangeAfterUpdatesAddsOrDeletes 验证后应用覆盖和旧结果稳定。 |
| 明确错误与取消 | Manager 测试的 Operations_PropagateStoreFailureWithoutRetry，以及组合测试的 InvalidUpdate_DoesNotPartiallyChangeStoredRecord、UnknownAndDeletedTargets_FailWithoutRecreatingRecords、PreCancellation_LeavesTheRealStoreUnchanged、CancellationBetweenValidationAndStorage_PreventsMutation。 |
| 独立、可重复测试 | [正式测试项目](../HanyingAgent/TEST/HanyingAgent.Tests/HanyingAgent.Tests.md) 通过 ProjectReference 引用应用，测试使用独立状态与可控时钟，不依赖 Agent、LLM、数据库、MCP 或真实时间等待。 |

结论：本期“基本读写、归属过滤、有效期、排序、并发保护和独立测试”已达到学习版 MVP 要求。
这不表示所有线程调度、生产负载或远程失败模式均已验证。

### 6.3 可调用方式与验收边界

使用示例见 [Memory 模块说明](../HanyingAgent/SRC/Capabilities/Memory/Memory.md)。
后续调用方通过 IWorkingMemoryManager 使用能力，入口负责组合 Manager、共享的 Store 实例与 TimeProvider.System；
不需要由 Agent 直接操作 Dictionary 或依赖具体 Store。

- 数据只在 Store 实例内存在，丢弃实例或进程退出即丢失；不提供 SQLite、重启恢复或远程服务。
- 原子性针对单次 Store 操作，不提供多次业务调用之间的事务或分布式一致性。
- 同条更新后应用覆盖，没有 Version 冲突检查、写入幂等键或自动重试。
- 取消不保证中断普通 lock 等待，不回滚已保存的数据；远程化需要额外定义超时与不确定结果处理。
- 查询过滤到期项，但没有后台过期清理、容量上限或淘汰；Limit 不是存储总容量。
- Owner 结构合法不代表用户真实存在、Task 属于 Session 或调用者已获授权。
- 内容、来源、分值与到期时间由可信调用方提供，没有自动提取、可信度校准或重要性计算。
- Context、Agent 执行流程和真实 LLM 调用不在本次验收范围；长期记忆同样未实现。

## 7. 后续优化与扩展

按实际需要推进，不把下表视为 MVP 的前置条件。

| 阶段 | 方向 | 何时需要 |
|---|---|---|
| 接入 Agent | Context 按需读取；Executor 或明确业务流程写入。 | 最小记忆闭环测试通过后。 |
| 持久化 | SQLite 适配、恢复校验和必要迁移。 | 需要重启保留数据时。 |
| 资源管理 | 容量上限、显式拒绝策略、过期清理、按归属配额和内容大小限制。 | 开始长期运行或接收不可控输入前。 |
| 查询优化 | Owner 索引、数据库排序与限制、性能测量。 | 数据量增加且测量发现瓶颈时。 |
| 变更语义 | 条件更新、版本检查、写入幂等、明确冲突策略。 | 出现多写入方、网络重试或远程部署时。 |
| 权限与可观测性 | 可信身份与归属验证、必要审计、指标和敏感内容脱敏。 | 跨用户、对外服务或接近生产使用前。 |
| 记忆整合 | 提取、压缩、语义去重、来源追踪和冲突处理。 | 有真实使用案例和可评估质量标准后。 |
| 长期记忆 | 单独实现 ILongTermMemoryManager，复用成熟搜索与 Embedding 能力。 | 近期直接读取已不能满足召回需求时。 |

长期记忆不是修改现有记录的 Scope；需要提取、整理并创建带来源的新记忆。
索引属于派生数据，不能代替权威记录。数据库、搜索引擎和分布式能力优先复用成熟实现。
远程化需要明确超时、幂等和一致性，不只是把本地方法替换成 HTTP。

## 8. 已确认的 MVP 策略

以下策略已经确认，后续实现和行为测试必须遵守：

| 项目 | 已确认内容 |
|---|---|
| 遗忘方式 | 物理删除，不等同于 Expired；未知目标显式报错。 |
| 初始状态 | 新写入为 Active，不把它误认为内容已经证实。 |
| 已过期有效期输入 | 写入与更新均接受过去或当前到期时间；读取时过滤，恰好到期也不可读。 |
| 状态更新 | 全部已定义状态之间可切换，包括重新激活；有效期仍独立检查。 |
| 同条并发更新 | 原子覆盖，后应用为准，UpdatedAt 不倒退；不新增 Version 或乐观冲突检测。 |
| 测试框架 | xUnit，不额外引入 Mock 库。 |
| 第一版存储 | 进程内存、重启清空；保留后续 SQLite 适配的端口。 |

这些是本期实现约定，不因此扩展成完整记忆平台，也不改变现有公共 Memory 契约。

## 9. 分步实施与当前进度

| 步骤 | 内容 | 当前状态 |
|---|---|---|
| 第一步：规则与测试基础 | 确认策略；建立正式测试项目、内部存储端口、测试时钟与契约基础测试。 | 已完成；该步只建立基础，不包含具体 Store 或 Manager。 |
| 第二步：内存 Store | 实现进程内存存储及新增、获取、原子更新、删除和快照测试。 | 已实现，并提供预取消和基本并发测试。 |
| 第三步：Manager | 实现参数校验、初始状态、有效期、排序、Limit 与时钟注入，并编写单元测试。 | 已实现，使用手写 Store 替身独立测试。 |
| 第四步：边界与并发 | 补齐到期相等、时间回退、同 ID 原子覆盖、取消和快照隔离等行为测试。 | 已增加真实 Manager + Store 的 50 个跨层用例，并覆盖基本并发竞争。 |
| 第五步：组合验收 | 统一核对 MVP 验收范围，运行相关正式测试并确认模块状态与限制。 | 已完成；252 个测试统一通过，构建 0 警告、0 错误，验收证据与限制见第 6 节。 |

基础、Store、Manager 和跨层边界测试运行命令见 [测试项目说明](../HanyingAgent/TEST/HanyingAgent.Tests/HanyingAgent.Tests.md)。
五步现已完成，后续可单独设计 Context 和任务执行接入；不将记忆模块可用等同于完整 Agent 已可运行。

本期成功的标志：得到一个边界清晰、行为可测、可以被 Agent 使用的工作记忆模块，
而不是宣称已经解决生产级记忆系统的全部问题。

## 10. 相关文档

- [模块职责与运行流程](<./HanyingAgent 模块职责与运行流程.md>)。
- [Memory 契约](../HanyingAgent/SRC/Contracts/Memory/Memory.md)。
- [Memory 操作约定](../HanyingAgent/SRC/Contracts/Memory/Operations/Operations.md)。
- [本地模块与远程 Service 演进设计](<./Agent Runtime 本地模块与远程 Service 演进设计.md>)。

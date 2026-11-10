# Models

## 功能与定位

定义与具体访问操作无关的记忆数据模型；即使作为参数或返回值使用，它们仍属于记忆本身。

当前无子目录，包含以下类型：

| 类型 | 功能 |
|---|---|
| [MemoryItem](./MemoryItem.cs) | 表达一条记忆的 ID、归属、分类、内容、来源、分值、状态与时间。 |
| [MemoryId](./MemoryId.cs) | 稳定标识单条记忆，用于引用和精确操作。 |
| [MemoryOwner](./MemoryOwner.cs) | 将 Scope 与具体 SessionId、TaskId 或 UserId 组合成不可变归属。 |
| [MemoryScope](./MemoryScope.cs) | 区分 Task、Session、User、Global 四种归属范围。 |
| [MemoryType](./MemoryType.cs) | 区分 Fact、Preference、Decision、Experience、Hypothesis、Summary 六类主要内容。 |
| [MemoryStatus](./MemoryStatus.cs) | 表达 Active、Confirmed、Superseded、Contradicted、Expired 等当前状态。 |

## 已有校验

MemoryId 的显式构造拒绝空白；MemoryOwner 拒绝未知 Scope、默认或无效 ID，以及不符合归属形状的 ID 组合：

- Task 只携带 SessionId 与 TaskId。
- Session 只携带 SessionId。
- User 只携带 UserId。
- Global 不携带归属 ID。

这些校验不证明 Task 实际属于 Session，也不构成访问授权；MemoryItem 的其他字段目前只是数据，不自动执行完整业务校验。

## 字段语义

Confidence 是可信度估计，Importance 是重要性，两者都不是某次查询的相关性分数。
Type 不是严格互斥的完整知识分类；Confirmed 表示确认程度，不能将 Active 理解为已经证实。
ExpiresAt 到期时即应视为失效，不依赖后台先把状态改为 Expired；这些读取规则由后续 Manager 实现。

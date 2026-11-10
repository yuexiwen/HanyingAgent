# Models

## 功能与定位

定义可序列化的会话数据，使会话身份和生命周期不依赖本地执行资源。

当前无子目录，包含以下类型：

| 类型 | 功能 |
|---|---|
| [SessionId](./SessionId.cs) | 以只读 record struct 包装字符串标识，显式构造拒绝空白并支持 JSON 反序列化。 |
| [SessionStatus](./SessionStatus.cs) | 只描述 Idle、Active、Closed 三种会话生命周期状态。 |
| [SessionInfo](./SessionInfo.cs) | 表达会话 ID、所属 UserId、显示名称、状态、当前任务关联和时间。 |

## 数据约定

- Name 是非空白显示名称，不是唯一键，允许不同 Session 重名。
- UserId 为必需归属，创建后不变，不保存完整用户资料或自动跟踪用户显示名称。
- Idle / Closed 的 CurrentTaskId 必须为 null；Active 必须关联有效 TaskId。
- CreatedAt 创建后不变，LastActiveAt 不早于 CreatedAt，随输入或生命周期活动更新且不倒退。
- 查询和列举本身不改变最后活动时间；重复关闭已关闭会话不改变快照。
- Task 的 Running、WaitingForTool、WaitingForUser 等细节不复制成 Session 状态。

## 校验边界

SessionId 的显式构造已有校验，但默认值仍可能无效。
SessionInfo 通过 JsonRequired 拒绝缺少 UserId 的 JSON，但不自动验证 default UserId、用户存在性、状态与任务关联或时间关系；后续管理与执行入口必须维护这些约束。
恢复、远程输入和存储反序列化同样需要业务校验，知道 SessionId 不等于拥有访问权。

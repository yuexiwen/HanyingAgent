# Operations

## 功能与定位

定义会话生命周期管理协议，当前只有接口与请求类型，没有具体执行或存储实现。

## 能力接口

[ISessionManager](./ISessionManager.cs) 提供以下操作：

| 操作 | 行为约定 |
|---|---|
| CreateAsync | 接受所属 UserId 和显示名称，校验用户与创建权限，生成新 SessionId 和时间，返回 Idle 会话。 |
| GetAsync | 返回指定会话的当前快照，不存在时显式返回可空结果 null，Closed 会话仍可查询。 |
| ListAsync | 返回全部可见会话的快照，包括 Closed；第一版不分页、不附带 Task 详细进度。 |
| CloseAsync | 关闭 Idle 会话并返回 Closed 快照，已关闭时原样返回，Active 或不存在时显式报错。 |

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Requests](./Requests/Requests.md) | 定义创建会话所需的所属用户和显示名称。 |

## 设计边界

- 所有方法异步接收 CancellationToken，具体取消传播由后续实现兑现。
- 非法默认 ID、空请求或空白名称报输入错误；未知关闭目标报不存在错误；关闭占用会话报冲突错误。
- 创建时未知所属用户报不存在错误，权限不足显式报错，不自动创建用户；后续实现应通过用户模块验证，不依赖其存储细节。
- 所有读取与关闭同样验证访问权限，知道 SessionId 或 UserId 不等于拥有权限。
- List 按 LastActiveAt 降序、Id.Value 的 Ordinal 升序打破平局，结果是读取时快照，不是实时可变列表。
- Close 不删除会话、任务、记忆或历史；Get 和 List 不产生会话活动。
- 实现需协调任务接收与 Close，避免关闭后仍启动新任务；模型、工具和用户等待都不释放当前任务占用。
- 任务提交、等待输入和取消属于 [ITaskManager](../../Tasks/Operations/ITaskManager.cs)，事件订阅仍留待后续，不属于本接口。
- 直接返回 SessionInfo，无需单独响应包装，当前不建立空 Responses 目录。

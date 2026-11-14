# Orchestration

## 功能与定位

协调 Session 与 Agent Task 的生命周期，为 Host 提供运行入口，并在任务启动、等待、完成、取消和失败时组织其他模块协作。

当前已实现可信本地单用户的内存 Direct 任务闭环，不包含工具循环、等待用户、排队或恢复。

## 当前组件

| 组件 | 一句话概述 |
|---|---|
| [LocalTaskManager](./LocalTaskManager.cs) | 实现 ITaskManager 和 IHostedService，管理提交、后台执行、查询、取消与退出收尾。 |
| [ITaskStore](./ITaskStore.cs) | 内部原子存储端口，协调任务状态、结果和 Session 占用。 |
| [TaskExecutionOptions](./TaskExecutionOptions.cs) | 接收入口配置的不可变执行参数，不依赖 Host 配置类型。 |
| 本地执行句柄 | 保存运行中的 Task、独立取消源与取消回调；收尾后移除并释放资源，不序列化。 |

## 提交与执行

Host 启动任务服务后才能接受提交；提交校验可信身份、输入与 Session，原子创建 Created 快照并设置
Session.Active / CurrentTaskId。立即返回接受快照，后台通过独立 DI Scope 调用现有 DialogueCoordinator。
同会话忙时拒绝、不排队；不同会话可以并行。消息携带 TaskId，历史选择及模型协议仍由原模块实现。

```text
Created → Running → WaitingForModel → Completed / Failed
    \          \           \
                  Cancelling → Cancelled
```

WaitingForModel 表示进入 Direct 模型执行阶段（包括最终 Context 构建），不是精确的网络请求时间。
状态变更递增 Version，时间不因时钟回拨而倒退。没有第二套 State 状态机。

| 执行结果 | 任务结果 |
|---|---|
| Delivered 且 Assistant 保存成功 | Completed，保存最终文本。 |
| 截断、未知、拒绝、过滤、不支持工具 | Failed，保留返回文本并给出明确原因；工具动作不伪造普通 Assistant 历史。 |
| 模型、Context 或历史保存异常 | Failed，不自动重新调用模型。 |
| 任务取消导致执行实际停止 | Cancelled，不把依赖超时误认为业务取消。 |

完成结果、任务终态和 Session 释放在 Store 的同一短锁内提交。迟到的完成或取消不能覆盖已有终态，
也不能释放后续任务占用。Delivered 不保证事实正确，只表示本策略正常交付。

## 查询与取消

- Get 不存在返回 null；GetResult 未结束返回 null、不存在则报错；所有访问校验所属 Session 的用户。
- ListBySession 默认 20 条，支持 1–200、状态过滤及向更早任务翻页。按内部提交顺序倒序，
  游标绑定 Session 与状态过滤；新提交和时钟回拨不会改变旧页的位置，状态过滤不是冻结快照。
- Cancel 返回取消请求快照，不等待业务停止。重复取消不重复增加 Version，终态取消返回原快照。
- WaitForResultAsync 是本地入口辅助方法；取消等待只取消该等待，不取消业务任务。
- 提交调用的 CancellationToken 只控制接受过程；任务接受后使用独立取消源。
- ProvideInput 明确抛出 NotSupportedException；本策略不会进入 WaitingForUser。
- 提交协议没有 RequestId，忙时重复提交会被拒绝；完成后的相同输入是新任务，不提供跨重试提交幂等。

## 退出和错误边界

Stop 停止接受、请求取消所有运行任务并等待实际收尾。超出 Host 退出期限则显式失败，
不能把仍运行的执行伪装成 Cancelled 或提前释放 Session。正常退出依赖执行组件遵守取消协议。
后台边界记录未预期异常并保存 Failed；最终状态保存失败记录 Critical 并传播，不假装完成。
执行句柄一直跟踪到取消回调与资源清理结束。没有自动重试、容量治理或跨进程恢复。

## 设计边界

会话公共信息与本地执行资源分离；取消一次调用等待不等于任务已取消。
不在此实现 Context 排序、工具参数执行或模型 Provider 解析。
Task 提交先返回接受快照，执行在后台继续；历史通过 Task.SessionId 查询，不在会话快照中重复维护任务集合。
SessionInfo 保存所属 UserId，当前通过 LocalUserContext 验证归属；不提供远程登录或多租户授权。
没有为预期的多个策略建立抽象基类、工厂或额外 Orchestrator；后续 MCP 工具适配不应进入任务 Store。

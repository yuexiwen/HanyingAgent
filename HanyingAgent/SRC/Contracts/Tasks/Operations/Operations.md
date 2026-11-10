# Operations

## 功能与定位

定义任务生命周期的公共调用入口，采用先接受、后查询的异步执行模型；当前没有管理器实现。

## 能力接口

[ITaskManager](./ITaskManager.cs) 提供以下操作：

| 操作 | 行为约定 |
|---|---|
| SubmitAsync | 在有效且空闲的 Session 中接受新工作，返回 TaskInfo，不等待执行完成。 |
| GetAsync | 返回任务快照，不存在时返回可空结果 null。 |
| ListBySessionAsync | 按 SessionId 和可选状态分页读取任务历史，包括终态任务。 |
| GetResultAsync | 已知任务未结束时返回 null，终态时返回 TaskResult，未知任务显式报不存在错误。 |
| ProvideInputAsync | 只向 WaitingForUser 且版本匹配的任务提供补充，恢复原任务并返回最新快照。 |
| CancelAsync | 请求取消指定任务，返回请求后的快照，但不承诺该任务已经停止。 |

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Requests](./Requests/Requests.md) | 定义新任务输入、等待回答和按会话分页查询的参数。 |
| [Responses](./Responses/Responses.md) | 定义任务历史页与独立终态结果。 |

## 接受与恢复

- Submit 必须原子协调新任务创建与 Session 占用；未知、Closed 或 Active 会话明确报错，不产生新任务。
- 接受后至少存在可查询的任务记录，Session.CurrentTaskId 关联该任务；返回快照时执行可能已经推进。
- ProvideInput 不创建新任务、不改变最初 Input；补充内容进入该任务对应的对话记录。
- 版本过期、状态不匹配、空白输入或未找到任务都显式报错，不自动改成新任务或下一轮回答。
- 暂不支持执行中的 steering，也不提供任务排队。

## 取消与结果

- Cancel 对终态任务原样返回，对已请求取消且未变化的任务可幂等返回。
- 非终态取消通常进入 Cancelling，也可直接结束；后续通过查询确认最终状态。
- 任务进入终态时应保存结果并释放 Session 占用；取消请求本身不提前将 Session 改成 Idle。
- 方法的 CancellationToken 控制本次调用或等待，不等价于 CancelAsync，也不保证撤销已接受的新任务。
- 提交目前没有幂等键；调用超时后的结果可能不确定，不能盲目重新提交。
- 所有读取与控制均需要验证访问权限；这些行为约定仍待实现。

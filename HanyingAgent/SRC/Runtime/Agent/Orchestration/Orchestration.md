# Orchestration

## 功能与定位

负责一轮对话流程的编排，将 Conversation、Context 和 Execution 的能力组织为完整调用链。
任务生命周期由独立的 [Tasks](../../Management/Tasks/Tasks.md) 模块管理，不在本模块维护第二套任务状态机。

## 当前组件

| 组件 | 职责 |
|---|---|
| [DialogueOrchestrator](./DialogueOrchestrator.cs) | 编排公共准备与收尾：输入保存、历史和记忆读取、执行调用及回复保存。 |
| [DialogueRequest](./DialogueRequest.cs) | 承载本轮输入及执行参数，不依赖 Console 或 Host 配置类型。 |

## 一轮对话流程

Tasks 将不可变执行配置映射为 DialogueRequest，并在独立 DI Scope 中调用对话协调器。
Conversation 提供历史读写，Context 负责模型历史选择和上下文组装，Execution 负责执行策略。

```text
Tasks 已持有任务占用（独立 RespondAsync 入口使用 Session.Active lease）
  → 保存本轮 User，取得 Sequence
  → Context/ConversationWindow 读取该输入之前的候选完整历史，并只加入一次当前输入
  → 通过 LocalUserContext 解析可信用户
  → 调用 IWorkingMemoryManager，读取当前 User 最近最多 20 条有效记忆并校验返回归属
  → 将候选消息、记忆、模型选项和字符保护上限作为 BuildContextRequest 交给 DirectExecutor
      → 调用 IContextBuilder，统一预算选择后取得 ModelInvocation
      → 调用阶段回调，由 Tasks 更新 WaitingForModel 状态
      → 调用模型并解释响应
  → 保存实际 Assistant 内容和结束原因
  → 返回 ExecutionOutcome，由 Tasks 保存任务终态并释放会话
```

`/remember` 显式写入的 User 级记忆可跨会话读取；编排器负责协调 Memory 读取，
Builder 负责数据校验、去重排序、模型预算与份额选择、参考数据格式及最终校验，不依赖 Memory 服务。
当前仍为进程内存存储，无自动提取或语义检索。
可选上下文默认按 History 75% / Memory 25% 分配，可借用闲置额度；不会因历史增长而占用记忆保留份额。
Token 计数当前为明确标记的估算。必需的系统指令和当前输入本身超限才因预算失败，不截断输入。

编排阶段顺序为 SaveUser、SelectHistory、ReadMemory、Execute、SaveAssistant。
任一数据准备阶段失败或观察到取消，不继续调用模型；异常原样向上传播。
WaitingForModel 由执行器在上下文构造成功后、调用模型前通知，不覆盖记忆读取和 Context 构造阶段。

`RespondCoreAsync` 保留为单次对话的流程骨架，阶段细节封装为同类内的 private 方法：
`SaveUserAsync`、`SelectHistoryAsync`、`ReadMemoryAsync`、`SaveAssistantAsync`；
Context 构造和模型执行通过 `executor.ExecuteAsync` 完成。
这些方法不构成新的模块层次，仍由编排器直接协调各能力。
会话 lease、日志 scope 和按 phase 过滤的异常处理保留在主流程，
确保它们覆盖完整调用，不因辅助方法返回而提前释放。
当前没有抽象基类或 virtual 扩展点，不采用继承式模板方法模式。

完整和非正常文本响应均保存用于展示，截断 / 未知 / 拒绝 / 过滤不会进入下一轮窗口。
工具动作当前无法在纯文本记录中如实表达，UnsupportedAction 明确记录事件但不保存为普通 Assistant 历史。
连接失败或取消保留已提交的 User，不伪造 Assistant；后续窗口跳过这些未回答的旧输入。
回复保存失败明确报错，标注 SaveAssistant 阶段，不重复调用模型、不伪装成保存成功。
本期无自动重试或恢复接口；消息幂等不等于整个对话或模型调用幂等，用户重新提问是新一轮输入。
异常和取消不回滚已提交记录，也不构成完整事务。

## 设计边界

- 不创建、查询或持久化业务任务状态，不管理后台执行句柄；通过阶段回调通知 Tasks。
- 不依赖 Tasks 的具体实现；任务路径接收 TaskId 和取消令牌，消息关联同一 TaskId。
- 不承担历史或记忆存储实现、Context 排序算法或模型 Provider 解析。
- Conversation、Memory 和 Execution 由同一编排层显式协调，Execution 调用 Context；
  各模块内部仍封装自己的 Store 或模型适配，不把所有底层访问展开到编排器。
- 独立对话入口使用 Session lease，任务入口复用已有占用，不重复占用会话。
- SessionId Scope 覆盖历史选择与执行，DirectExecutor 的 ExecutionId 嵌套其中；
  任务路径继承 Tasks 的 TaskId Scope。
- Context 构造和模型调用均在 DirectExecutor 的 ExecutionId 范围内，同时继承外层 SessionId / TaskId。
- 执行器已有的错误不重复记录；历史读写、记忆读取和会话访问失败由编排边界记录并抛出。

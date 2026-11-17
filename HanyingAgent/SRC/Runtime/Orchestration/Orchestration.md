# Orchestration

## 功能与定位

负责一轮对话流程的编排，将 Conversation、Context 和 Execution 的能力组织为完整调用链。
任务生命周期由独立的 [Tasks](../Tasks/Tasks.md) 模块管理，不在本模块维护第二套任务状态机。

## 当前组件

| 组件 | 职责 |
|---|---|
| [DialogueOrchestrator](./DialogueOrchestrator.cs) | 编排输入保存、历史选择、执行和回复保存。 |
| [DialogueRequest](./DialogueRequest.cs) | 承载本轮输入及执行参数，不依赖 Console 或 Host 配置类型。 |

## 一轮对话流程

Tasks 将不可变执行配置映射为 DialogueRequest，并在独立 DI Scope 中调用对话协调器。
Conversation 提供历史读写，Context 负责模型历史选择和上下文组装，Execution 负责执行策略。

```text
Tasks 已持有任务占用（独立 RespondAsync 入口使用 Session.Active lease）
  → 保存本轮 User，取得 Sequence
  → Context/ConversationWindow 选择该输入之前的历史，并只加入一次当前输入
  → 调用阶段回调，由 Tasks 更新 WaitingForModel 状态
  → DirectExecutor
  → 保存实际 Assistant 内容和结束原因
  → 返回 ExecutionOutcome，由 Tasks 保存任务终态并释放会话
```

完整和非正常文本响应均保存用于展示，截断 / 未知 / 拒绝 / 过滤不会进入下一轮窗口。
工具动作当前无法在纯文本记录中如实表达，UnsupportedAction 明确记录事件但不保存为普通 Assistant 历史。
连接失败或取消保留已提交的 User，不伪造 Assistant；后续窗口跳过这些未回答的旧输入。
回复保存失败明确报错，标注 SaveAssistant 阶段，不重复调用模型、不伪装成保存成功。
本期无自动重试或恢复接口；消息幂等不等于整个对话或模型调用幂等，用户重新提问是新一轮输入。
异常和取消不回滚已提交记录，也不构成完整事务。

## 设计边界

- 不创建、查询或持久化业务任务状态，不管理后台执行句柄；通过阶段回调通知 Tasks。
- 不依赖 Tasks 的具体实现；任务路径接收 TaskId 和取消令牌，消息关联同一 TaskId。
- 不承担历史存储实现、Context 排序算法或模型 Provider 解析。
- 独立对话入口使用 Session lease，任务入口复用已有占用，不重复占用会话。
- SessionId Scope 覆盖历史选择与执行，DirectExecutor 的 ExecutionId 嵌套其中；
  任务路径继承 Tasks 的 TaskId Scope。
- 执行器已有的错误不重复记录；历史读写和会话访问失败由对话协调边界显式记录并抛出。

# Execution

## 功能与定位

定义执行策略交给调用方的结果，不代替模型响应或业务任务状态。

- [ExecutionConclusion](./Models/ExecutionConclusion.cs) 表达交付结论。
- [ExecutionOutcome](./Models/ExecutionOutcome.cs) 包含结论、输出文本，以及诊断用途的模型结束原因、用量和 Provider 原因。
- Conclusion 与 FinishReason 是必需的 JSON 字段；文本与用量可空。
- 当前没有 IExecutor、抽象基类或独立执行请求；DirectExecutor 继续接受已有 BuildContextRequest。

## 当前 Direct 策略

| 模型返回 | 执行结论 |
|---|---|
| Stop、有非空白文本、无工具调用 | Delivered：正常交付，不保证事实正确。 |
| Length、无工具调用 | Incomplete：生成不完整，可带部分文本。 |
| Unknown、无工具调用 | Unconfirmed：无法确认正常结束。 |
| Refused、无工具调用 | Refused：明确拒绝，不根据正文猜测。 |
| ContentFiltered、无工具调用 | Filtered：明确过滤。 |
| 非空工具调用列表，或 ToolCalls 结束原因 | UnsupportedAction：Direct 不支持此动作，不执行工具。 |

合法响应中的工具请求优先于其他结束原因，不能因 Stop 或存在文字而被当成正常交付。
空响应、空工具列表引用、非法结束原因枚举，以及无工具请求但 Stop 没有非空白正文，
均抛 InvalidOperationException，不转为成功形结果。
保留原始文本和诊断元数据；调用方根据 Conclusion 展示或决策，无须再次判断 FinishReason。
不复制工具动作到执行结果中，也不将文字说明当作工具已执行的证据。

## 异常与生命周期边界

- 模型正常返回后，执行器按策略返回结论；依赖失败、超时、主动取消仍通过异常显式传播，无自动重试。
- 调用方请求取消通过其 CancellationToken 判断；没有该取消信号的 OperationCanceledException
  代表超时或依赖取消，不能擅自认定业务任务已取消。
- 执行结果没有 TaskId、SessionId、版本或生命周期时间，不是任务的持久化记录或第二套状态机。
- 当前不支持等待用户、恢复、工具循环；不提前增加 WaitingForUser 执行结论或恢复游标。
- 未来 Orchestration 根据执行结论和任务政策更新已有 TaskInfo、TaskStatus 与终态 TaskResult。
  是否重试、接受部分交付或失败收尾，不由本契约写死。
- Delivered 不自动写入 TaskStatus.Completed；Refused / Filtered 等结论也不直接等同某个任务终态。

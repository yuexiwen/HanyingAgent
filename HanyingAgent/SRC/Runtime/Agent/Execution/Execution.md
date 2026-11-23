# Execution

## 功能与定位

负责选择和执行任务内部的控制流，以不同策略推进模型调用、工具动作和结果处理。

当前已实现 [DirectExecutor](./DirectExecutor.cs)，接入连续单轮 Console。

## 当前最小实现

构造注入 IContextBuilder、ILanguageModelRuntime 与 ILogger，ExecuteAsync 接受
BuildContextRequest（系统指令、候选消息和记忆、模型参数及字符上限）：

```text
创建 ExecutionId 日志 Scope
  → ContextBuilder.BuildAsync：按统一预算构造 ModelInvocation
  → 检查取消，调用可选 waitingForModel 回调，再检查取消
  → Model.InvokeAsync（仅一次）
  → 校验并解释 ModelResponse
  → 返回 ExecutionOutcome
```

取消信号传入模型调用，并在调用前后检查取消，避免依赖忽略取消时仍返回成功。
保留文本、FinishReason、Usage 与 ProviderFinishReason；调用方依据 Conclusion 处理，不再解析模型结束原因。
完成规则见[执行结果协议](../../../Contracts/Agent/Execution/Execution.md)：仅 Stop、有效文本且无工具请求时正常交付；
截断、未知、拒绝、过滤和不支持的动作分别表达，非法响应明确报错，不依据正文猜测拒绝或等待用户。
执行结论不是 Task 状态，Delivered 不证明答案正确，也不自动完成业务 Task。
已知调用异常在执行边界记录并重新抛出，主动取消与超时分别记录，无重试或成功形回退。
调用 ContextBuilder，但不实现其预算和组装算法；不读取记忆、不保存历史、不执行工具、不直接修改 Task 状态。
上下文构造失败或取消时不通知 WaitingForModel、不调用模型，异常原样传播；
执行边界记录构造及模型调用失败，编排器不重复记录。
没有预建策略工厂、IExecutor 或抽象基类。

## 后续组件规划

| 组件 | 一句话概述 |
|---|---|
| ExecutionEngine | 选择策略并将任务交给对应执行器。 |
| DirectExecutor | 已实现：接收 Context 构造材料，调用 Builder 后调用一次模型，解释并返回执行结论。 |
| ChainExecutor | 按预定义顺序执行多个步骤并传递中间结果。 |
| ReactExecutor | 在模型、工具与 Observation 之间循环，直到完成或达到停止条件。 |

## 设计边界

编排器负责公共准备与收尾，执行器拥有包括第一轮 Context 构造调用在内的完整策略。
当前 DirectExecutor 使用 Context 和 Model 能力，初始历史和记忆由编排器获取；
不直接拼装 Provider 请求或读写数据库。未来 ReAct 可在初始快照上追加工具记录并反复调用 Builder；
工具记录的保存责任需随工具协议一起落实，不在本次预建。
ReAct 必须约束迭代次数、工具调用数、超时与取消；带副作用工具不能因超时就盲目重试。

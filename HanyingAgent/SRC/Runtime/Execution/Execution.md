# Execution

## 功能与定位

负责选择和执行任务内部的控制流，以不同策略推进模型调用、工具动作和结果处理。

当前已实现 [DirectExecutor](./DirectExecutor.cs)，接入连续单轮 Console。

## 当前最小实现

构造注入 IContextBuilder、ILanguageModelRuntime 与 ILogger，ExecuteAsync 接受已有 BuildContextRequest：

```text
创建 ExecutionId 日志 Scope
  → Context.BuildAsync
  → Model.InvokeAsync（仅一次）
  → 校验并解释 ModelResponse
  → 返回 ExecutionOutcome
```

取消信号贯穿各层，两个异步阶段后也检查取消，避免依赖忽略取消时仍返回成功。
保留文本、FinishReason、Usage 与 ProviderFinishReason；调用方依据 Conclusion 处理，不再解析模型结束原因。
完成规则见[执行结果协议](../../Contracts/Execution/Execution.md)：仅 Stop、有效文本且无工具请求时正常交付；
截断、未知、拒绝、过滤和不支持的动作分别表达，非法响应明确报错，不依据正文猜测拒绝或等待用户。
执行结论不是 Task 状态，Delivered 不证明答案正确，也不自动完成业务 Task。
已知调用异常在执行边界记录并重新抛出，主动取消与超时分别记录，无重试或成功形回退。
不保存历史、不写记忆、不执行工具、不修改 Task 状态；没有预建策略工厂、IExecutor 或抽象基类。

## 后续组件规划

| 组件 | 一句话概述 |
|---|---|
| ExecutionEngine | 选择策略并将任务交给对应执行器。 |
| DirectExecutor | 已实现：构建 Context 后调用一次模型，解释并返回执行结论。 |
| ChainExecutor | 按预定义顺序执行多个步骤并传递中间结果。 |
| ReactExecutor | 在模型、工具与 Observation 之间循环，直到完成或达到停止条件。 |

## 设计边界

通过 Context、Model、Tool 和 State 的能力推进任务，不直接拼装 Provider 请求或读写数据库。
ReAct 必须约束迭代次数、工具调用数、超时与取消；带副作用工具不能因超时就盲目重试。

# Execution

## 功能与定位

负责选择和执行任务内部的控制流，以不同策略推进模型调用、工具动作和结果处理。

当前为目录骨架，没有子目录或业务实现。

## 后续组件规划

| 组件 | 一句话概述 |
|---|---|
| ExecutionEngine | 选择策略并将任务交给对应执行器。 |
| DirectExecutor | 构建 Context 后调用一次模型并生成回答。 |
| ChainExecutor | 按预定义顺序执行多个步骤并传递中间结果。 |
| ReactExecutor | 在模型、工具与 Observation 之间循环，直到完成或达到停止条件。 |

## 设计边界

通过 Context、Model、Tool 和 State 的能力推进任务，不直接拼装 Provider 请求或读写数据库。
ReAct 必须约束迭代次数、工具调用数、超时与取消；带副作用工具不能因超时就盲目重试。

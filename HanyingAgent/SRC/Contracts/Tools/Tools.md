# Tools

## 功能与定位

定义工具能力的公共描述、调用归属和结果协议，使 Runtime 不依赖具体工具实例或传输方式。

当前已定义契约，没有 ToolManager、Registry、具体工具或远程适配器实现。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Models](./Models/Models.md) | 定义工具调用身份、工具说明和明确区分未知结果的执行状态。 |
| [Operations](./Operations/Operations.md) | 定义按任务列举工具与等待调用结果的接口，以及请求和响应数据。 |

## 调用流程

```text
列举允许使用的 ToolDefinition
  → 模型生成 ToolCallRequest
  → Runtime 补充 SessionId、TaskId，构成 InvokeToolRequest
  → ToolManager 校验、执行并整理 ToolResult
  → ToolObservation 返回 Agent 执行循环
```

每次调用有独立 ToolCallId，同一 Task 可有多个调用；工具调用不是新建 Task。
InvokeAsync 等待本次调用结束或获得明确的未知结果，不提供提交后轮询协议。

## 设计边界

- 工具定义不是可执行实例，调用边界只传可序列化数据。
- 本地工具实例注册属于启动组合，不放入公共 IToolManager；业务协调在 Capabilities/Tools。
- HasSideEffects 是风险描述，不是权限或幂等性承诺；有副作用的调用不能因超时盲目重试。
- 方法 CancellationToken 取消调用或等待，不证明远端工具已停止，也不取消所属 Task。
- DTO 只定义数据；归属、权限、Schema、超时、调用去重和输出限制仍待实现。

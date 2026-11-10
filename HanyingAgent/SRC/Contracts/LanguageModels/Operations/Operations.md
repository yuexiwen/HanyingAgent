# Operations

## 功能与定位

定义一次非流式模型调用，不混入任务提交、工具执行或 Context 构建。

## 能力接口

[ILanguageModelRuntime](./ILanguageModelRuntime.cs) 只有 InvokeAsync：
接收 ModelInvocation 和 CancellationToken，等待本轮响应并返回非 null ModelResponse。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Requests](./Requests/Requests.md) | 定义本轮消息、可用工具与可选模型参数。 |
| [Responses](./Responses/Responses.md) | 定义文本、完整工具调用、结束原因和可空用量。 |

## 调用边界

- Context / Runtime 提供已组装的消息与工具定义，模型实现不自行查找 Session、User 或 Memory。
- 调用前校验消息协议、工具关联、选项与目标模型能力；非法输入或不支持的能力显式报错。
- 空 Tools 表示禁止提出工具调用，适配器需正确映射 Provider 行为；违反约束的输出不得当作有效调用执行。
- Provider 网络错误、超时、无效响应或取消等待显式传播，不用空文本响应或 Unknown 结束原因掩盖调用失败。
- CancellationToken 控制本次调用或等待，不等于取消所属 Task，也不证明远端生成或计费已经停止。
- Provider 响应完成并不自动进入 Task 终态；Runtime 依据结束原因和工具调用决定后续处理。

当前没有具体实现、认证配置、网络请求、自动重试或流式接口。

# Operations

## 功能与定位

定义工具发现与调用协议，公共调用等待本次执行结果，不包含本地注册或任务调度。

## 能力接口

[IToolManager](./IToolManager.cs) 提供：

| 操作 | 行为约定 |
|---|---|
| ListDefinitionsAsync | 返回所属任务允许使用的工具定义，再由 AllowedTools 缩小范围。 |
| InvokeAsync | 校验任务归属、权限和参数，执行一次工具调用，返回标准化 ToolObservation。 |

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Requests](./Requests/Requests.md) | 定义模型工具调用数据以及带 Session、Task 归属的发现和执行请求。 |
| [Responses](./Responses/Responses.md) | 区分工具原始结果与供 Agent 使用的摘要、错误和输出引用。 |

## 发现与执行约定

- 验证请求和 ID，Session、Task 必须存在且归属匹配，不能把请求中的 ID 当作访问授权。
- List 返回非 null 列表，按 Name 的 Ordinal 升序排序；没有可用工具时返回空列表。
- Invoke 重新检查执行权限和任务是否允许工具动作，不以先前列举结果作为永久授权。
- 未知工具、参数不符合 Schema、归属不匹配或权限不足显式报错，不自动换工具或返回成功。
- ToolCallId 在应用实例内关联一次调用，必须保持请求与 Observation 一致。
- 同一 Call ID 不同归属、工具或参数显式报冲突；重复同一请求只能复用已知结果或显式拒绝，不能盲目再次执行。
- 当前本地实现检查任务用户归属、Session 匹配与活动状态；已注册工具均允许活动任务调用。
  AllowedTools 只是本次发现过滤，不构成后续执行授权；更细的任务策略尚未实现。
- 本地 Manager 在实例生命周期内保留已开始的 Call ID，所有重复调用显式拒绝，不跨重启。
- 通用入口校验 JSON 对象，具体工具执行前校验自身 Schema；未引入通用 JSON Schema 引擎。

## 取消与未知结果

CancellationToken 控制本次调用或等待，允许显式抛出取消异常；它不等于 TaskManager.CancelAsync。
只有确认工具取消时才返回 Cancelled；传输超时或响应丢失且无法确认最终结果时返回 OutcomeUnknown，
或在无法返回 Observation 时显式抛出调用异常，不将未知结果包装成成功或确定失败。
Failed 与 Cancelled 均不承诺撤销副作用，调用方不可据此自动重试。

ToolResult 由本地工具实现返回；当前公开 InvokeAsync 返回 ToolObservation，
本地实现对 Content 与 Error 设置合计 8192 字符上限，超限显式失败，Artifact 支持仍待实现。

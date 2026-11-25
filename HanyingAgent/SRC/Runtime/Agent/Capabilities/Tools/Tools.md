# Tools

## 功能与定位

负责 Agent 实际可调用能力的业务管理，统一工具定义、参数校验、调用约束和执行结果。

当前已实现本地工具管理入口和 `get_current_time` UTC 时间工具，尚未接入模型工具调用循环。
公共调用协议已定义于 [Contracts/Agent/Capabilities/Tools](../../../../Contracts/Agent/Capabilities/Tools/Tools.md)：按任务列举定义，等待一次调用并返回 Observation。

## 当前组件

工具管理与本地执行协议保留在模块根目录，具体工具集中放在 Implementations：

```text
Tools
├─ LocalToolManager.cs
├─ ILocalTool.cs
├─ Implementations
│  └─ GetCurrentTimeTool.cs
└─ Tools.md
```

| 组件 | 一句话概述 |
|---|---|
| [LocalToolManager](./LocalToolManager.cs) | 按名称索引工具，校验任务归属与活动状态，拒绝重复调用并生成 Observation。 |
| [ILocalTool](./ILocalTool.cs) | 本地扩展点，提供 ToolDefinition 和一次执行方法；具体工具负责按自身 Schema 校验参数。 |
| [GetCurrentTimeTool](./Implementations/GetCurrentTimeTool.cs) | 只接受空 JSON 对象，使用 TimeProvider 返回 `{"utcTime":"..."}`，不产生副作用。 |

## 添加工具

1. 在 Implementations 中实现 ILocalTool，定义唯一名称、描述和对象类型的输入 JSON Schema。
2. 执行前严格校验具体参数；实现需支持并发调用，并遵守 CancellationToken。
3. 返回 ToolResult，失败或未知结果必须带 Error；异常也可以明确向上传播。
4. 在 Host 中注册 `AddSingleton<ILocalTool, YourTool>()`，无需修改 Manager 分发代码。

Host 注册单例 IToolManager，并在启动时解析，重复名称会导致启动失败。
Manager 持有构造时的定义快照；注册与权限不支持运行时动态变化。
当前策略是：本地用户拥有的 Running、WaitingForModel、WaitingForTool 任务可以使用所有已注册工具；
AllowedTools 只缩小本次发现范围，不是授权令牌或持久化任务策略。
任务状态在调用前检查，不与整个工具执行保持同一把锁；执行中的任务取消需要调用方传递业务取消 Token。

## 调用限制

- Session、Task ID 必须有效，任务必须存在、属于当前用户和所给 Session，并处于可执行状态。
- 通用入口校验参数是合法 JSON 对象；具体 Schema 的语义校验由具体工具实现，不包含通用 Schema 引擎。
- Call ID 在单例 Manager 生命周期内只允许执行一次，并发、完成、失败或取消后的重复调用均拒绝。
  ID 保留在内存中，不跨重启，也没有自动清理；长期服务需要另行设计有界、可持久化的调用记录。
- Timeout 是协作式取消，不强制终止线程；不响应 Token 的工具可能继续运行，不能将超时声称为已停止。
- 原始 Content 直接用作 Observation.Summary，Content 与 Error 合计最多 8192 个 UTF-16 字符；
  超限明确抛异常，不静默截断。Artifact 输出暂未实现，因此工具应返回紧凑结果。
- 业务失败、未知状态和错误保持可见；不自动重试。没有新增 Console 命令或修改 DirectExecutor。

## 设计边界

工具调用需要稳定 Call ID，结果必须关联原调用；大输出保存为 Artifact 并返回摘要或引用。
带副作用操作不得因为网络超时就盲目重试，执行结果未知时应明确表达。
本地注册由入口组合，外部工具的 HTTP / MCP 等传输细节由适配器隔离。
原始 ToolResult 经受控整理生成 ToolObservation，保留失败与未知状态；取消调用等待不证明远端动作已停止。

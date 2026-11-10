# Responses

## 功能与定位

区分工具产生的原始结果与返回 Agent 的受控观察数据。

当前无子目录，包含：

| 类型 | 功能 |
|---|---|
| [ToolResult](./ToolResult.cs) | 表达执行状态、可空原始 Content 和可空 Error，供具体工具协议复用。 |
| [ToolObservation](./ToolObservation.cs) | 关联调用，返回状态、摘要、错误、可空 Artifact 引用及结束时间和耗时。 |

## 状态与输出约定

- Succeeded 的 Error 为 null；无输出操作允许 Content 为 null 或空字符串，不虚构输出。
- Failed 和 OutcomeUnknown 必须提供非空白 Error；Cancelled 可提供取消原因。
- 不成功时可以保留真实的部分输出，不能据此把状态变为成功。
- Observation 必须保留原始结果状态和错误，不能在摘要整理时掩盖失败或未知结果。
- Summary 为非空白且有用的观察说明；小输出可直接作为摘要，无输出时描述已确认的结果。
- 大输出保存到 Artifact 后返回摘要与 ArtifactReference，不能只返回失效引用或静默丢失必要内容。
- 引用是可解析的存储标识，不内嵌凭据；ArtifactReference 为 null 表示没有外部输出引用。
- CallId、ToolName 与请求一致；Timestamp 为该观察生成时的结束时间，Duration 非负。

ToolResult 与 ToolObservation 不是两份独立的执行事实，后者由前者加调用上下文整理生成。
当前未实现 Artifact 存储、摘要策略或输出大小限制，也未在 DTO 构造中强制这些业务约定。

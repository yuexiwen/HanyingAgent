# Responses

## 功能与定位

表达一次模型生成的结果，而不是整个 Agent Task 的终态结果。

当前无子目录，只有 [ModelResponse](./ModelResponse.cs)：

| 字段 | 功能 |
|---|---|
| Text | 可空模型文本，可能与工具调用同时返回，不称为 FinalAnswer。 |
| ToolCalls | 非 null 的完整 ToolCallRequest 列表，无调用时为空列表。 |
| FinishReason | 必需的标准化结束原因，不能缺失后默认为 Stop。 |
| Usage | 可空 ModelUsage，单项未知也使用 null。 |
| ProviderFinishReason | 可空的 Provider 原始结束原因标识，不保存整份原始响应。 |

## 结果约定

- ToolCalls 结束原因必须有一个或多个完整、有效且被允许的调用；其他结束原因不返回可执行 ToolCalls。
- Stop 应提供非空白文本，不表示 Task 自动完成。
- Length 可返回部分文本，不生成可执行的截断工具调用；Runtime 需明确处理截断，不能当作正常完成。
- ContentFiltered 与 Refused 保留可用说明但不返回可执行工具调用，不包装成正常回答。
- 未知 Provider 原因映射为 Unknown 并保留已报告的原始标识；若 Provider 未报告原因，原始标识为 null。
- 结束原因未知时不自动执行工具或宣告任务完成；异常、解析失败和非法输出应显式报错，不伪造该响应。
- ToolCalls 和 FinishReason 为 JSON 必需字段；未知 enum 值、空列表规则和结果合法性由后续实现校验。

工具调用 ID、参数与后续 Observation 必须保持关联。Provider 若需要 ID 转换，适配器须维护对应关系，
不能把历史工具结果匹配到另一项调用。当前 Ollama 适配器已实现纯文本响应解析与结果验证，
但明确拒绝工具输出；其他 Provider 和完整工具协议仍待实现，DTO 本身不自动完成业务校验。

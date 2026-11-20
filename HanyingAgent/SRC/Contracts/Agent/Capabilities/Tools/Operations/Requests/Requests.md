# Requests

## 功能与定位

定义可序列化调用数据，将模型生成的参数与 Runtime 提供的执行归属分开。

当前无子目录，包含：

| 请求 | 功能 |
|---|---|
| [ToolCallRequest](./ToolCallRequest.cs) | 携带调用 Id、ToolName 和 ArgumentsJson，可供未来模型返回协议复用。 |
| [InvokeToolRequest](./InvokeToolRequest.cs) | 在 Call 外补充 SessionId 和 TaskId，提交一次实际调用。 |
| [ListToolDefinitionsRequest](./ListToolDefinitionsRequest.cs) | 指定执行归属和可空的 AllowedTools 名称过滤。 |

## 校验约定

- ID 与 ToolName 必须有效，Call 不为 null；ArgumentsJson 是 JSON 对象，并符合工具输入 Schema。
- 无参数工具仍传递 "{}"，不能用空字符串或任意非法 JSON 代替。
- SessionId 和 TaskId 由 Runtime 从可信任务上下文提供，不由模型自行决定授权范围。
- 如果 Provider 的调用标识只在单次模型响应内唯一，适配器需映射为应用实例内的稳定 ToolCallId。
- AllowedTools 为 null 时列举全部已授权工具，空列表时不列举工具；非空时只返回授权集合与名称集合的交集。
- 名称按 Ordinal 精确匹配，过滤不会授予额外权限；重复名称按集合处理，非法空白名称明确报错。
- 不携带本地锁、委托、服务实例、认证令牌或执行句柄。

这些是后续管理器的入口校验要求，DTO 本身不解析 JSON 或查询任务归属。

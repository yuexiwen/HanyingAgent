# Models

## 功能与定位

定义工具说明、调用关联标识和执行结果分类，不包含本地工具实例。

当前无子目录，包含以下类型：

| 类型 | 功能 |
|---|---|
| [ToolCallId](./ToolCallId.cs) | 标识一次工具调用，显式构造拒绝空白并支持 JSON 反序列化。 |
| [ToolDefinition](./ToolDefinition.cs) | 描述名称、用途、输入 JSON Schema、副作用和可空超时要求。 |
| [ToolCallStatus](./ToolCallStatus.cs) | 区分成功、明确失败、确认取消和结果未知。 |

## 工具定义约定

- Name 为非空白且区分大小写的稳定名称，在可用工具集合内唯一。
- Description 为非空白的使用说明；InputJsonSchema 为有效 JSON Schema，描述参数对象。
- HasSideEffects 默认 false，由工具提供者准确声明，不能据此跳过授权或推断重试安全。
- Timeout 默认 null，表示未在定义中指定，仍受执行环境策略约束；显式超时须大于零。
- 不引入 Provider SDK 或可执行函数，Schema 暂用字符串表示，不添加 Schema 库依赖。

## 结果状态

| 状态 | 值 | 含义 |
|---|---|---|
| Succeeded | 0 | 已知本次调用成功。 |
| Failed | 1 | 已知本次调用失败，不保证没有部分副作用或已回滚。 |
| Cancelled | 2 | 已确认调用取消，不保证此前副作用被撤销。 |
| OutcomeUnknown | 3 | 无法确认最终结果，例如远端执行后的响应丢失。 |

Timeout 或取消等待本身不足以认定 Failed / Cancelled，无法确认时必须表达 OutcomeUnknown 或显式调用异常。
ToolCallId 只是关联键，不自动赋予幂等执行；default ID 可能无效，模块入口仍需校验。
除 ID 构造外，这些约定由后续实现验证，record 和 enum 不自动强制执行业务规则。

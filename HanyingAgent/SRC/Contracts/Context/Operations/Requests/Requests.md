# Requests

## 功能与定位

[BuildContextRequest](./BuildContextRequest.cs) 是一次上下文构建请求。

| 字段 | 约定 |
|---|---|
| SystemInstruction | 必需的非空白可信系统指令。 |
| Messages | 必需的已排序消息序列，包含当前用户输入并以 User 结尾。 |
| MemoryOwners | 必需的归属列表，可为空；不允许 null 项，重复归属按值去重。 |
| Options | 可选的 ModelOptions，默认 null；构建器不替 Provider 解释模型专属参数。 |

## 数据边界

- 三个必需字段使用 JsonRequired，缺字段反序列化失败；显式 null 等业务无效值由构建器拒绝。
- ModelMessage 复用已有 role 判别协议，MemoryOwner 复用严格的 Scope / ID 组合模型。
- 不传 Tools、Task.Input 或第二份当前用户输入；当前版本只输出空工具列表。
- Options 不等于输入预算，MaxOutputTokens 仅表达输出选项；本地实现的总输入字符上限由构造参数配置，默认 32,000，不属于请求字段，也不是 Token 上限。
- 不以 MemoryOwners 的结构校验替代真实归属、身份或权限验证。

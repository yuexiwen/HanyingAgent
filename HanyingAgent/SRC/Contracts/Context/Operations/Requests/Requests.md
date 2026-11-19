# Requests

## 功能与定位

[BuildContextRequest](./BuildContextRequest.cs) 是一次上下文构建请求。

| 字段 | 约定 |
|---|---|
| SystemInstruction | 必需的非空白可信系统指令。 |
| Messages | 必需的已排序消息序列，包含当前用户输入并以 User 结尾。 |
| Memories | 必需的 MemoryItem 列表，可为空；归属权限、状态和到期过滤由调用方及 Memory 服务保证。 |
| Options | 可选的 ModelOptions，默认 null；构建器不替 Provider 解释模型专属参数。 |

## 数据边界

- 三个必需字段使用 JsonRequired，缺字段反序列化失败；显式 null 等业务无效值由构建器拒绝。
- ModelMessage 复用已有 role 判别协议，MemoryItem 复用 Memory 契约；null 项、缺失必要内容和冲突 ID 明确拒绝。
- 不传 Tools、Task.Input 或第二份当前用户输入；当前版本只输出空工具列表。
- Options 不等于输入预算，MaxOutputTokens 仅表达输出选项；本地实现的总输入字符上限由构造参数配置，默认 32,000，不属于请求字段，也不是 Token 上限。
- 不以 Memories 的结构校验替代真实归属、身份或权限验证。Builder 不根据数据中的 Owner 再次读取服务。
- MemoryOwners 已移除，改为传递明确的数据快照，旧请求需要更新，不能省略 Memories。

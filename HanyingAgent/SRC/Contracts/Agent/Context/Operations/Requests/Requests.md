# Requests

## 功能与定位

[BuildContextRequest](./BuildContextRequest.cs) 是一次上下文构建请求。

| 字段 | 约定 |
|---|---|
| SystemInstruction | 必需的非空白可信系统指令。 |
| Messages | 必需的按时间排列的候选完整 User / Assistant 问答轮次，最后一条为当前 User 输入。 |
| Memories | 必需的 MemoryItem 列表，可为空；归属权限、状态和到期过滤由调用方及 Memory 服务保证。 |
| Options | 可选的 ModelOptions，默认 null；MaxOutputTokens 用于输出预留，未指定时在结果中补齐配置默认值。 |
| MaxInputCharacters | 可选的正整数字符保护上限，默认 null；只能收紧 Builder 配置，不是 Token 数。 |

## 数据边界

- 三个必需字段使用 JsonRequired，缺字段反序列化失败；显式 null 等业务无效值由构建器拒绝。
- ModelMessage 复用已有 role 判别协议，MemoryItem 复用 Memory 契约；null 项、缺失必要内容和冲突 ID 明确拒绝。
- 不传 Tools、Task.Input 或第二份当前用户输入；当前版本只输出空工具列表。
- Context 统一选择候选历史与记忆；System 与当前输入必须完整保留。候选数量不保证最终带入数量。
- 模型输入 Token 额度扣除 MaxOutputTokens 与安全余量；字符保护上限另外校验，默认 32,000。
  当前生产计数器明确使用估算，不代表 Provider 实际用量或精确 tokenizer。
- 不以 Memories 的结构校验替代真实归属、身份或权限验证。Builder 不根据数据中的 Owner 再次读取服务。
- MemoryOwners 已移除，改为传递明确的数据快照，旧请求需要更新，不能省略 Memories。

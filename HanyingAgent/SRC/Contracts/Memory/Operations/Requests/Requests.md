# Requests

## 功能与定位

定义记忆操作的不可变输入数据；Request 表示业务调用输入，不限定 HTTP 或其他传输方式。

当前无子目录，包含以下类型：

| 请求 | 功能 |
|---|---|
| [MemoryWriteRequest](./MemoryWriteRequest.cs) | 提供归属、分类、内容、来源、可信度、重要性与可选有效期，不包含生成的 ID 和时间戳。 |
| [MemoryUpdateRequest](./MemoryUpdateRequest.cs) | 完整替换可修改字段，其中 ExpiresAt 为 null 表示清除有效期而非保持原值。 |
| [MemoryReadRequest](./MemoryReadRequest.cs) | 按 Owner 和可选 Type 直接读取最近项，默认 Limit 为 20。 |
| [MemorySearchRequest](./MemorySearchRequest.cs) | 以自然语言 Query、Owner 和可选 Type 进行检索，默认 TopK 为 10。 |

## 设计边界

- Update 不修改 ID、Owner、Type 或 CreatedAt，目标 MemoryId 单独传入方法。
- Read 没有 Query，不要求分词或相关性评分。
- Search 的 Query 是检索需求，不是 SQL，也不要求调用方自行生成倒排词项。
- DTO 当前不执行完整输入校验；后续 Manager 必须拒绝空白内容或查询、无效分值及非正的数量上限。
- 请求归属来自可信执行上下文，不能把客户端提交的 ID 直接视为访问许可。

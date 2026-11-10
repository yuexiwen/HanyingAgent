# Responses

## 功能与定位

定义分页特有响应；追加和单条查询直接返回 ConversationMessage。

当前无子目录，只有 [ConversationMessagePage](./ConversationMessagePage.cs)：

| 字段 | 功能 |
|---|---|
| Items | 非 null 的本页记录，数量不超过 PageSize，按 Sequence 严格升序。 |
| ContinuationToken | 可空的继续读取更早历史的令牌；没有更早记录时为 null。 |

## 结果约定

- Items 中记录属于同一请求 Session，消息 ID 和序号不重复。
- 无历史时返回空 Items 和 null 令牌，不用 null 列表表示空页。
- Host 加载下一页后可将更早记录放到当前展示前面，不把页的读取先后当作消息顺序。
- 分页可能切断 Assistant 调用与 Tool 结果；Context 必须补齐需要的关联或舍弃整个相关片段，不能将半套协议发送给模型。
- 返回的是完整记录快照，没有流式片段、总页数、总消息数或实时订阅语义。

这些结果约束由后续实现保证，DTO 本身不排序、不校验跨页关联。

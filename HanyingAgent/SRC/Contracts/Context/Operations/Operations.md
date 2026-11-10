# Operations

## 功能与定位

[IContextBuilder](./IContextBuilder.cs) 提供 BuildAsync，接收请求与 CancellationToken，返回 ModelInvocation。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Requests](./Requests/Requests.md) | 定义构建所需的系统指令、消息、归属和模型选项。 |

## 操作约定

- 只组装请求，不调用 LLM、不执行工具，也不写入 Memory、Conversation 或 Task。
- 非法输入显式抛 ArgumentException / ArgumentNullException；不支持的协议抛 NotSupportedException。
- 原始消息只允许非空白 User / 纯文本 Assistant，Assistant 的 ToolCalls 必须为非 null 空列表。
- 消息序列至少有当前 User 输入，并以 User 结尾；不要求历史严格交替，不插入缺失轮次。
- System 消息须通过独立 SystemInstruction 提供，不允许嵌入原始消息序列。
- 每个不同 Owner 最多读取 20 条工作记忆；重复 ID 去重后统一按 UpdatedAt 降序、Id.Value Ordinal 升序排列。
- 输出顺序为 System、可选的参考记忆 User 消息、原始消息；Tools 始终为空，Options 原样传递。
- 本地实现按最终所有消息正文的 string.Length 校验构造配置的总字符上限，默认 32,000；等于允许，超过抛 InvalidOperationException，不裁剪或返回部分结果。
- 不支持工具结果或 Assistant 工具调用时明确拒绝，不丢弃协议片段或伪造成普通文字。
- 依赖异常与取消显式传播，无自动重试或缺失记忆时的成功形回退。
- 输入集合在读取记忆前快照化，返回的消息集合不共享调用方可变容器。

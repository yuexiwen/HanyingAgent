# Operations

## 功能与定位

[IContextBuilder](./IContextBuilder.cs) 提供 BuildAsync，接收请求与 CancellationToken，返回 ModelInvocation。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Requests](./Requests/Requests.md) | 定义构建所需的系统指令、消息、记忆数据和模型选项。 |

## 操作约定

- 统一管理模型输入预算、选择并组装候选内容，不读取 Memory 或 Conversation，不调用 LLM、不执行工具，也不写入业务状态。
- 非法输入显式抛 ArgumentException / ArgumentNullException；不支持的协议抛 NotSupportedException。
- 原始消息只允许非空白 User / 纯文本 Assistant，Assistant 的 ToolCalls 必须为非 null 空列表。
- 消息序列由完整的 User / Assistant 历史轮次与最后的当前 User 构成；不插入缺失轮次。
- System 消息须通过独立 SystemInstruction 提供，不允许嵌入原始消息序列。
- 记忆由编排层读取和校验权限；Builder 检查必要字段，重复 ID 去重，冲突载荷明确拒绝，
  再按 UpdatedAt 降序、Id.Value Ordinal 升序排列。当前编排读取用户最近最多 20 条有效记忆。
- 输出顺序为 System、可选的参考记忆 User 消息、选中的完整历史轮次和当前输入；Tools 始终为空。
  显式输出上限保留，未指定时补齐预算使用的默认 MaxOutputTokens。
- 模型 Token 额度预留输出与安全空间后，剩余额度默认按 History 75% / Memory 25% 分配，可借用闲置份额。
  候选超限时省略完整轮次或记忆条目并记录数量，不删除原数据。
- 最终正文另受 UTF-16 字符上限约束，默认 32,000；请求可收紧。等于允许；
  必需的系统指令与当前输入本身超限抛 InvalidOperationException，不截断或伪装成功。
- 不支持工具结果或 Assistant 工具调用时明确拒绝，不丢弃协议片段或伪造成普通文字。
- 取消显式传播，校验失败不返回部分上下文；没有需要 Builder 处理的记忆读取重试或降级。
- 输入集合快照化，返回的消息集合不共享调用方可变容器；构建操作可同步完成，但保留 Task 返回契约。

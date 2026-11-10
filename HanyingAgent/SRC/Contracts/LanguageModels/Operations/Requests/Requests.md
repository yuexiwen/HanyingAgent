# Requests

## 功能与定位

定义统一模型调用输入，不泄露 Provider SDK 或业务状态管理对象。

当前无子目录，只有 [ModelInvocation](./ModelInvocation.cs)：

| 字段 | 功能 |
|---|---|
| Messages | 按顺序排列的非空模型消息列表。 |
| Tools | 本轮允许模型提出调用的工具定义集合，无工具时传空列表。 |
| Options | 可空的 ModelOptions，不指定时使用明确配置。 |

## 设计边界

- Messages 和 Tools 不为 null，成员不为 null；工具名称唯一，定义需有效且已经过业务授权筛选。
- 有工具定义并不意味着模型必须调用，更不意味着语言模型模块代为执行工具。
- 不再设置重复的 EnableToolCalling 标志，空 Tools 就表示关闭工具调用。
- Messages 与 Tools 为 JSON 必需字段，缺失显式报错；null、空消息和业务关联仍需入口校验。
- 消息使用 ModelMessage 基类的 JSON 判别协议，保留 Assistant 工具调用与 Tool Observation 的结构。
- 不携带密钥、Endpoint、数据库连接、取消源或整个执行状态；调用者身份和权限由可信入口处理。
- 不依赖尚未定义的 ModelContext，Context 内部分析与预览数据无需全部跨模型调用边界传递。

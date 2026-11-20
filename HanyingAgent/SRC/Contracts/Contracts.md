# Contracts

## 功能与定位

定义模块之间稳定的公共协议，包括强类型 ID、不可变数据模型、操作请求、结果和异步能力接口。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Management](./Management/Management.md) | User、Session、Task 的身份、归属、状态与运行管理操作。 |
| [Agent](./Agent/Agent.md) | Context、Execution、LanguageModels 以及 Conversation、Memory、Tools 能力协议。 |

## 设计边界

- 不引用 Provider SDK、数据库连接或 Runtime 的实现类型。
- 构造校验可以维护基本数据约束，但不执行检索、持久化或业务编排。
- 数据归属不等于访问授权，具体实现仍需验证调用者权限。
- 公共业务协议不直接等同 HTTP API；远程适配器负责必要的传输映射。
- 每个领域按需保留 Models、Operations、Requests、Responses；不为分组新增空实现或重复接口。
- 目录和命名空间同步分层，是源码引用路径的变更；JSON 字段、role 判别值和业务行为不变。
- Agent 契约引用 Management 的归属 ID 是正常依赖，不要求两组完全隔离。

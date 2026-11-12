# Contracts

## 功能与定位

定义模块之间稳定的公共协议，包括强类型 ID、不可变数据模型、操作请求、结果和异步能力接口。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Sessions](./Sessions/Sessions.md) | 定义会话标识、用户归属、生命周期和创建、查询、列举、关闭协议。 |
| [Tasks](./Tasks/Tasks.md) | 定义任务身份、Session 归属、生命周期和提交、查询、补充、取消协议。 |
| [Conversation](./Conversation/Conversation.md) | 定义类型化历史、稳定 ID、会话内顺序及幂等追加、查询和向更早历史分页协议。 |
| [Users](./Users/Users.md) | 定义用户标识、资料快照和创建、查询、列举、显示名称更新协议。 |
| [LanguageModels](./LanguageModels/LanguageModels.md) | 定义类型化消息、工具协议和非流式模型调用、结束原因与用量。 |
| [Context](./Context/Context.md) | 定义纯文本上下文构建请求与接口，输出既有 ModelInvocation。 |
| [Execution](./Execution/Execution.md) | 定义执行交付结论与结果，保留诊断元数据，不代替 Task 生命周期。 |
| [Memory](./Memory/Memory.md) | 定义记忆模型以及工作记忆和长期记忆的访问协议。 |
| [Tools](./Tools/Tools.md) | 定义工具描述、按任务发现与调用、执行结果和 Observation 协议。 |

## 设计边界

- 不引用 Provider SDK、数据库连接或 Runtime 的实现类型。
- 构造校验可以维护基本数据约束，但不执行检索、持久化或业务编排。
- 数据归属不等于访问授权，具体实现仍需验证调用者权限。
- 公共业务协议不直接等同 HTTP API；远程适配器负责必要的传输映射。

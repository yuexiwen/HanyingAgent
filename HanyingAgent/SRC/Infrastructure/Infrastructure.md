# Infrastructure

## 功能与定位

提供与技术平台有关的具体实现，使 Runtime 与能力业务规则不依赖某个 Provider、数据库或传输协议。

当前 Persistence 已实现工作记忆的进程内存 Store；其余适配与可观测性仍为目录骨架。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [LanguageModels](./LanguageModels/LanguageModels.md) | 适配具体语言模型 API、SDK 和响应协议。 |
| [Persistence](./Persistence/Persistence.md) | 实现状态、记忆和 Checkpoint 所需的具体存储。 |
| [Observability](./Observability/Observability.md) | 实现日志、指标、追踪和事件传输。 |

## 设计边界

实现 Contracts 或业务模块的内部端口，不反向定义上层编排规则。
未来的远程记忆客户端与搜索适配器也属于基础设施，但仅在有实际实现时增加对应子目录。
网络错误、超时和序列化失败必须明确传播，不能伪装成空结果或成功。

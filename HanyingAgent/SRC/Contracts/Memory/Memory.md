# Memory

## 功能与定位

定义记忆本身及其访问协议，将按归属直接读取的工作记忆与按相关性召回的长期记忆分开。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Models](./Models/Models.md) | 定义记忆标识、归属、内容分类、状态和完整记忆数据。 |
| [Operations](./Operations/Operations.md) | 定义共同变更能力以及两种读取路径的接口、请求与结果。 |

## 设计边界

- Scope 表示归属，不决定保存期限或存储技术；Task / Session 与 User / Global 只是两类记忆的默认使用范围。
- Global 限定为当前实例内共享，当前没有多租户契约。
- 工作记忆不依赖 Query 或搜索分数，长期召回可依赖独立的派生索引。
- 当前只有数据类型、接口与部分构造校验，尚未实现 Manager、Store、评分、索引或授权。
- 业务实现位于 [Capabilities/Memory](../../Capabilities/Memory/Memory.md)，不是本目录的职责。

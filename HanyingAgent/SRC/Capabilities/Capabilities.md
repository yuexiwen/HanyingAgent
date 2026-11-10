# Capabilities

## 功能与定位

承载 Runtime 可使用的业务能力，关注能力本身的规则，不控制整个 Agent 执行循环。

当前各子模块均为目录骨架。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Memory](./Memory/Memory.md) | 管理工作记忆与长期记忆的业务规则及访问协调。 |
| [Skills](./Skills/Skills.md) | 选择并加载特定任务的操作说明与参考资料。 |
| [Tools](./Tools/Tools.md) | 管理工具定义、校验、执行约束和结果标准化。 |

## 设计边界

公共协议位于 Contracts，内部存储或检索端口可由能力模块声明，技术实现放在 Infrastructure。
能力实现不依赖 Runtime 的 Orchestrator；本地与远程适配通过入口组合替换。

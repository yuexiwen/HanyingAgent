# SRC

## 功能与定位

承载应用的模块源码，以角色分组减少顶层平铺；目录层次表达职责，不要求每组拆成独立项目或服务。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Contracts](./Contracts/Contracts.md) | 定义模块之间的能力接口、标识和数据协议。 |
| [Host](./Host/Host.md) | 将用户命令和输入适配成 Runtime 操作，并展示结果与事件。 |
| [Runtime](./Runtime/Runtime.md) | 协调任务生命周期、执行策略、Context 构建和执行状态。 |
| [Capabilities](./Capabilities/Capabilities.md) | 实现记忆、技能和工具等可供 Runtime 使用的业务能力。 |
| [Infrastructure](./Infrastructure/Infrastructure.md) | 实现模型访问、存储和可观测性等技术适配。 |

## 依赖边界

- Contracts 不依赖业务实现或基础设施实现。
- Runtime 通过契约使用 Capabilities，不直接创建数据库或 HTTP 客户端。
- Capabilities 不反向依赖 Runtime 的编排实现，可声明实现所需的内部端口。
- Infrastructure 实现公共契约或内部端口，项目根入口负责选择具体适配器。
- 当前只有部分 Contracts 类型已有代码，其余分组主要为后续实现预留。

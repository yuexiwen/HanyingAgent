# Management

## 功能与定位

管理用户身份、会话归属和任务生命周期。这里的业务管理不等于线程调度：
TaskManager 安排后台入口并跟踪完成，实际代码调度由 .NET 运行时负责。

| 子模块 | 职责 |
|---|---|
| [Users](./Users/Users.md) | 用户资料与可信本地用户上下文。 |
| [Sessions](./Sessions/Sessions.md) | 会话归属、状态及单会话占用。 |
| [Tasks](./Tasks/Tasks.md) | 提交、查询、取消、结果保存及运行资源收尾。 |

Task Lifecycle Flow 从入口接收提交，创建业务记录、安排执行，再根据结果保存终态。
Agent 内部工作流程由 [Agent](../Agent/Agent.md) 负责。
任务管理调用 Agent 编排入口，Agent 不反向依赖 Tasks 的实现；
必要的用户身份和会话访问通过各自模块协作。

公共协议见 [Contracts/Management](../../Contracts/Management/Management.md)。
User、Session、Task 主要表达控制面身份、归属和状态，但 Task.Input 等仍承载实际内容；
控制面与数据面是职责视角，不是互不关联的数据孤岛。

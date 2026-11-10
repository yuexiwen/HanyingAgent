# Orchestration

## 功能与定位

协调 Session 与 Agent Task 的生命周期，为 Host 提供运行入口，并在任务启动、等待、完成、取消和失败时组织其他模块协作。

当前为目录骨架，没有子目录或业务实现。

## 后续组件规划

| 组件 | 一句话概述 |
|---|---|
| AgentRuntime | 提供执行、观察及任务控制的统一应用入口。 |
| TaskManager | 按 ITaskManager 接受任务、提供查询并协调等待输入和取消请求。 |
| AgentOrchestrator | 串联任务创建、Skill 选择、策略执行、Checkpoint 和完成事件。 |
| SessionManager | 创建、查询、关闭会话并与任务入口协调当前占用。 |
| 本地执行句柄 | 保存执行 Task 和 CancellationTokenSource 等不可序列化资源。 |

## 设计边界

会话公共信息与本地执行资源分离；取消一次调用等待不等于任务已取消。
不在此实现 Context 排序、工具参数执行或模型 Provider 解析；同一 Session 的执行与快照协调规则后续在这里落实。
Task 提交先返回接受快照，执行在后台继续；历史通过 Task.SessionId 查询，不在会话快照中重复维护任务集合。
SessionInfo 保存所属 UserId；Runtime 按该 ID 通过用户契约读取资料，按 User 级 MemoryOwner 召回用户记忆。
SessionManager 创建时验证用户存在性和权限，恢复及访问会话时校验已保存的归属，不从当前调用者隐式替换 UserId。
这些归属读取和校验目前仍是规划，没有具体管理器实现。

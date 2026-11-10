# Mini Agent Runtime 设计初稿

## 1. 文档目的

本文档定义一个面向学习目的的单进程 Agent Runtime 设计初稿。

系统按照模块化单体（Modular Monolith）方式实现：所有组件位于同一个 .NET Project 和进程内，通过 C# 接口交互，不拆分成独立微服务。

设计目标是亲手实现并理解以下 Agent Runtime 核心机制：

- 多 Session 管理
- Agent Task 生命周期
- Direct、Prompt Chaining 和 ReAct 执行模式
- Model 调用与 Tool Calling
- Context 构造
- State 管理与 Checkpoint
- Short-term / Long-term Memory
- Skill 加载
- Tool 注册与执行
- Persistence 与恢复
- Observability 与事件流

---

## 2. 设计原则

### 2.1 模块化单体

所有模块都在一个 Project 内实现，但保持清晰的职责和接口边界：

```text
HanyingAgent.csproj
```

暂时不引入：

- HTTP / gRPC 服务间通信
- 微服务拆分
- 消息队列
- Kubernetes
- 分布式锁
- 分布式事务
- Vector Database
- Multi-Agent 协作

### 2.2 职责分离

核心边界：

- **Orchestrator 管任务生命周期。**
- **Execution Engine 管任务内部如何推进。**
- **State Manager 管任务运行到哪里。**
- **工作记忆与长期记忆模块管理 Agent 已经知道什么，分别提供直接读取和相关性检索。**
- **Context Manager 管当前一轮让 LLM 知道什么。**
- **Tool Manager 管 Agent 实际能够做什么。**
- **Skill Manager 管某类任务应该怎么做。**
- **Model Runtime 管如何调用 LLM。**

### 2.3 先实现逻辑边界，不急于实现物理边界

各模块通过本地接口调用：

```csharp
await _memoryManager.SearchAsync(...);
```

而不是：

```text
HTTP → Memory Service
```

### 2.4 显式命令使用确定性代码

例如：

```text
sls
select session-01
tail -f session-01
cancel session-01
close session-01
```

这些命令由 Shell 直接解析执行，不调用 LLM。

仅在输入是模糊自然语言时，才考虑使用 LLM 做路由或语义决策。

---

## 3. 建议技术栈

- .NET 8 或更新版本
- 单个 Console Project
- `Microsoft.Extensions.DependencyInjection`
- `Microsoft.Extensions.Logging`
- `System.Threading.Channels`
- `IAsyncEnumerable<T>`
- JSON 文件作为第一版 Checkpoint Store
- SQLite 作为后续持久化实现
- 一个支持 Tool Calling 的 Model API
- 一个可预测的 `MockModelClient` 用于单元测试

---

## 4. 总体模块结构

项目名为 `HanyingAgent`。第一版使用单个 Console Project，当前骨架目标框架为 .NET 10。
HanyingAgent 仍是项目根目录；模块目录放入其 SRC 子目录，TEST 与 SRC 同级。
项目文件、Program.cs 和生成目录 bin / obj 保留在项目根目录：

```text
Hanying
├── .docs
└── HanyingAgent
    ├── HanyingAgent.csproj
    ├── Program.cs
    ├── SRC
    │   └── 各业务模块目录
    ├── TEST
    │   └── .gitkeep
    ├── bin
    └── obj
```

`HanyingAgent/SRC` 保存业务模块代码，入口 Program.cs 留在项目根目录；
`HanyingAgent/TEST` 预留给后续 UT 项目，目前不选择测试框架或创建测试项目。
应用项目通过 DefaultItemExcludes 排除 TEST 子树，避免将测试代码及其生成文件编译进应用。
测试项目后续通过 ProjectReference 引用应用项目。移动目录不改变业务类型的命名空间。

从工作区根目录构建和运行：

```powershell
dotnet build .\HanyingAgent\HanyingAgent.csproj
dotnet run --project .\HanyingAgent\HanyingAgent.csproj
```

以下为 `HanyingAgent/SRC` 内部结构；后续模块相对路径（如 Contracts/Memory）均相对于 SRC：

```text
SRC
├── Contracts
│   ├── Sessions
│   │   ├── Models
│   │   └── Operations
│   │       └── Requests
│   ├── Tasks
│   │   ├── Models
│   │   └── Operations
│   │       ├── Requests
│   │       └── Responses
│   ├── Users
│   │   ├── Models
│   │   └── Operations
│   │       └── Requests
│   ├── Conversation
│   │   ├── Models
│   │   └── Operations
│   │       ├── Requests
│   │       └── Responses
│   ├── LanguageModels
│   │   ├── Models
│   │   └── Operations
│   │       ├── Requests
│   │       └── Responses
│   ├── Memory
│   │   ├── Models
│   │   └── Operations
│   │       ├── Requests
│   │       └── Responses
│   └── Tools
│       ├── Models
│       └── Operations
│           ├── Requests
│           └── Responses
├── Host
├── Runtime
│   ├── Orchestration
│   ├── Execution
│   ├── Context
│   ├── Conversation
│   └── State
├── Capabilities
│   ├── Memory
│   ├── Skills
│   └── Tools
└── Infrastructure
    ├── LanguageModels
    ├── Persistence
    └── Observability
```

五个顶层目录按角色分组，不是五个微服务，也不要求拆成五个 Project：

| 目录           | 职责                                                     |
| -------------- | -------------------------------------------------------- |
| Contracts      | 模块能力接口与可序列化数据契约，不依赖业务或基础设施实现 |
| Host           | Shell、命令解析与用户交互适配                            |
| Runtime        | 编排、执行策略、Context 构建和执行状态                   |
| Capabilities   | Memory、Skill、Tool 等能力的业务规则及其内部依赖端口     |
| Infrastructure | 模型 Provider、存储、远程客户端与可观测性等技术实现      |

Runtime 通过契约使用能力，不直接创建数据库或 HTTP 客户端。
Capabilities 不反向依赖 Runtime 的编排实现；Infrastructure 实现公共契约或模块所需的内部端口。
例如记忆业务规则放在 Capabilities/Memory，存储端口也可定义于此；
具体的内存 / JSON / SQLite 实现放在 Infrastructure/Persistence。
未来的远程记忆客户端或搜索适配器可放在 Infrastructure/Memory，需要时才创建目录。
Session 生命周期协调属于 Runtime/Orchestration，Host 只负责交互入口。
Program.cs 保留在项目根目录，通过依赖注入组合实现。
Contracts/LanguageModels 与 Infrastructure/LanguageModels 表示语言模型能力，
其他模块内部的 Models 仍表示数据结构分类，不随能力目录重命名。
Contracts/Conversation 已定义独立的历史消息、类型化内容和追加、查询、分页契约；
Runtime/Conversation 和 Conversation Store 仍未实现。
对话历史由 Conversation 模块管理，并由 Infrastructure/Persistence 中后续的 Conversation Store 保存；
按 SessionId 有序读取，必要时关联 TaskId，不放入 SessionInfo，也不等同于 Memory。
当前除已有 Contracts 类型外，其余目录仍是骨架；以下为后续组件规划，不代表已实现的文件：

```text
SRC
├── Host
│   ├── AgentShell
│   └── CommandParser
├── Runtime
│   ├── Orchestration
│   │   ├── AgentRuntime
│   │   ├── AgentOrchestrator
│   │   ├── AgentSession
│   │   └── SessionManager
│   ├── Execution
│   │   ├── ExecutionEngine
│   │   ├── DirectExecutor
│   │   ├── ChainExecutor
│   │   └── ReactExecutor
│   ├── Context
│   │   ├── ContextManager
│   │   ├── ContextAssembler
│   │   ├── ContextRanker
│   │   └── ContextBudget
│   ├── Conversation
│   │   └── ConversationManager
│   └── State
│       └── StateManager
├── Capabilities
│   ├── Memory
│   │   ├── LocalWorkingMemoryManager
│   │   ├── LocalLongTermMemoryManager
│   │   ├── IWorkingMemoryStore
│   │   ├── ILongTermMemoryStore
│   │   └── ILongTermMemoryRetriever
│   ├── Skills
│   │   ├── SkillManager
│   │   └── SkillCatalog
│   └── Tools
│       ├── ToolManager
│       └── ToolRegistry
└── Infrastructure
    ├── LanguageModels
    │   ├── LanguageModelRuntime
    │   └── Provider Clients
    ├── Persistence
    │   ├── Working Memory Store
    │   ├── Long-term Memory Store
    │   ├── Conversation Store
    │   └── JsonCheckpointStore
    └── Observability
        ├── AgentEventHub
        └── RuntimeMetrics
```

上面的组件规划省略了 Contracts 子树；公共数据与能力接口仍按前述 Contracts 结构组织，
不因实现目录分组而移动或改名。仅有一个组件时直接放文件，不为每个类建立空子目录。

---

## 5. 核心领域对象

### 5.1 强类型标识

```csharp
public readonly record struct SessionId(string Value);
public readonly record struct TaskId(string Value);
public readonly record struct ToolCallId(string Value);
public readonly record struct MemoryId(string Value);
public readonly record struct CheckpointId(string Value);
```

### 5.2 Task 公共契约

当前已定义任务协议，尚未实现 TaskManager、任务存储或执行器：

```text
Contracts/Tasks
├── Models
│   ├── TaskId
│   ├── TaskInfo
│   └── TaskStatus
└── Operations
    ├── ITaskManager
    ├── Requests
    │   ├── SubmitTaskRequest
    │   ├── ProvideTaskInputRequest
    │   └── ListSessionTasksRequest
    └── Responses
        ├── TaskListResponse
        └── TaskResult
```

TaskId 已移入 Contracts/Tasks/Models，SessionInfo 与 MemoryOwner 的引用已同步。
每个 TaskInfo 保存 SessionId、最初 Input、状态、Version 和时间字段；
会话历史按 TaskInfo.SessionId 反向分页读取，不在 SessionInfo 内重复维护全部 TaskIds。

| ITaskManager 操作  | 契约                                                               |
| ------------------ | ------------------------------------------------------------------ |
| SubmitAsync        | 在存在且空闲的 Session 内接受任务，返回 TaskInfo，不等待执行完成。 |
| GetAsync           | 返回任务快照，不存在时返回 null。                                  |
| ListBySessionAsync | 按 SessionId、可选状态与不透明游标读取历史页。                     |
| GetResultAsync     | 终态返回 TaskResult，未结束返回 null，未知任务显式报错。           |
| ProvideInputAsync  | 仅恢复 WaitingForUser 且 ExpectedVersion 匹配的原任务。            |
| CancelAsync        | 请求取消并返回快照，不承诺已经取消完成。                           |

任务状态为 Created、Running、WaitingForModel、WaitingForTool、WaitingForUser、Cancelling、
Completed、Failed、Cancelled。只有 Completed、Failed、Cancelled 是终态；
其他状态都保留 Session 占用，任务成功、失败或确认取消后才释放会话。
Created 表示接受后的准备状态，不是排队执行多个任务；Session 忙时拒绝新任务。

Version 从 0 开始，任务信息变更时递增；补充输入须校验版本和等待状态，防止过期回答恢复错误轮次。
当前仅支持等待用户时的补充，不设计运行中 steering；补充消息进入对话记录，不改变最初 Input。
Cancel 对终态可幂等返回，Cancelling 不代表已终止，完成与取消竞争时以最终查询结果为准。
方法 CancellationToken 取消调用等待，不等于业务取消，也不能保证撤销已接受的任务。

历史默认每页 20 项，按 CreatedAt 升序、ID Ordinal 升序排序，返回可空的下一页令牌；
令牌绑定查询范围，非法令牌或不匹配过滤条件显式报错，每页不保证属于同一个并发快照。
结果 DTO 只表达终态回答或错误；详细字段约定见各目录说明，全部业务规则仍待实现。
当前提交未定义幂等键，网络超时后的接收结果可能不确定，不能盲目重复提交。

### 5.3 执行器内部请求（规划）

以下 AgentRequest 是已接受任务进入执行器后的内部数据，不是 Host 的公开提交请求，
需携带已分配的 TaskId，不能在执行过程中再生成另一项任务：

```csharp
public sealed record AgentRequest(
    SessionId SessionId,
    TaskId TaskId,
    string Input,
    IReadOnlyDictionary<string, object>? Metadata = null);
```

### 5.4 执行器内部结果（规划）

AgentResult 表达一次内部运行结果，可以包含等待用户的暂停；它不等于只在终态产生的公共 TaskResult。

```csharp
public sealed record AgentResult(
    SessionId SessionId,
    TaskId TaskId,
    AgentCompletionStatus Status,
    string? FinalAnswer,
    string? Error = null);
```

```csharp
public enum AgentCompletionStatus
{
    Completed,
    WaitingForUser,
    Cancelled,
    Failed
}
```

### 5.5 User 公共契约

当前 Users 定义独立的用户资料协议，没有用户管理器、存储或认证实现：

```text
Contracts/Users
├── Models
│   ├── UserId
│   └── UserInfo
└── Operations
    ├── IUserManager
    └── Requests
        ├── CreateUserRequest
        └── UpdateUserRequest
```

UserInfo 包含 Id、DisplayName、CreatedAt 和 UpdatedAt，不保存全部 Session、Task 或 Memory。
IUserManager 提供 CreateAsync、GetAsync、ListAsync 和 UpdateAsync；Get 未找到返回 null，
Update 未找到显式报错，不自动创建用户。显示名称不可空白但允许重名，更新不改变 UserId。
List 第一版不分页，按 UpdatedAt 降序、ID Ordinal 升序返回可见资料；读取不更新资料时间。
当前名称更新为无条件替换，没有 Version 或 ExpectedVersion；完整行为约定见 Users 目录说明。

创建资料不等于登录或认证，UserId 不代表调用者权限。SessionInfo 与 CreateSessionRequest
已通过必需 UserId 建立单用户归属，创建会话时须验证用户存在和创建权限。
User 级记忆继续通过 UserId 归属关联，其构造校验不证明用户存在。
UserId 已移至 Contracts/Users/Models，Memory 引用同步迁移，原有 JSON Value 结构保持不变。
用户存储端口和具体实现后续定义，具体保存技术归 Infrastructure/Persistence。

---

## 6. Application / Host

Host 层负责用户如何操作 Agent 和多个长期 Session。

Host 不负责：

- 拼接模型 Context
- 管理 Memory 生命周期
- 执行 Tool
- 决定 ReAct 的下一步

### 6.1 `IAgentShell`

```csharp
public interface IAgentShell
{
    Task RunAsync(CancellationToken cancellationToken);
}
```

职责：

- 读取用户输入
- 识别 Shell 命令
- 将普通自然语言发送给当前 Session
- 展示 Session 增量事件
- 管理当前选中的 Session

### 6.2 `ICommandParser`

```csharp
public interface ICommandParser
{
    ShellCommand Parse(string input);
}
```

命令对象：

```csharp
public abstract record ShellCommand;

public sealed record ListSessionsCommand : ShellCommand;
public sealed record CreateSessionCommand(string? Name) : ShellCommand;
public sealed record SelectSessionCommand(SessionId SessionId) : ShellCommand;
public sealed record SendCommand(SessionId SessionId, string Input) : ShellCommand;
public sealed record TailCommand(SessionId SessionId) : ShellCommand;
public sealed record CancelCommand(SessionId SessionId) : ShellCommand;
public sealed record CloseSessionCommand(SessionId SessionId) : ShellCommand;
public sealed record LeaveSessionCommand : ShellCommand;
public sealed record ExitCommand : ShellCommand;
public sealed record UserInputCommand(string Input) : ShellCommand;
```

建议命令：

```text
sls
create incident-01
select incident-01
send incident-01 "继续调查日志"
tail -f incident-01
cancel incident-01
close incident-01
quit
exit
```

命令语义：

- `quit`：离开当前 Session 视图，返回主 Shell
- `exit`：退出整个程序
- `cancel`：取消当前任务，不删除 Session
- `close`：关闭并释放 Session

---

## 7. Session Manager

Session Manager 负责多个 Agent Session 的生命周期。

当前只定义契约，不实现管理器或存储。类型位于 Contracts/Sessions 的 Models 与 Operations 子目录，
创建请求位于 Operations/Requests；命名空间与目录对应，不为直接返回的会话数据创建额外响应包装。

### 7.1 `ISessionManager`

```csharp
public interface ISessionManager
{
    Task<SessionInfo> CreateAsync(
        CreateSessionRequest request,
        CancellationToken cancellationToken);

    Task<SessionInfo?> GetAsync(
        SessionId sessionId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionInfo>> ListAsync(
        CancellationToken cancellationToken);

    Task<SessionInfo> CloseAsync(
        SessionId sessionId,
        CancellationToken cancellationToken);
}
```

```csharp
public sealed record CreateSessionRequest(
    [property: JsonRequired] UserId UserId,
    string Name);
```

Name 必须非空白，可重名，不是唯一标识；Create 生成新的 ID 与时间并返回 Idle。
UserId 为必需归属，创建时验证已有用户及访问权限，不自动创建用户；
JSON 的 JsonRequired 标记来自 System.Text.Json.Serialization，拒绝缺少 UserId 的旧请求。
Get 不存在时返回 null；List 包含 Closed，按 LastActiveAt 降序、ID Ordinal 升序返回快照。
Close 只关闭 Idle，返回 Closed；已关闭时原样返回，Active 时显式报冲突，未知 ID 显式报不存在错误。
Get / List 不改变活动时间，关闭不取消任务也不删除历史。
任务提交、用户补充、取消和事件观察由后续任务 / Runtime 契约表达，不放进本接口。

### 7.2 Session 公共信息

```csharp
public sealed record SessionInfo(
    SessionId Id,
    [property: JsonRequired] UserId UserId,
    string Name,
    SessionStatus Status,
    TaskId? CurrentTaskId,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActiveAt);
```

```csharp
public enum SessionStatus
{
    Idle = 0,
    Active = 1,
    Closed = 2
}
```

SessionId 位于 Contracts/Sessions/Models，显式构造拒绝空白并支持 JSON 反序列化。
SessionInfo 是不可变 DTO，不保存取消源、执行 Task 或锁；本地执行句柄属于 Runtime/Orchestration。
CreatedAt 创建后不变，LastActiveAt 随实际输入或生命周期活动更新，不早于 CreatedAt 且不倒退。
SessionInfo 的 UserId 创建后不变；JSON 缺少该字段时显式失败，旧数据需明确迁移，不自动补默认或当前用户。
字段必需不等于用户有效，后续管理与恢复入口必须维护归属、权限、状态、任务关联和时间约束。
Runtime 查询会话后，通过 UserId 调用 IUserManager 获取资料，再按 User 级 MemoryOwner 召回记忆；
不将完整 UserInfo 或用户记忆复制进会话快照。
当前 ListAsync 仍列举可见会话，尚未定义按用户过滤的查询接口，未来存储可按 UserId 建索引。

### 7.3 单会话单未结束任务

- Idle / Closed 没有 CurrentTaskId，Active 必须关联一个有效 TaskId。
- 一个 Session 可以顺序承载多个 Task，但最多只有一个未结束任务；多个 Session 的并发独立处理。
- 等待模型、工具或用户都属于未结束，Session 仍为 Active。
- Active 时拒绝新任务，暂不排队；当前任务的补充与恢复不是另一个新任务。
- 当前任务完成、失败或确认取消后，清除关联并回到 Idle；Closed 不允许重新启动。
- 任务接收、占用和 Close 需由后续实现原子协调，不能依赖先 Get 再无条件启动。

Session 只表达粗粒度生命周期。Task 的详细状态由任务契约管理，展示层可关联查询并渲染：

`sls` 示例：

```text
NAME          STATUS            CURRENT ACTIVITY
incident-01   Active            Querying Kusto
code-review   Active            Need repository path
learning      Idle              -
```

### 7.4 Conversation 历史契约

对话记录不放进 SessionInfo，也不直接把本轮 ModelMessage 当成存储模型。
Contracts/Conversation 定义：

- MessageId：由可信调用方首次追加前生成，重试复用。
- ConversationMessage：Id、SessionId、可空 TaskId、Sequence、Content 和 CreatedAt。
- ConversationContent：通过 JSON role 区分 User、Assistant、Tool 内容。
- Assistant 内容保留文本、ToolCallRequest 和必要结束原因；Tool 内容复用 ToolObservation。
- IConversationManager：AppendAsync、GetAsync、ListBySessionAsync。

AppendAsync 返回服务端分配 Sequence 和 CreatedAt 的消息。
同 ID、归属和内容的重试返回原记录，不同载荷显式报冲突；去重、分配顺序和保存必须并发安全。
任务内交互应关联 TaskId，工具结果还须匹配此前同 Session、同 Task 中的调用。
只保存截断、过滤、拒绝或未知结束信息的历史也允许保留，但不能伪造为正常模型回答。

分页首次默认最近 20 条，页内 Sequence 升序，使用不透明 ContinuationToken 读取更早消息。
未知会话、非法页大小或游标显式报错；Closed 可读但不新增交互，已提交的相同重试可在授权后返回原记录。
Context 处理分页时须补齐工具调用关联或舍弃完整片段，不能将半套协议送入模型。
当前只有契约，去重、排序、权限、消息关联、管理器和存储均待实现。

---

## 8. Agent Runtime 与 Orchestrator

### 8.1 `IAgentRuntime`

`IAgentRuntime` 是后续 Host 调用 Runtime 的统一门面规划，复用已定义的 ITaskManager，
不再提供等待整个任务完成才返回的公开 ExecuteAsync。事件观察协议仍待实现。

```csharp
public interface IAgentRuntime : ITaskManager
{
    IAsyncEnumerable<AgentEvent> ObserveAsync(
        SessionId sessionId,
        CancellationToken cancellationToken);
}
```

### 8.2 `IAgentOrchestrator`

```csharp
public interface IAgentOrchestrator
{
    Task<AgentResult> RunAsync(
        AgentRequest request,
        CancellationToken cancellationToken);
}
```

Orchestrator 负责：

1. 加载已接受任务及其 Session State，复用提交阶段分配的 TaskId
2. 选择相关 Skill
3. 选择 Execution Strategy
4. 调用 Execution Engine
5. 保存 Checkpoint
6. 发布任务完成或失败事件

Orchestrator 不负责：

- 拼接 Context
- 直接执行 Tool
- 实现 Memory 检索
- 解析具体模型 Provider 响应

否则 Orchestrator 容易演变为 God Object。

---

## 9. Execution Engine

Execution Engine 解决：

> 当前任务采用哪一种执行模式，以及该模式如何推进任务。

### 9.1 执行模式

```csharp
public enum ExecutionStrategyType
{
    Direct,
    PromptChain,
    ReAct
}
```

### 9.2 `IExecutionEngine`

```csharp
public interface IExecutionEngine
{
    Task<ExecutionStrategyType> SelectStrategyAsync(
        AgentRequest request,
        AgentState state,
        CancellationToken cancellationToken);

    Task<AgentResult> ExecuteAsync(
        ExecutionStrategyType strategy,
        AgentRequest request,
        CancellationToken cancellationToken);
}
```

### 9.3 `IExecutionStrategy`

```csharp
public interface IExecutionStrategy
{
    ExecutionStrategyType Type { get; }

    Task<AgentResult> ExecuteAsync(
        ExecutionContext context,
        CancellationToken cancellationToken);
}
```

```csharp
public sealed record ExecutionContext(
    AgentRequest Request,
    AgentState State,
    SessionId SessionId,
    TaskId TaskId);
```

注意：`ExecutionContext` 是执行引擎的数据对象，不是 LLM 的 Model Context。

### 9.4 Direct Executor

执行流程：

```text
User Input → Build Context → Invoke Model → Final Answer
```

### 9.5 Prompt Chain Executor

第一版只实现顺序 Chain：

```text
Step 1 → Step 2 → Step 3 → Validate → Final
```

```csharp
public interface IChainDefinition
{
    string Name { get; }
    IReadOnlyList<ChainStepDefinition> Steps { get; }
}
```

```csharp
public sealed record ChainStepDefinition(
    string Id,
    string Name,
    string Instruction,
    string? OutputSchema = null);
```

第一版不实现 DAG、动态分支或并行 Chain。

### 9.6 ReAct Executor

核心循环：

```text
Build Context
→ Invoke Model
→ Tool Call
→ Execute Tool
→ Observation
→ Update State
→ Build Next Context
→ Invoke Model
→ Final Answer
```

基本接口：

```csharp
public interface IReactExecutor : IExecutionStrategy
{
    int MaxIterations { get; }
}
```

必须提供：

- `MaxIterations`
- 最大 Tool Call 数量
- Cancellation Token
- Tool Timeout
- 无效模型输出处理
- Stop Condition

---

## 10. Language Model Runtime

LanguageModelRuntime 封装一次 LLM 调用，不处理业务控制流。
当前已定义 Contracts/LanguageModels 的类型化消息与非流式调用契约，尚未实现 Provider 客户端。
流式、多模态和 Embedding 后续独立设计，不把规划接口当成已实现能力。

### 10.1 `ILanguageModelRuntime`

```csharp
public interface ILanguageModelRuntime
{
    Task<ModelResponse> InvokeAsync(
        ModelInvocation invocation,
        CancellationToken cancellationToken);
}
```

### 10.2 模型调用对象

```csharp
public sealed record ModelInvocation(
    [property: JsonRequired] IReadOnlyList<ModelMessage> Messages,
    [property: JsonRequired] IReadOnlyList<ToolDefinition> Tools,
    ModelOptions? Options = null);
```

```csharp
public sealed record ModelOptions(
    string? ModelName = null,
    double? Temperature = null,
    int? MaxOutputTokens = null);
```

Messages 为按顺序排列的非空消息，Tools 非 null，空列表表示不允许提出工具调用，
不再额外定义 EnableToolCalling。JsonRequired 来自 System.Text.Json.Serialization，
只要求字段存在；选项、消息关联和模型能力仍需后续实现校验。
未指定参数时使用明确配置或 Provider 默认值，不支持的参数显式报错，不能静默忽略。

### 10.3 消息协议

ModelMessage 是抽象 record，通过 JSON role 判别四种具体消息：

| 类型                  | 内容                                                                  |
| --------------------- | --------------------------------------------------------------------- |
| ModelSystemMessage    | Content 系统规则。                                                    |
| ModelUserMessage      | Content 用户文本。                                                    |
| ModelAssistantMessage | 可空 Text 与非 null ToolCalls，支持文本和多个调用同时返回。           |
| ModelToolMessage      | 复用 ToolObservation，保留 CallId、工具名、状态、错误与必要输出引用。 |

以基类序列化时 role 为 system、user、assistant、tool，未知角色显式报错，不默认降级为普通文本。
下一轮请求必须正确对应 Assistant 调用与 Tool 结果，不能用 User 文本代替工具角色或丢弃调用 ID。
Provider 适配器负责将统一消息映射为原生协议，不能直接把内部 Observation 当成 SDK 消息发送。

### 10.4 模型返回结果

```csharp
public sealed record ModelResponse(
    string? Text,
    [property: JsonRequired] IReadOnlyList<ToolCallRequest> ToolCalls,
    [property: JsonRequired] ModelFinishReason FinishReason,
    ModelUsage? Usage = null,
    string? ProviderFinishReason = null);
```

ToolCallRequest 已定义于 Contracts/Tools/Operations/Requests，包含 Id、ToolName 与 ArgumentsJson，
模型返回协议复用该类型，不再重复定义。Runtime 从可信任务上下文补充 SessionId 与 TaskId，
构成 InvokeToolRequest 后执行工具；模型不能自行决定授权归属。

```csharp
public sealed record ModelUsage(
    int? InputTokens,
    int? OutputTokens);
```

ModelFinishReason 为 Stop=0、ToolCalls=1、Length=2、ContentFiltered=3、Refused=4、Unknown=5。
Text 只是本次模型输出，不是整个 Task 的 FinalAnswer；Stop 也不自动结束任务。
只有完整且允许的工具调用才返回 ToolCalls 原因；截断、过滤、拒绝和未知原因不返回可执行工具调用。
实际调用错误和无效输出显式报错，Unknown 仅用于结束原因未报告或无法映射，不掩盖网络异常。
未知用量使用 null，0 只表示实际报告为零，不把估算冒充实际消耗；
ProviderFinishReason 保存已报告的原始原因标识，不保存整份原始响应。

### 10.5 `IModelClient`（后续内部规划）

以下客户端与 Provider 专用请求 / 响应仍是内部适配规划，不属于本次已生成的公共契约。

```csharp
public interface IModelClient
{
    Task<ModelProviderResponse> CompleteAsync(
        ModelProviderRequest request,
        CancellationToken cancellationToken);
}
```

建议实现：

```text
AzureOpenAIModelClient
OpenAIModelClient
MockModelClient
```

`MockModelClient` 用于稳定测试 Tool Calling 和 ReAct 循环。

---

## 11. Context Manager

Context Manager 负责：

> 当前一轮 Model Invocation 应该让 LLM 看到什么。

Context Manager 读取 State、Memory、Skill、Conversation 和 Tool Observations，但不管理这些数据的生命周期。

### 11.1 `IContextManager`

```csharp
public interface IContextManager
{
    Task<ModelContext> BuildAsync(
        ContextBuildRequest request,
        CancellationToken cancellationToken);

    Task<ContextPreview> PreviewAsync(
        ContextBuildRequest request,
        CancellationToken cancellationToken);
}
```

`PreviewAsync` 用于查看最终送入 LLM 的 Context，而不真正调用模型。

### 11.2 Context 构建请求

```csharp
public sealed record ContextBuildRequest(
    SessionId SessionId,
    TaskId TaskId,
    string CurrentInput,
    int? TokenBudget = null);
```

### 11.3 Model Context

```csharp
public sealed record ModelContext(
    IReadOnlyList<ModelMessage> Messages,
    IReadOnlyList<ToolDefinition> Tools,
    ContextDiagnostics Diagnostics);
```

ModelContext 与 ContextDiagnostics 仍为内部 Context 规划，消息直接复用已定义的 ModelMessage。
模型调用时只传 Messages、Tools 和 Options，组装 ModelInvocation，不将预览诊断混入调用协议。
原先 Role + Content 的平铺消息草图已由类型化消息替代，避免丢失 Assistant 工具调用和 Tool 结果关联。

### 11.4 Context 构建流水线

```text
State
Memory
Skill
Conversation
Tool Observations
Retrieved Knowledge
        ↓
Recall / Collect
        ↓
Deduplicate
        ↓
Filter / Rank
        ↓
Token Budget
        ↓
Compress
        ↓
Assemble Model Context
```

### 11.5 Context 诊断信息

```csharp
public sealed record ContextDiagnostics(
    int CandidateCount,
    int SelectedCount,
    int EstimatedTokens,
    IReadOnlyList<ContextSelectionRecord> Selections);
```

```csharp
public sealed record ContextSelectionRecord(
    string SourceType,
    string SourceId,
    double Score,
    int EstimatedTokens,
    bool Selected,
    string? RejectionReason);
```

该设计类似推荐系统的 Recall / Rank Debug View，可以帮助解释某条信息为什么进入或没有进入 Context。

---

## 12. State Manager

State Manager 管理当前任务的可恢复运行状态。

State 回答：

> 当前任务执行到哪里，Runtime 应该从哪里继续。

### 12.1 `IStateManager`

```csharp
public interface IStateManager
{
    Task<AgentState> StartTaskAsync(
        AgentRequest request,
        CancellationToken cancellationToken);

    Task<AgentState> GetAsync(
        TaskId taskId,
        CancellationToken cancellationToken);

    Task<AgentState> UpdateAsync(
        TaskId taskId,
        Func<AgentState, AgentState> updater,
        CancellationToken cancellationToken);

    Task SetStatusAsync(
        TaskId taskId,
        TaskStatus status,
        CancellationToken cancellationToken);

    Task RecordObservationAsync(
        TaskId taskId,
        ToolObservation observation,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        TaskId taskId,
        string finalAnswer,
        CancellationToken cancellationToken);
}
```

本节 StateManager 仍是内部能力规划，不是本次已定义的公开 Task 接口。
StartTaskAsync 初始化已接受 TaskId 对应的执行进度，不创建另一项任务；
TaskInfo 的状态与版本必须与该任务的内部状态协调，不能维护相互矛盾的两份事实。

### 12.2 Agent State

```csharp
public sealed record AgentState(
    SessionId SessionId,
    TaskId TaskId,
    string Goal,
    TaskStatus Status,
    ExecutionStrategyType Strategy,
    int Iteration,
    string? CurrentStep,
    string? ActiveSkillId,
    string? PendingToolCallId,
    IReadOnlyList<ToolObservation> RecentObservations,
    long Version);
```

生命周期复用 Contracts/Tasks/Models 的 TaskStatus，不另定义一套重复枚举。

### 12.3 State Transition

```csharp
public sealed record StateTransition(
    TaskId TaskId,
    TaskStatus From,
    TaskStatus To,
    string Reason,
    DateTimeOffset Timestamp);
```

需要阻止非法转换，例如：

```text
Completed → Running
Cancelled → WaitingForTool
Cancelling → Running
```

State 与流处理中的 State / Checkpoint 对应：

```text
Current State → Snapshot → Checkpoint → Crash → Restore → Resume
```

---

## 13. 工作记忆与长期记忆

Memory 模块管理语义信息，不管理执行位置，也不自行实现完整搜索引擎。

Memory 回答：

> Agent 已经知道什么，以及什么信息值得在以后继续使用。

### 13.1 两种记忆访问模式

当前 `HanyingAgent` 已定义两组 Memory 契约，尚未实现 Manager、Store 或索引构建流程。
原有统一记忆接口已替换为 `IWorkingMemoryManager` 和 `ILongTermMemoryManager`。
`SessionId`、`TaskId`、`UserId` 分别位于 `Contracts/Sessions/Models`、`Contracts/Tasks/Models` 和 `Contracts/Users/Models`，
Memory 契约分为 Models 和 Operations 两部分，操作输入与结果放在 Operations 的子目录：

```text
Contracts/Memory
├── Models
│   ├── MemoryItem.cs
│   ├── MemoryId.cs
│   ├── MemoryOwner.cs
│   ├── MemoryScope.cs
│   ├── MemoryType.cs
│   └── MemoryStatus.cs
└── Operations
    ├── IMemoryWriter.cs
    ├── IWorkingMemoryManager.cs
    ├── ILongTermMemoryManager.cs
    ├── Requests
    │   ├── MemoryWriteRequest.cs
    │   ├── MemoryUpdateRequest.cs
    │   ├── MemoryReadRequest.cs
    │   └── MemorySearchRequest.cs
    └── Responses
        └── MemorySearchResult.cs
```

命名空间与子目录对应：`HanyingAgent.Contracts.Memory.Models`、`HanyingAgent.Contracts.Memory.Operations`、
`HanyingAgent.Contracts.Memory.Operations.Requests` 和 `HanyingAgent.Contracts.Memory.Operations.Responses`。
Models 描述记忆本身；Operations 集中组织能力接口及其调用协议；
Requests 描述操作输入，Responses 描述操作特有的结果。这些 DTO 是公共契约，不是某个接口的私有类型。
MemoryId 即使作为参数、MemoryItem 即使作为返回值，也仍属于 Models。
操作输入统一使用 Request 后缀：MemoryWriteRequest、MemoryUpdateRequest、MemoryReadRequest、MemorySearchRequest。
类型命名和目录调整不改变请求字段、默认值或操作行为。
以下示例省略命名空间，调用方按需要导入对应子命名空间。

| 维度         | 工作记忆（短期）                     | 长期记忆                                 |
| ------------ | ------------------------------------ | ---------------------------------------- |
| 主要内容     | 当前任务发现、会话背景、近期摘要     | 长期偏好、可复用事实与经验               |
| 默认归属     | Task / Session                       | User / Global                            |
| 读取方式     | 按 Owner 直接读取最近有效项          | 按 Owner 过滤后，根据 Query 检索         |
| 数据结构     | 可用 KV、内存字典或数据库            | 权威存储 + 可重建的派生索引              |
| 写入可见性   | 成功写入后，后续读取应可见           | 权威存储写入成功；搜索可暂时不可见       |
| 初期实现重点 | 归属隔离、读写、有效期、Context 注入 | 明确接口和索引边界，后续复用成熟检索组件 |

Task / Session / User / Global 表达归属，不表达保存期限或存储技术。
表中的归属映射是默认使用策略，不是两个接口的硬性 Scope 限制；两者都接受合法的 MemoryOwner。
例如 Session 摘要可以长期保存，User 的临时偏好也可以放入工作记忆。
工作记忆并不等于不持久化，可按需要持久化以支持重启恢复。

### 13.2 模块能力接口

三个共同的变更操作提取到 IMemoryWriter。它只共享方法契约，不共享存储实现，
不承诺统一的搜索可见性；具体一致性语义由工作记忆和长期记忆接口分别约定。

```csharp
public interface IMemoryWriter
{
    Task<MemoryItem> WriteAsync(
        MemoryWriteRequest request,
        CancellationToken cancellationToken);

    Task<MemoryItem> UpdateAsync(
        MemoryId memoryId,
        MemoryUpdateRequest update,
        CancellationToken cancellationToken);

    Task ForgetAsync(
        MemoryId memoryId,
        CancellationToken cancellationToken);
}
```

工作记忆继承变更能力，独有的读取操作不接受 Query、不计算相关性分数：

```csharp
public interface IWorkingMemoryManager : IMemoryWriter
{
    Task<IReadOnlyList<MemoryItem>> ListRecentAsync(
        MemoryReadRequest request,
        CancellationToken cancellationToken);
}
```

长期记忆继承变更能力，独有的操作提供相关性检索：

```csharp
public interface ILongTermMemoryManager : IMemoryWriter
{
    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        MemorySearchRequest query,
        CancellationToken cancellationToken);
}
```

需要读取能力的调用方依赖具体的 Manager 接口；只需变更记忆的流程可以依赖 IMemoryWriter，
但仍需在组合入口选择工作记忆或长期记忆的具体实例，不能将两个存储视为同一个目标。
不要为 IMemoryWriter 配置一个含糊的默认实现，以免把记忆写入错误的来源。

契约行为约定（由后续 Manager 实现并验证）：

- 两种 Manager 共用 MemoryItem、MemoryOwner、MemoryWriteRequest 和 MemoryUpdateRequest，不复制相同数据结构。
- Write 生成新的 ID 与时间戳，初始状态为 Active；Update 返回更新后的完整记忆。
  ID 不应在两个召回源间冲突，以便 Context 按 ID 去重。
- Content、Source 和长期 Query 不允许为空白；Confidence、Importance 必须为有限数且在 `[0, 1]` 内；
  Limit、TopK 必须大于零。默认 Limit 为 20，默认 TopK 为 10。
- MemoryOwner 校验归属形状：Task 仅提供 SessionId 和 TaskId；Session 仅提供 SessionId；
  User 仅提供 UserId；Global 不提供归属 ID。缺失必需 ID、额外 ID、默认 ID 和未知 Scope 在构造时显式报错。
  Manager 还必须验证 Task 实际属于指定 Session。其他模块入口也必须拒绝强类型 ID 的默认值。
- User 记忆供同一用户跨 Session 复用。Global 限定为当前 HanyingAgent 实例内共享，不支持多租户；
  它不是自动公开用户数据的入口。归属数据不等于访问授权，后续 Manager 必须验证访问权限。
- ListRecent 和 Search 都仅返回与 Owner 完全匹配、状态为 Active / Confirmed 且尚未过期的项，
  并应用可选 Type 过滤。不同 Scope 不自动继承，需要多个归属时分别调用，由 Context 合并。
- ListRecent 按 UpdatedAt 降序返回最多 Limit 项；更新时间相同时按 Id.Value 的 Ordinal 升序打破平局。
  它是有数量上限的直接读取，不是全量枚举，也不是关键词搜索。
- Search 按相关性 Score 降序返回最多 TopK 项。分数由检索实现定义，不是可信度概率，
  不承诺跨检索后端可比；不为工作记忆伪造 Score。
- 工作记忆的 Write / Update / Forget 成功后，后续读取必须反映对应变更。
- 长期记忆的 Write / Update / Forget 成功表示权威存储已完成变更，不保证搜索索引立即刷新；
  检索返回前必须根据权威数据确认归属、访问权限、状态和有效期，剔除已删除或失效的旧索引项。
  返回数据须来自当前权威记录，不能将旧索引中的内容直接当作最新记忆。
- MemoryUpdateRequest 是完整替换可修改字段，不是局部 Patch；ExpiresAt 为 null 表示清除有效期。
  ID、Owner、Type、CreatedAt 不由 Update 修改。
- 移除原有 Promote 操作：工作记忆转长期记忆通常需要选择、提取、压缩、去重与授权，
  不等于修改 Scope。后续整合流程调用长期 Write 创建新记忆，并通过 Source 记录原记忆来源；
  不自动删除工作记忆，不自动向 Global 发布用户信息。本次不实现该整合流程。
- Forget 删除记忆；不存在的 ID、非法输入和无效操作必须显式报错，不能返回成功形状的默认结果。
- 这些是业务契约，不是 HTTP API；远程适配器负责传输映射，服务端仍需验证输入与访问权限。

### 13.3 共用的记忆数据与归属

```csharp
public sealed record MemoryItem(
    MemoryId Id,
    MemoryOwner Owner,
    MemoryType Type,
    string Content,
    string Source,
    double Confidence,
    double Importance,
    MemoryStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ExpiresAt);
```

```csharp
public enum MemoryScope
{
    Task = 0,
    Session = 1,
    User = 2,
    Global = 3
}
```

`MemoryOwner` 是不可变归属对象，包含 `Scope` 和可空的 `SessionId`、`TaskId`、`UserId`，
构造时按 Scope 校验归属形状。原有 Task / Session 构造调用保持兼容：

```csharp
var taskOwner = new MemoryOwner(MemoryScope.Task, sessionId, taskId);
var sessionOwner = new MemoryOwner(MemoryScope.Session, sessionId);
var userOwner = new MemoryOwner(MemoryScope.User, userId: new UserId("user-01"));
var globalOwner = new MemoryOwner(MemoryScope.Global);
```

```csharp
public enum MemoryType
{
    Fact,
    Preference,
    Decision,
    Experience,
    Hypothesis,
    Summary
}
```

```csharp
public enum MemoryStatus
{
    Active,
    Confirmed,
    Superseded,
    Contradicted,
    Expired
}
```

### 13.4 直接读取与相关性检索请求

工作记忆的读取请求不包含查询文本：

```csharp
public sealed record MemoryReadRequest(
    MemoryOwner Owner,
    MemoryType? Type = null,
    int Limit = 20);
```

长期记忆的 Query 是自然语言检索文本，不是 SQL，也不要求调用方手工分词：

```csharp
public sealed record MemorySearchRequest(
    string Query,
    MemoryOwner Owner,
    MemoryType? Type = null,
    int TopK = 10);
```

写入、更新和检索结果：

```csharp
public sealed record MemoryWriteRequest(
    MemoryOwner Owner,
    MemoryType Type,
    string Content,
    string Source,
    double Confidence,
    double Importance,
    DateTimeOffset? ExpiresAt = null);

public sealed record MemoryUpdateRequest(
    string Content,
    string Source,
    double Confidence,
    double Importance,
    MemoryStatus Status,
    DateTimeOffset? ExpiresAt);

public sealed record MemorySearchResult(
    MemoryItem Item,
    double Score);
```

Context 调用示例（当前仅有契约，示例不是已实现逻辑）：

```csharp
var workingMemories = await workingMemory.ListRecentAsync(
    new MemoryReadRequest(
        new MemoryOwner(MemoryScope.Session, sessionId),
        Limit: 20),
    cancellationToken);

var longTermMemories = await longTermMemory.SearchAsync(
    new MemorySearchRequest(
        Query: currentTaskGoal,
        Owner: new MemoryOwner(MemoryScope.User, userId: userId),
        TopK: 5),
    cancellationToken);
```

### 13.5 存储、离线索引与两个召回源

```text
工作记忆：
Write / Update / Forget → Working Store
ListRecent(owner)       → Working Store → 最近有效记忆

长期记忆写入：
Write / Update / Forget → Long-term Store（权威数据）
                                      ↓
                       可靠的变更捕获或离线扫描
                                      ↓
                     整理 / 分词 / 可选 Embedding
                                      ↓
                        可重建的 Search Index

长期记忆查询：
Search(owner, query) → Search Index → 候选 ID / Score
                                  → 校验并读取权威数据
                                  → 相关长期记忆
```

离线索引是可选的初期实现方式，后续可替换为增量更新；Context 不依赖索引更新机制。
删除、更新、失效也必须传播到索引，不能只处理新增项。索引扫描或变更消费需可重试、可恢复，
避免漏更新；引入生产级异步链路时可采用 Outbox / CDC，但本次不搭建该基础设施。
分词产生的是词项，不等于业务标签；倒排索引主要优化检索，不承诺总存储量更小。
全文检索、向量检索或混合检索由后端组件实现，不在 Runtime 中自研搜索引擎，
也不把固定关键词权重公式写进公共契约。

Context Manager 将两种能力作为独立来源：

```text
Context Manager
  ├─ WorkingMemory.ListRecentAsync：当前任务 / 会话的近期信息
  └─ LongTermMemory.SearchAsync：相关偏好、事实和经验
                ↓
      合并 / 去重 / 优先级 / Token Budget
                ↓
            Model Context
```

长期索引可能滞后；当前执行刚产生的发现应从工作记忆或 State 的 Observation 直接进入下一轮 Context。
对刚变更且要求立即生效的用户偏好，可通过工作记忆保留覆盖项；不要依赖尚未刷新的长期索引。
Context 按 ID、来源和内容处理重复或替代信息，不比较工作记忆与长期搜索的原始 Score。
Context 负责按任务需求生成检索文本；分词和匹配留在长期检索实现中。

### 13.6 State 与 Memory 的边界

- State：`Where am I?`
- Memory：`What do I know?`
- Context：`What should the model see now?`
- Execution：`How do I proceed?`

同一个 Tool Observation 可以同时产生：

```text
State Update:
CurrentStep = AnalyzeKustoResult

Memory Update:
Only SU03 has elevated failure rate.
```

---

## 14. Skill Manager

Skill Manager 管理 LLM 可读取的 SOP、Instructions 和参考资料。

### 14.1 `ISkillManager`

```csharp
public interface ISkillManager
{
    Task<IReadOnlyList<SkillMetadata>> ListAsync(
        CancellationToken cancellationToken);

    Task<SkillDefinition?> SelectAsync(
        string userInput,
        AgentState state,
        CancellationToken cancellationToken);

    Task<SkillDefinition> LoadAsync(
        string skillId,
        CancellationToken cancellationToken);

    Task<string?> LoadReferenceAsync(
        string skillId,
        string referenceName,
        CancellationToken cancellationToken);
}
```

### 14.2 Skill 定义

```csharp
public sealed record SkillMetadata(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string> TriggerHints);
```

```csharp
public sealed record SkillDefinition(
    SkillMetadata Metadata,
    string Instructions,
    IReadOnlyList<SkillReference> References,
    IReadOnlyList<string> AllowedTools);
```

```csharp
public sealed record SkillReference(
    string Name,
    string Path,
    string Description);
```

V1 使用关键词选择 Skill：

```text
incident / error / production
→ incident-investigation
```

后续再增加 LLM-based Skill Selection。

---

## 15. Tool Manager

Tool Manager 负责工具注册、Schema 暴露、参数校验、执行和结果标准化。
当前已定义 Contracts/Tools 公共协议，尚未实现工具管理器、注册表或具体工具。
注册是本地启动组合职责，不放进公共调用接口；调用方等待本次调用并直接接收 Observation。

### 15.1 `IAgentTool`

以下是后续具体工具的内部实现协议规划，不是远程公共接口；
ToolExecutionContext 尚未定义，原始 ToolResult 已作为可复用 DTO 定义。

```csharp
public interface IAgentTool
{
    ToolDefinition Definition { get; }

    Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken);
}
```

### 15.2 Tool Definition

```csharp
public sealed record ToolDefinition(
    string Name,
    string Description,
    string InputJsonSchema,
    bool HasSideEffects = false,
    TimeSpan? Timeout = null);
```

### 15.3 `IToolManager`

```csharp
public interface IToolManager
{
    Task<IReadOnlyList<ToolDefinition>> ListDefinitionsAsync(
        ListToolDefinitionsRequest request,
        CancellationToken cancellationToken);

    Task<ToolObservation> InvokeAsync(
        InvokeToolRequest request,
        CancellationToken cancellationToken);
}
```

ListToolDefinitionsRequest 包含 SessionId、TaskId 和可空 AllowedTools。
null 表示全部已授权工具，空列表表示无工具，非空集合只缩小权限范围；
结果按工具名 Ordinal 升序排列。InvokeToolRequest 包含 SessionId、TaskId 和 ToolCallRequest。
管理器必须验证归属、权限、Schema 和工具名称，不能因为模型提出调用就直接执行。
同一个 Call ID 不同参数或归属报冲突，重复请求不能盲目执行；Call ID 不自动保证幂等。

### 15.4 Tool Result

```csharp
public enum ToolCallStatus
{
    Succeeded = 0,
    Failed = 1,
    Cancelled = 2,
    OutcomeUnknown = 3
}

public sealed record ToolResult(
    ToolCallStatus Status,
    string? Content,
    string? Error = null);
```

```csharp
public sealed record ToolObservation(
    ToolCallId CallId,
    string ToolName,
    ToolCallStatus Status,
    string Summary,
    string? Error,
    string? ArtifactReference,
    DateTimeOffset Timestamp,
    TimeSpan Duration);
```

超大 Tool Result 不应直接写入 State：
ToolObservation 由原始 ToolResult 与调用上下文生成，保留状态和错误，只将受控摘要与引用带入执行循环。
Succeeded 无 Error，Failed 与 OutcomeUnknown 必须有明确 Error；
超时、响应丢失或取消等待不能证明远端没有执行，无法确认时不能伪装成确定失败或成功。
Failed / Cancelled 也不承诺已回滚副作用，不能据此盲目重试。
调用 CancellationToken 控制本次调用或等待，不等于取消所属 Task。

```text
Raw Result → Artifact Store

State:
ArtifactReference = artifact://kusto/result-123
Summary = failure concentrated in SU03
```

---

## 16. Persistence

Persistence 负责 Session、State 和 Memory 的持久化机制。

### 16.1 `IPersistenceManager`

```csharp
public interface IPersistenceManager
{
    Task<CheckpointId> CheckpointAsync(
        SessionId sessionId,
        CancellationToken cancellationToken);

    Task<AgentSessionSnapshot?> RestoreAsync(
        SessionId sessionId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionInfo>> ListSessionsAsync(
        CancellationToken cancellationToken);

    Task DeleteSessionAsync(
        SessionId sessionId,
        CancellationToken cancellationToken);
}
```

### 16.2 `ICheckpointStore`

```csharp
public interface ICheckpointStore
{
    Task SaveAsync(
        AgentSessionSnapshot snapshot,
        CancellationToken cancellationToken);

    Task<AgentSessionSnapshot?> LoadLatestAsync(
        SessionId sessionId,
        CancellationToken cancellationToken);
}
```

```csharp
public sealed record AgentSessionSnapshot(
    CheckpointId CheckpointId,
    SessionId SessionId,
    SessionInfo Session,
    IReadOnlyList<AgentState> Tasks,
    IReadOnlyList<MemoryItem> WorkingMemories,
    DateTimeOffset CreatedAt);
```

WorkingMemories 包含恢复该 Session 及其 Task 所需的工作记忆，不只是 Session Scope 的记忆。
长期记忆由独立权威存储管理，不随每个 Session Checkpoint 复制整个 User / Global 数据集或搜索索引。

建议演进路径：

```text
V1: JSON File
V2: SQLite
V3: 按需要再考虑 Redis / SQL / Cosmos DB
```

---

## 17. Observability

学习型 Agent 需要能够观察每一轮模型调用、Tool Call、State Update 和 Context Selection。

### 17.1 Agent Event

```csharp
public abstract record AgentEvent(
    SessionId SessionId,
    TaskId? TaskId,
    DateTimeOffset Timestamp);
```

建议事件：

```text
TaskStarted
ContextBuilt
ModelInvocationStarted
ModelInvocationCompleted
ToolInvocationStarted
ToolInvocationCompleted
StateChanged
MemoryWritten
CheckpointCreated
TaskCompleted
TaskFailed
```

### 17.2 `IAgentEventHub`

```csharp
public interface IAgentEventHub
{
    ValueTask PublishAsync(
        AgentEvent agentEvent,
        CancellationToken cancellationToken);

    IAsyncEnumerable<AgentEvent> SubscribeAsync(
        SessionId sessionId,
        CancellationToken cancellationToken);
}
```

`tail -f session-01` 的实现：

```csharp
await foreach (
    var evt in eventHub.SubscribeAsync(
        sessionId,
        cancellationToken))
{
    renderer.Render(evt);
}
```

---

## 18. 模块依赖关系

建议依赖方向：

```text
AgentShell
  → SessionManager
  → AgentRuntime

AgentRuntime
  → Orchestrator

Orchestrator
  → StateManager
  → SkillManager
  → ExecutionEngine
  → PersistenceManager
  → AgentEventHub

ExecutionEngine
  → ExecutionStrategy

ExecutionStrategy
  → ContextManager
  → LanguageModelRuntime
  → ToolManager
  → StateManager
  → AgentEventHub

ContextManager
  → StateManager
  → IWorkingMemoryManager
  → ILongTermMemoryManager
  → SkillManager
  → ToolManager

IWorkingMemoryManager
  → WorkingMemoryStore

ILongTermMemoryManager
  → LongTermMemoryStore
  → LongTermMemoryRetriever

OfflineIndexer
  → LongTermMemoryStore
  → SearchIndex

StateManager
  → StateStore

PersistenceManager
  → CheckpointStore
```

应避免的反向依赖：

```text
LanguageModelRuntime → ContextManager
LanguageModelRuntime → StateManager
ToolManager → LanguageModelRuntime
WorkingMemoryManager → ContextManager
LongTermMemoryManager → ContextManager
SkillManager → ExecutionEngine
```

原则：

> 下层能力模块不应该知道上层编排逻辑。

---

## 19. ReAct 请求完整调用流程

```text
AgentShell
  ↓
ITaskManager.SubmitAsync
  ↓
原子创建 Task 并占用 Session
  ↓
返回 TaskInfo（不等待执行完成）

后台执行：
  ↓
Orchestrator.RunAsync
  ↓
StateManager.StartTaskAsync
  ↓
SkillManager.SelectAsync
  ↓
ExecutionEngine.SelectStrategyAsync
  ↓
ReactExecutor.ExecuteAsync
  ↓
ContextManager.BuildAsync
  ├─ StateManager.GetAsync
  ├─ WorkingMemory.ListRecentAsync
  ├─ LongTermMemory.SearchAsync
  ├─ SkillManager.LoadAsync
  └─ ToolManager.ListDefinitionsAsync
  ↓
LanguageModelRuntime.InvokeAsync
  ↓
LLM returns ToolCall
  ↓
ToolManager.InvokeAsync
  ↓
ToolObservation
  ↓
StateManager.RecordObservationAsync
  ↓
PersistenceManager.CheckpointAsync
  ↓
ContextManager.BuildAsync
  ↓
LanguageModelRuntime.InvokeAsync
  ↓
LLM returns FinalAnswer
  ↓
StateManager.CompleteAsync
  ↓
保存终态 TaskResult 并释放 Session 占用

调用方：
ITaskManager.GetResultAsync → TaskResult
```

---

## 20. MVP 实现顺序

### Phase 1：最小 Runtime

实现：

- `IAgentRuntime`
- `IAgentOrchestrator`
- `ILanguageModelRuntime`
- `IContextManager`
- `IStateManager`
- `DirectExecutor`

目标：

```text
User → Context → LLM → Answer
```

### Phase 2：Tool Calling

实现：

- `IAgentTool`
- `IToolManager`
- `ToolRegistry`
- `get_current_time`
- `calculator`

目标：

```text
LLM → Tool Call → Tool → Observation → LLM
```

### Phase 3：ReAct

实现：

- `ReactExecutor`
- Max Iterations
- Tool Timeout
- Stop Condition
- Observation 写入 State

目标：

```text
Reason → Action → Observation → Reason → Final
```

### Phase 4：Persistence

实现：

- JSON Checkpoint
- Resume
- Session 恢复

验证场景：

1. Agent 执行到 Tool Call 后生成 Checkpoint
2. 强制终止程序
3. 重启程序
4. 恢复 Session
5. 继续执行

### Phase 5：Memory

实现：

- `MemoryItem`
- `IWorkingMemoryManager`：按 Task / Session 直接读取近期记忆
- `ILongTermMemoryManager`：为 User / Global 等长期信息保留检索端口
- 工作记忆的内存 / KV 实现、有效期和 Context 注入
- Context 合并工作记忆与长期检索结果

后续再增加长期权威存储、离线或增量索引适配器，以及短期到长期的记忆整合流程。
第一版不自研中文分词或搜索引擎，不把索引构建塞进 Agent 执行循环。

### Phase 6：Skill

实现：

- 本地 Skill 目录
- `SKILL.md`
- Metadata 选择
- Context 注入

### Phase 7：Prompt Chain

实现固定 Chain：

```text
Extract Facts
→ Build Timeline
→ Analyze
→ Generate Report
→ Validate
```

用固定 Chain 和 ReAct 做控制流对比。

### Phase 8：Multi-Session Shell

实现：

- `sls`
- `create`
- `select`
- `send`
- `tail -f`
- `cancel`
- `close`
- `quit`
- `exit`

---

## 21. V1 最小接口集合

核心接口按能力划分如下（其中长期记忆先定义契约，后实现检索适配器）：

```csharp
IAgentRuntime
IAgentOrchestrator
ISessionManager
ITaskManager
IExecutionEngine
IExecutionStrategy
ILanguageModelRuntime
IContextManager
IStateManager
IMemoryWriter
IWorkingMemoryManager
ILongTermMemoryManager
IToolManager
IUserManager
```

下一阶段再增加：

```csharp
ISkillManager
IPersistenceManager
IAgentEventHub
```

---

## 22. 模块职责速查表

| 模块             | 核心问题                   | 主要输入                    | 主要输出                    |
| ---------------- | -------------------------- | --------------------------- | --------------------------- |
| Agent Shell      | 用户怎样操作 Agent         | 命令、自然语言              | Session 操作、Agent Request |
| Session Manager  | 多 Session 怎样管理        | Session 命令                | Session、状态、事件流       |
| Orchestrator     | 一个 Task 怎样被协调       | Agent Request               | Task 生命周期结果           |
| Execution Engine | 使用什么模式执行           | Request、State              | Strategy 执行结果           |
| Model Runtime    | 怎样调用模型               | Model Context、Tools        | Text 或 Tool Call           |
| Context Manager  | 本轮让模型看到什么         | Input、State、Memory、Skill | Model Context               |
| State Manager    | 当前执行到哪里             | Transition、Observation     | Agent State                 |
| Working Memory   | 当前工作上下文中知道什么   | Owner、读取数量、写入内容   | 最近有效 Memory Items       |
| Long-term Memory | 哪些长期信息与当前需求相关 | Owner、Query、写入内容      | 带相关性分数的 Memory Items |
| Skill Manager    | 这类任务应该怎样做         | User Input、State           | Skill Instructions          |
| Tool Manager     | Agent 能做什么             | Tool Call                   | Tool Observation            |
| Persistence      | 怎样保存和恢复             | Snapshot                    | Checkpoint                  |
| Observability    | Runtime 发生了什么         | Agent Events                | Logs、Trace、Event Stream   |

---

## 23. 暂不实现的内容

为避免学习项目过早工程化，V1 暂不实现：

- 多 Agent 协作
- 分布式 Session Runtime
- Event Bus
- Vector Database
- 自动 DAG Planner
- Reflection Agent
- Agent-to-Agent Protocol
- MCP Server
- Web UI
- Kubernetes Deployment
- 跨 Region 高可用
- Exactly-once Tool Execution

这些能力应在单 Session、Tool Calling、ReAct、State、Memory 和 Checkpoint 已经清晰后再逐步扩展。

---

## 24. 总结

该设计采用单 Project、单进程的模块化单体架构。

系统的核心执行关系是：

```text
Host
→ Orchestrator
→ Execution Engine
→ Context Manager
→ Model Runtime
→ LLM
→ Tool Manager
→ Observation
→ State Update
→ Next Context
```

其中：

- 工作记忆按归属直接读取，长期记忆通过检索召回；Context 合并这两个来源并组装模型输入。
- 长期 Memory / Context 可以类比推荐系统中的画像、召回、排序和组装，但不要求自研搜索基础设施。
- State / Persistence 可以类比流处理系统中的运行状态、Checkpoint 和恢复。
- Execution Engine、Tool Calling、ReAct、Skill Loading 是学习重点。

第一版的成功标准不是功能丰富，而是能够清晰观察并解释：

1. 当前任务使用了什么 Execution Strategy。
2. 当前 State 处于什么阶段。
3. 哪些工作记忆被直接读取，哪些长期记忆被检索出来。
4. 哪些信息最终进入了 Model Context。
5. LLM 为什么请求某个 Tool。
6. Tool Observation 如何影响下一轮决策。
7. 程序崩溃后如何从 Checkpoint 恢复。

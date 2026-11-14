# HanyingAgent 模块职责与运行流程

## 1. 文档定位

本文整理 HanyingAgent 的模块职责、模块之间的边界，以及从用户输入到任务结束的完整流程。
项目以学习为目的，先采用单项目、单进程的模块化单体，通过明确契约为后续远程适配保留边界。

本文中的业务执行流程是目标设计，不代表所有模块已经实现。当前已定义 Session、Task、User、
Memory、Tools、LanguageModels 和 Conversation 的主要契约；已实现内存工作记忆、Context Builder、
Ollama 模型适配器，以及 Generic Host / DI、关联日志和 DirectExecutor 驱动的 Console。
当前已实现可信本地单用户、内存 Session / Conversation 和有界多轮历史窗口；
最小 Direct 任务编排已实现：后台运行、状态与结果查询、取消和 Session 原子占用；
工具循环、等待用户与恢复流程仍待实现。具体当前范围见 [Orchestration](../HanyingAgent/SRC/Runtime/Orchestration/Orchestration.md)。

逻辑模块不等于独立进程或项目，Contracts 中的数据类型也不等于保存数据或执行业务的实现。
项目入口见 [HanyingAgent](../HanyingAgent/HanyingAgent.md)。

## 2. 总体划分

```text
Host：用户交互
  ↓
Runtime：会话与任务协调、历史、执行流程、Context、执行状态
  ↓
能力契约：User / Session / Task / Conversation / Memory / Tools / LanguageModels
  ↓
能力与技术实现：Memory、Skills、Tools、模型适配、持久化、可观测性
```

上图表达逻辑协作，不是所有接口必须经过同一个运行组件，也不表示每个业务能力都已有独立实现目录。
模块之间传递明确的数据和操作请求，不把 Provider SDK、数据库连接或本地执行句柄暴露到公共 DTO。

### 2.1 模块职责总表

| 模块 | 核心职责 | 不负责 |
|---|---|---|
| [User](../HanyingAgent/SRC/Contracts/Users/Users.md) | 管理用户标识、显示名称和资料时间。 | 不管理所有用户关联数据，不等于认证或授权服务。 |
| [Session](../HanyingAgent/SRC/Contracts/Sessions/Sessions.md) | 管理会话身份、所属 UserId、生命周期和当前任务关联。 | 不保存完整历史，不负责界面，不携带执行句柄。 |
| [Task](../HanyingAgent/SRC/Contracts/Tasks/Tasks.md) | 定义一次工作的公共快照、结果和提交、查询、补充、取消协议。 | 不把每条消息都当作新任务，不独立维护另一套矛盾的执行状态。 |
| [Conversation](../HanyingAgent/SRC/Runtime/Conversation/Conversation.md) | 管理真实交互的追加、确定顺序、归属和历史读取。 | 不渲染界面，不选择全部模型输入，不决定数据库技术。 |
| [Memory](../HanyingAgent/SRC/Capabilities/Memory/Memory.md) | 管理可复用语义信息的写入、修正、读取、检索、失效和遗忘。 | 不等于全部聊天历史，不管理当前执行步骤。 |
| [Skills](../HanyingAgent/SRC/Capabilities/Skills/Skills.md) | 提供任务方法、SOP、参考资料和选择线索。 | 不代替工具实现，也不直接推进任务控制流。 |
| [Context](../HanyingAgent/SRC/Runtime/Context/Context.md) | 为本轮模型调用选择、排序、压缩并组装消息和工具定义。 | 不管理来源数据的生命周期，不默认发送全部历史。 |
| [Orchestration](../HanyingAgent/SRC/Runtime/Orchestration/Orchestration.md) | 协调任务接受、会话占用、执行启动、等待、恢复、取消和收尾。 | 不实现每种任务内部策略或 Provider 协议。 |
| [Execution / Executor](../HanyingAgent/SRC/Runtime/Execution/Execution.md) | 按 Direct、Chain、ReAct 等策略推进任务内部步骤。 | 不直接实现模型 SDK、工具传输或数据库访问。 |
| [State](../HanyingAgent/SRC/Runtime/State/State.md) | 管理执行位置、步骤、迭代、待处理动作、状态转换和恢复进度。 | 不代替语义记忆，也不与 TaskInfo 各自维护矛盾事实。 |
| [LanguageModels](../HanyingAgent/SRC/Contracts/LanguageModels/LanguageModels.md) | 统一一次模型调用的消息、参数、文本、工具调用、结束原因和用量。 | 不执行工具，不召回记忆，不决定整个 Task 是否完成。 |
| [Tools](../HanyingAgent/SRC/Capabilities/Tools/Tools.md) | 校验并执行实际动作，统一工具定义、执行约束和 Observation。 | 不决定整个 Agent 的执行策略，不信任模型自行授予的权限。 |
| [Host](../HanyingAgent/SRC/Host/Host.md) | 接收输入、解析显式命令、选择会话、展示历史和结果。 | 不构造模型 Context，不推进 ReAct，不实现检索算法。 |
| [Persistence](../HanyingAgent/SRC/Infrastructure/Persistence/Persistence.md) | 实现用户、会话、任务、历史、记忆、状态、Checkpoint 和 Artifact 的具体存储。 | 不决定业务含义、任务策略或访问政策。 |
| [Observability](../HanyingAgent/SRC/Infrastructure/Observability/Observability.md) | 通过日志、事件、指标和追踪解释执行过程。 | 不成为任务状态或业务记录的另一套权威存储。 |

### 2.2 Contracts 的职责

[Contracts](../HanyingAgent/SRC/Contracts/Contracts.md) 是模块之间的协议层，不是另一个执行引擎。

- 定义接口、ID、消息、请求、响应与必要的约定。
- DTO 表达数据，接口表达能力，不自动完成业务规则、存储或远程调用。
- DTO 不携带锁、CancellationTokenSource、执行 Task、数据库连接或 Provider SDK 实例。
- 方法中的 CancellationToken 控制本次调用或等待，不作为需要持久化的数据字段。
- 本地和远程实现应保留相同业务语义，而不只是相同方法名称。

## 3. 容易混淆的关系

### 3.1 用户、会话、任务和消息

```text
User
  └── 多个 Session
        ├── 多条 Conversation 记录
        └── 多个先后执行的 Task
              ├── 多次用户输入或补充
              ├── 多次模型调用
              └── 多次工具调用
```

- 一个 Session 属于一个 User，不在会话快照中嵌入完整 UserInfo。
- 一个 Session 可以先后执行多个 Task，但最多一个未结束任务。
- 一个 Task 不等于一条消息，也不等于一次模型调用。
- TaskInfo.Input 保存最初工作请求，不被后续补充输入覆盖。
- 会话历史通过 SessionId 读取，任务历史通过 TaskInfo.SessionId 查询。
- UserInfo 不重复保存无限增长的 SessionId、TaskId 或记忆集合。

### 3.2 资料归属不等于模块归属

与用户相关的数据仍由各自模块管理：

| 数据 | 管理者与关联方式 |
|---|---|
| 用户资料 | User，通过 UserId 查询。 |
| 用户拥有的会话 | Session，通过 UserId 关联。 |
| 聊天记录 | Conversation，通过 SessionId 关联，必要时关联 TaskId。 |
| 用户偏好、经验等记忆 | Memory，通过 User 归属关联。 |
| 工具动作结果 | Tools 表达执行结果，Conversation、State 等按需要记录引用和进度。 |

知道 UserId 或 SessionId 不等于拥有访问权。身份来自可信入口，数据归属和访问权限需要业务边界验证。
名称允许重复，不能依赖用户显示名称或会话名称识别身份。

### 3.3 历史、记忆与状态

| 概念 | 回答的问题 |
|---|---|
| Conversation | 实际发生过哪些交互？ |
| Memory | 哪些语义信息值得后续复用？ |
| State | 当前执行到哪里，下一步从哪里继续？ |
| Context | 当前这一轮应让模型看到什么？ |

Memory 中还要区分两个独立维度：

- 使用方式：工作记忆与长期记忆。
- 归属范围：Task、Session、User、Global。

User / Session / Task 不直接等于记忆的保存期限。长期记忆也不等于原样保存的全部聊天历史。
目前 Global 仅指当前 HanyingAgent 实例，不包含多租户模型。

### 3.4 公共快照与权威数据

SessionInfo、TaskInfo、UserInfo 是公共快照，不是存储引擎或本地执行对象。
查询不会让快照持续同步，复制或使用 with 创建新对象也不等于保存业务变更。

Task 的公共状态、版本和内部执行进度需要由一致的变更流程维护，不能在 Task 与 State 中
分别保存两套互相矛盾的事实。具体持久化技术属于 Infrastructure。

### 3.5 Conversation 追加与分页

历史内容独立于 ModelMessage，分为 User、Assistant 和 Tool，工具调用与 Observation 复用已有类型。
Assistant 来自模型时保留结束原因，截断、过滤或拒绝即使没有有效正文也不应伪装成完整回答。

调用方首次追加前生成稳定 MessageId，服务端分配会话内 Sequence 和 CreatedAt。
同 ID、归属和内容的重试返回原记录，不同载荷报冲突；不能在超时重试时重新生成 ID。
接口目前只定义这些语义，真正去重、并发顺序和存储仍未实现。

读取首次默认最近 20 条，页内 Sequence 升序，游标继续读取更早历史。
Context 不直接把分页结果发送给模型：需要选择和转换内容，并补齐工具关联或舍弃整个相关片段。
接口与具体约定见 [Conversation 契约](../HanyingAgent/SRC/Contracts/Conversation/Conversation.md)。

## 4. 完整运行流程

### 4.1 确定用户

Host 从可信入口确定当前 UserId，按需查询用户资料；首次使用时可以显式创建资料。
创建 UserInfo 不等于登录或认证，目前尚未实现认证、角色或外部身份绑定。
不能仅凭请求里声称的 UserId 就认定调用者有权限。

### 4.2 创建或选择会话

创建会话：

```text
UserId + 会话名称
  → 校验用户存在性和创建权限
  → 分配 SessionId
  → 保存 UserId、名称和时间
  → 返回 Idle 的 SessionInfo
```

选择会话：

```text
SessionId
  → 校验访问权限
  → 读取 SessionInfo
  → 按 SessionId 分页读取历史及必要的任务信息
  → Host 恢复展示
```

选择会话不自动创建 Task、调用模型或重新执行旧动作。Closed 会话可以查看，不能再提交工作。
当前 ListAsync 返回可见会话，尚未定义按 UserId 过滤会话的独立查询接口。

### 4.3 区分输入操作

| 操作 | 当前契约输入 | 行为 |
|---|---|---|
| 创建会话 | CreateSessionRequest：UserId、Name。 | 显式创建，不按名称猜测复用。 |
| 提交新工作 | SubmitTaskRequest：SessionId、Input。 | 空闲会话中创建新 Task。 |
| 补充原任务 | ProvideTaskInputRequest：TaskId、ExpectedVersion、Input。 | 只恢复版本匹配的 WaitingForUser 任务。 |
| 请求取消 | TaskId。 | 发起业务取消，不保证已停止。 |
| 查看会话或任务 | 对应 ID 或查询请求。 | 返回快照或结果，不启动执行。 |

提交新工作时不需要重复传递会话名称、用户名称和完整资料。Runtime 按 SessionId 找到
SessionInfo.UserId，再按需要读取用户资料或用户级记忆。
提交到不存在的会话应显式报错，不自动创建另一会话。

### 4.4 接受新任务

Orchestration 协调以下操作：

1. 验证 Session 存在、未关闭且调用方有权限。
2. 确认没有未结束任务。
3. 创建任务记录，保存原始 Input、SessionId、状态、版本和时间。
4. 将 Session 置为 Active 并关联 CurrentTaskId。
5. 协调记录用户消息，并准备启动后台执行。

任务接收与会话占用必须一致，不能使用先 Get 再无条件启动的竞态流程。
记录历史与启动执行也需要明确一致性和失败处理，不能报告接受成功却留下不可查询、无法推进的任务。
具体事务、并发与恢复机制仍待实现，不假设独立远程调用天然具备原子性。

SubmitAsync 接受后返回 TaskInfo，不等待最终回答；返回快照时任务可能已经推进，不保证仍为 Created。
忙时拒绝新工作，不排队；等待模型、工具和用户都属于未结束。

### 4.5 接受补充输入

只有 WaitingForUser 的原任务可以接受补充，状态和 ExpectedVersion 需要一起检查。
检查、接受输入和状态版本变更须原子协调：

```text
校验任务归属和访问权
  → 校验 WaitingForUser
  → 校验 ExpectedVersion
  → 接受并记录补充
  → 更新状态、递增版本、恢复原任务
```

版本防止重复点击、重试或过期回答恢复错误轮次。补充不创建新任务，也不替换最初 Input。
若任务正在运行或等待模型、工具，当前不支持 steering，也不自动将新输入排到后面。

### 4.6 初始化或恢复执行

Orchestration 加载 Task、Session 和必要执行进度，复用已经接受的 TaskId。
Execution 选择执行策略，结合 Skill 说明启动或恢复相应 Executor：

| 策略 | 含义 |
|---|---|
| Direct | 按约定执行一次模型调用。 |
| Chain | 按预定义步骤推进并传递中间结果。 |
| ReAct | 在模型、工具和 Observation 之间循环。 |

恢复任务应从等待或安全恢复位置继续，不能重新创建身份或盲目重复工具动作。
State 记录策略、步骤、迭代和待处理信息；当前这些具体实现和完整恢复协议仍未定义。

### 4.7 构造本轮 Context

当执行步骤需要模型时，Context 根据当前工作选择输入：

```text
任务和执行位置
  ├── Conversation 中必要的历史
  ├── 按归属读取的工作记忆
  ├── 按需要检索的长期记忆
  ├── 相关 Skill 与约束
  ├── 工具 Observation
  └── 本轮允许使用的工具定义
        ↓
选择、去重、排序、预算控制和必要压缩
        ↓
ModelInvocation：Messages + Tools + Options
```

用户级资料和记忆从 SessionInfo.UserId 出发按需获取，不能因为相关性高就越过用户归属和访问边界。
并非每轮都需要读取全部记忆；展示完整历史也不意味着发送完整历史。

模型消息须保留角色、工具 Call ID、参数及结果关联。ModelToolMessage 复用 ToolObservation，
压缩不能丢弃失败或未知状态、错误和必要输出引用，也不能把工具协议压平成普通 User 文本。

### 4.8 调用语言模型

ILanguageModelRuntime.InvokeAsync 完成一次非流式调用，Provider 适配器转换请求和响应。
模型模块不查询业务数据库、不执行工具，也不决定任务终态。

ModelResponse 返回：

- Text：本轮文本，可以与工具调用同时存在。
- ToolCalls：完整且被允许的工具调用，没有调用时为空列表。
- FinishReason：Stop、ToolCalls、Length、ContentFiltered、Refused 或 Unknown。
- Usage：可空用量，单项未知也用 null，不用 0 伪装未知。
- ProviderFinishReason：可空的原始结束原因标识。

Stop 只表示本轮生成结束，不等于整个 Task 完成。
网络错误、取消、无效响应和解析失败必须显式处理，不能转换成空文本成功响应。
Unknown 仅表示结束原因无法映射或未报告，不用来掩盖调用失败。

### 4.9 分支一：执行工具后继续

```text
模型提出完整调用
  → Runtime / Executor 校验当前执行约束
  → ToolManager 校验归属、权限、Schema 和调用记录
  → 本地实现 / HTTP 适配器 / MCP 适配器执行
  → 返回 ToolObservation
  → 记录交互并更新执行进度
  → 构造下一轮 Context
  → 再次调用模型
```

工具调用不创建新 Task。一次 Assistant 可提出多个调用，下一轮请求前须为对应调用提供明确结果。
工具失败不一定导致整个 Task 立即失败，由 Executor 根据策略处理，但必须保留真实失败信息。
工具结果未知不等于没有执行；Failed 或 Cancelled 也不证明副作用已经回滚。
同一 Call ID 不自动保证幂等，有副作用的调用不能因超时就盲目重试。
大型输出通过 Artifact 保存，向后续模型提供受控摘要与必要引用。

### 4.10 分支二：等待用户

由执行策略或明确的结构化交互协议决定需要用户补充，不能仅凭输出出现问号自动暂停：

```text
记录问题与恢复信息
  → Task 进入 WaitingForUser
  → Session 保持 Active
  → Host 展示问题
  → 等待 ProvideInputAsync 补充原任务
```

目前已定义等待状态与补充接口，但尚未定义完整的等待发起协议、问题载荷和恢复实现。
这是后续需要补齐的能力，不假设当前模型响应已经自动支持结构化等待。

### 4.11 分支三：继续步骤或结束任务

若策略还有下一步，更新 State 后继续，不创建新 Task。
若达到完成条件，进入终态收尾；输出截断、拒绝、过滤或未知原因需要明确处理，
不能仅因为收到响应就判定成功。执行期间还需检查迭代数、工具调用数量、时间和取消约束。

收尾需要协调：

```text
保存 TaskResult
  + 更新任务终态、版本和完成时间
  + 记录最终交互与必要恢复信息
  + 清除 Session.CurrentTaskId
  + 将 Session 恢复为 Idle
```

Completed、Failed、Cancelled 才是任务终态，收尾后不重新启动同一任务。
请求取消可以先进入 Cancelling；在任务真正结束前不能提前释放会话。
完成与取消存在竞争，以最终状态和结果为准，不保证每个取消请求最终都得到 Cancelled。

### 4.12 展示结果、整理记忆和再次打开

Host 可以先通过 GetAsync / GetResultAsync 查询任务进度与最终结果。
已知任务未结束时 GetResultAsync 返回 null，未知任务显式报错；未来可再设计事件推送。

执行过程中或结束后，可按策略提取值得保留的记忆，完成归属判断、压缩、去重和来源保留。
不把所有消息原样提升为长期记忆，也不让记忆整理失败掩盖已经确定的任务结果。
这些自动整理能力目前尚未实现。

再次打开 Session 时，按权限读取会话、历史和任务状态并恢复展示。
若存在未结束任务，需要结合持久化执行进度决定如何安全恢复。
读取历史不等于恢复执行，持久化了快照也不自动证明外部动作可以安全重放。

## 5. 总流程概览

```text
确定用户
  ↓
创建或选择 Session
  ↓
Host 识别操作
  ↓
接受新 Task / 为原 Task 补充输入
  ↓
Orchestration 初始化或恢复执行
  ↓
Executor 按策略推进
  ↓
Context 构造本轮输入
  ↓
LanguageModels 调用
  ├── 工具调用 → Tools → Observation → 下一轮
  ├── 需要用户 → 保存等待位置 → 等待补充
  ├── 内部未完成 → 继续步骤
  └── 工作结束 → 保存结果、更新状态、释放 Session
  ↓
Host 展示
```

State、Persistence 和 Observability 贯穿流程，而不是只在最后运行。

## 6. 关键约束与当前缺口

| 项目 | 当前约定或状态 |
|---|---|
| 身份与归属 | Session 必需关联 UserId；身份、存在性与授权验证仍待实现。 |
| 旧会话数据 | 缺少 UserId 的 JSON 显式失败，需明确迁移，不自动填入当前或默认用户。 |
| 同会话并发 | 最多一个未结束任务，忙时拒绝，无排队和 steering。 |
| 版本 | Task Version 从 0 开始，快照变更时递增，补充输入校验 ExpectedVersion；查询不递增。 |
| 取消 | 方法 CancellationToken 不等于业务取消，也不保证撤销已接受或持久化的变更。 |
| 重试 | 当前创建和提交没有幂等键；调用超时不能无条件重新创建或执行。 |
| 历史 | 已定义 Conversation 消息、稳定 ID、序号、幂等追加和向更早历史分页；管理器、存储和关联校验仍待实现。 |
| 执行恢复 | Executor、状态管理、Checkpoint 及重复动作处理尚未实现。 |
| 模型能力 | 已定义文本、工具协议和非流式调用，未实现 Provider、流式、多模态或 Embedding。 |
| 通知 | 可先查询任务快照与结果，完整事件订阅协议尚未定义。 |
| 存储与可观测性 | 已提供工作记忆的进程内存 Store；其他存储、可观测性与跨模块一致性仍待实现。 |

这些缺口不应通过静默默认值、伪造成功结果或把问题全部交给模型推断来绕过。

## 7. 一句话总结

User 管是谁，Session 管哪次长期会话，Task 管正在完成什么工作。
Conversation 管历史，Memory 管可复用知识，Skills 管方法，Context 管本轮输入。
Orchestration 管组织协调，Executor 管执行流程，State 管执行位置。
LanguageModels 管模型调用，Tools 管实际动作。
Host 管交互，Persistence 管保存，Observability 管观察，Contracts 管边界。

同类业务事实应有明确管理者，其他模块通过契约使用它，不各自维护重复且矛盾的事实。
远程化需要保留这些语义，并明确超时、幂等、授权与一致性，不能简单地把本地方法替换成网络调用。

## 8. 相关设计

- [Mini Agent Runtime 设计初稿](<./Mini Agent Runtime 设计初稿.md>)：完整背景、分层及后续组件草图。
- [本地模块与远程 Service 演进设计](<./Agent Runtime 本地模块与远程 Service 演进设计.md>)：适配器和远程边界。
- [Contracts 导航](../HanyingAgent/SRC/Contracts/Contracts.md)：当前公共契约入口。

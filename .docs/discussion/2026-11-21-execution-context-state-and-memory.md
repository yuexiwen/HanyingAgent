# Execution、Context、State、Conversation 与 Memory 的职责

讨论日期：2026-11-21

本文整理围绕 Direct / ReAct 执行策略、Context 构造、任务中间数据和记忆召回的讨论。
区分当前实现与未来设计，不表示已实现 ReAct、执行状态恢复或相关性检索。

## 一、为什么重新讨论 Orchestration 与 Execution？

ReAct 可能在一个业务 Task 内多次调用模型：

```text
构造 Context → 模型推理 → 工具调用 → 观察结果
      ↑                                  ↓
      └────────── 更新执行记录 ────────────┘
```

因此，第一次构造的 ModelInvocation 不能直接满足所有后续调用。
新的工具结果会改变候选内容，Context 需要重新选择和组装。

两种职责方案都成立：

| 方案 | 谁拥有完整策略 | 另一层负责什么 |
|---|---|---|
| Orchestration 拥有 Direct / Chain / ReAct | 编排器决定步骤、循环、停止条件，并调用 Context、模型执行和工具能力。 | Execution 负责单次模型执行边界。 |
| Execution 拥有 Direct / Chain / ReAct | 执行器决定策略内部步骤、循环与停止条件。 | Orchestration 负责公共准备与收尾。 |

复杂度不会因为换层而消失。关键是只让一层拥有策略循环，
避免 ReactOrchestrator 与 ReactExecutor 各维护一套循环、预算或重试规则。

本次讨论选择第二种方案：

> Orchestration 管公共准备与收尾；Execution 管包括第一轮在内的完整策略；
> Context 管每次模型输入的筛选、预算和组装规则。

## 二、选定的调用边界

```text
Management/Tasks
  接受任务、占用 Session、安排后台执行
      ↓
Agent/Orchestration
  保存用户输入
  读取初始历史与 Memory
  准备执行材料
      ↓
Agent/Execution
  调用 ContextBuilder
  调用模型
  按策略执行后续步骤
  返回最终 ExecutionOutcome
      ↓
Agent/Orchestration
  保存最终 Assistant 回复
      ↓
Management/Tasks
  保存 TaskResult 与终态、释放 Session
```

第一轮 Context 也由 Execution 调用 Builder 构造，避免第一轮与后续轮次由两个地方分别协调。
这不意味着把预算算法复制进执行器；具体算法仍由 Context 模块维护。

### 当前已完成的调整

- [DialogueOrchestrator](../../HanyingAgent/SRC/Runtime/Agent/Orchestration/DialogueOrchestrator.cs)
  保留输入保存、历史候选读取、用户 Memory 读取、执行调用与回复保存。
- [DirectExecutor](../../HanyingAgent/SRC/Runtime/Agent/Execution/DirectExecutor.cs)
  注入 IContextBuilder，接收 BuildContextRequest，构造 ModelInvocation 后调用一次模型。
- 当前复用已有 BuildContextRequest，没有预建通用执行请求、执行器工厂或 ReAct 实现。
- WaitingForModel 回调仍在 Context 构造成功后、模型调用前触发。
- Context 构造失败或取消时，不继续调用模型。
- Context 构造纳入 ExecutionId 日志范围；执行边界负责传播和记录相应失败，
  编排器不重复记录执行器已处理的错误。
- 修改后相关回归 231 项通过，Release 使用 `-warnaserror`；不是全套回归或真实模型联调结果。

`phase = "SaveAssistant"` 等变量只是编排器的诊断阶段标记，
不是 Task 生命周期状态，也不是未来的 ExecutionState。

## 三、重新构造 Context，不等于重新读取全部数据

任务开始时可以获取初始历史和 Memory 快照：

```text
初始历史 + 初始 Memory + 用户输入
                  ↓
          执行中新增的记录
                  ↓
      每次调用模型前重新构造 Context
```

每轮候选输入通常是：

> 初始快照 + 本次任务中已产生的模型可见响应、工具调用与工具结果。

- Context 通常需要每轮重建，因为候选内容和可用预算变化。
- 历史与 Memory 不必默认每轮重新查询。
- 同一 Session 忙时拒绝新任务，有利于保持初始历史稳定，但这不是数据库事务快照承诺。
- 用户 Memory 可能被其他合法操作更新；可以约定本次任务使用初始快照，下次任务再读取最新数据。

重新读取是明确的策略选择，不是 ReAct 的固定步骤。

## 四、Memory 可以由两个阶段访问

### 公共初始读取

Orchestrator 可以按策略读取 Global、User、Session、Task 范围的记忆。
任务在进入编排器前已经创建，任务入口也已有 TaskId，因此并非无法读取 Task Memory。
新任务可能没有对应记忆，这是无数据，不是缺少身份。

当前实现只读取 User 级最近有效工作记忆，没有自动合并上述所有范围。

### 执行中的按需读取

Executor 可以在需要时通过 Memory 接口补充或刷新，例如：

- 当前子目标需要初始快照没有包含的信息。
- 等待用户后恢复，需要明确获取最新记忆。
- 本次任务写入了新的语义信息，后续步骤需要使用。
- 产品要求当前任务看到其他操作对 Memory 的更新。

这不意味着每轮都查，也不意味着每个执行器现在都要注入 Memory。
当前 DirectExecutor 使用编排器提供的快照即可。
未来 ReAct 有明确需求时再引入按需检索能力。

共同边界：

```text
Orchestrator / Executor：决定何时查、查什么
Memory：维护与检索允许范围内的记忆
Context：决定候选结果中哪些进入本次模型输入
```

执行器不直接查询 Store，不复制记忆检索算法，也不能因为模型提出请求就越过归属和权限边界。

## 五、一次 Task 内部的多轮响应如何保存？

用户一次被接受的普通输入创建一个业务 Task。
这个 Task 可以包含多次模型调用与工具调用，不是每轮都创建新业务 Task。

讨论选定的未来默认规则：

```text
用户输入 → Task A
              ├─ 中间模型可见响应
              ├─ 工具调用与结果
              ├─ 后续模型调用
              └─ 最终回答
                     ↓
Conversation：[用户输入，最终回答]
                     ↓
Task B 的候选历史
```

- 初始 User 和最终 Assistant 进入跨任务对话历史。
- 中间记录用于当前任务的后续推理，由 ContextBuilder 按预算选择。
- 中间记录可以另行保存为执行轨迹或检查点，但不因此自动进入下一任务的候选历史。
- 不要求保存模型内部隐藏的推理过程；这里讨论的是 API 可见且执行需要的记录。
- 某条记录被保存，不代表每次都发给模型；进入某轮 Context，也不意味着永远保留在后续 Context。

这是产品策略，不是所有 Agent 都必须采用的唯一规则。
当前只有 Direct，Conversation 仍拒绝工具历史；
执行记录与跨任务历史的分离需要在实现工具策略时具体落实。

### SaveUserAsync 与 SaveAssistantAsync

| 方法 | 内容 | 时机 | 用途 |
|---|---|---|---|
| SaveUserAsync | 用户本次输入 | 执行前 | 保存记录并取得 Sequence，定位候选历史。 |
| SaveAssistantAsync | 最终回复与结束原因 | 执行后 | 留存回答，供历史查看和后续窗口筛选。 |

两者都通过 Conversation 追加消息，不是重复保存。
调用失败可能留下已保存的 User，但不会伪造 Assistant。
保存 Assistant 失败时，不自动重复模型调用，任务路径由 Tasks 处理失败。
当前不支持的工具动作不会被伪装成普通 Assistant 文本历史。

Conversation 回复与 TaskResult 分别表达交互记录和业务任务结果，不是同一个保存动作。
已保存的非正常回复也不一定符合下一轮历史窗口的选入规则。

## 六、任务中间数据由谁维护？

| 数据 | 负责模块 | 当前或未来的保存方式 |
|---|---|---|
| 当前执行的中间消息、工具结果 | Execution | Executor 持有本次执行工作数据。 |
| 执行位置、待执行动作、恢复数据 | Execution/State | 当前为规划；未来按需保存检查点。 |
| 需要留存的执行轨迹 | 执行领域与诊断设施 | 按需通过存储端口保存，具体实现放 Persistence。 |
| Task 生命周期与最终结果 | Management/Tasks | Task Store。 |
| 用户输入与最终回复 | Conversation | Conversation Store。 |
| 可复用事实、偏好、约束与经验 | Memory | Memory Store 与未来检索设施。 |
| 界面选择、未读与展示标记 | Console / GUI | 界面状态对象，不是执行状态。 |

### State 不等于执行轨迹

- State 回答“现在在哪里、如何继续”。
- 轨迹回答“之前发生了什么、如何排错或审计”。

恢复需要足够的输入、中间结果、待执行动作等，不是记录“第 3 步”就能恢复。
同样，日志摘要不一定包含足够的恢复数据。
未来 State 不应另建一套与 Management/Tasks 冲突的业务生命周期状态机。

### 任务结束后是否删除？

- 运行中的工作对象在执行真正结束后释放，不必一直挂在 TaskManager 中。
- 最终回答和 TaskResult 按各自规则保留。
- 执行轨迹按容量或期限保留，不默认全部永久保存，也不默认一完成就删除全部记录。
- 检查点按恢复需求管理；大型结果可以按需存为产物，敏感内容应最小化记录。
- 取消请求不等于实际停止，不能提前清理仍被执行使用的数据。

当前首个 ReAct MVP 可以先使用执行器内的状态对象与有限诊断信息，
不预建完整 StateManager、检查点系统或轨迹数据库。

## 七、Memory 的范围与保存期限

当前契约支持以下范围：

| Scope | 意义 | 示例 |
|---|---|---|
| Global | 当前 Agent 实例共享的语义信息。 | 通用参考事实或约定。 |
| User | 归属于某个用户，可跨会话使用。 | 用户偏好简洁回答。 |
| Session | 归属于某个会话。 | 本会话项目采用 .NET。 |
| Task | 归属于某项任务。 | 本任务示例必须使用 C#。 |

Scope 表示归属与检索范围，不直接决定期限。
User Memory 不一定永久保存，Task Memory 也不一定随 Task 完成自动删除。

Task Memory 与执行状态不同：

> Task Memory 是本任务应记住的语义信息；ExecutionState 是当前执行如何继续所需的数据。

不能因为数据属于某个 Task，就把工具调用列表、步骤位置或全部中间响应塞进 Memory。
中间数据中值得复用的事实可以经过提取、校验和筛选后写入 Memory，
但这个转换不是把原始记录整体搬走。

## 八、相关性召回与搜索推荐的联系

Memory 不仅可以读取最近记忆，也可以提供与问题或子目标相关的检索。

### 两种查询时机

| 时机 | 查询依据 |
|---|---|
| 初始准备 | 用户问题，例如部署项目时寻找部署环境和既定约束。 |
| 任务内部 | 当前子目标、工具结果或模型提出的信息需求。 |

不建议默认每轮把完整模型 response 直接作为查询。
响应可能包含猜测和无关内容，应围绕下一步需要的信息构造查询，
或使用受约束的显式记忆检索请求。
每轮检索不等于每轮把响应写入 Memory。

### 与搜推系统类似的处理链

```text
显式问题 / 隐式任务上下文
          ↓
限定允许的归属与访问范围
          ↓
关键词、倒排索引、BM25、向量或混合检索
          ↓
有效性过滤、相关性排序
          ↓
Context 按预算选入
```

- 搜索通常有用户显式提供的查询。
- 推荐通常没有显式查询，但仍有历史行为、兴趣和当前场景等隐式输入。
- 两者都可采用候选召回与排序，但目标、信号和评价标准不完全相同。
- Agent 记忆检索可以结合用户问题、偏好、任务目标和当前子目标。
- 关键词与倒排检索是成熟技术，不一定必须使用向量或额外调用 LLM。

需要分别定义：

1. **归属与权限**：允许查谁的记忆。
2. **相关性**：其中哪些对问题有用。
3. **Context 预算**：最终给模型多少。

当前 LocalWorkingMemoryManager 按指定 Owner 读取最近有效记忆，
并非语义相关检索；倒排、向量或混合检索是后续能力。
不能把未来设计描述为当前已有功能，也不能把 Scope 本身当成完整授权机制。

## 九、Conversation 与 Memory 的最终区分

| | Conversation | Memory |
|---|---|---|
| 核心问题 | 这个会话中发生过什么？ | 当前有什么值得记住和复用？ |
| 数据形态 | 带顺序与归属的交互记录。 | 有来源、归属、有效性等规则的语义信息。 |
| 常见读取 | 按 Session、Sequence 或轮次查看。 | 按归属、有效性及未来的查询相关度获取。 |
| 用途 | 历史展示、上下文候选、追溯。 | 为当前推理补充事实、偏好、约束和经验。 |

例如：

```text
Conversation：
用户：“我喜欢简洁回答。”
Assistant：“好的。”

Memory：
用户偏好：简洁回答。
来源：对应的交互记录。
```

Conversation 不只是“历史呈现”，展示只是其用途之一。
Memory 不只是“相关搜索”，写入、更新、归属、有效期和遗忘同样属于它的职责。

两者职责分离，但可以保留来源关联：

- 原始 Conversation 不因信息提取为 Memory 就自动删除。
- Memory 中的偏好可以被确认、修正、失效或遗忘。
- Memory 可以来自对话，也可以来自其他合法来源。

> Conversation 记录发生过什么；Memory 提供当前值得使用的知识、偏好和经验。
> State 承载当前任务如何继续；Context 决定这一次模型实际看到什么。

## 十、当前边界与后续方向

当前已经调整 Direct 的 Context 构造调用位置，保留任务生命周期、历史与用户工作记忆读取。
以下仍未实现：

- ReAct 工具循环和中间执行记录。
- 执行状态、暂停恢复和检查点。
- Executor 中的按需 Memory 检索。
- Global / User / Session / Task 的组合读取策略。
- 查询相关度召回、自动 Memory 提取与长期整合。

后续由首个工具闭环的真实需求推动这些边界落地，
不为可能的多轮策略提前建立大量抽象或重复的数据管理模块。

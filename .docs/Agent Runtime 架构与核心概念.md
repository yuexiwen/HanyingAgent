# Agent Runtime 架构与核心概念

## 一、Agent Runtime 的定义

> **Agent Runtime 是负责让一个 Agent 持续运行起来的执行环境。它连接 LLM、上下文、状态、工具、Skill 和执行策略，并负责驱动“输入 → 决策 → 行动 → 观察 → 继续或结束”的全过程。**

一个比较完整的逻辑架构可以拆成：

1. **Orchestrator**
2. **Model Runtime**
3. **Context Manager**
4. **State / Memory / Persistence**
5. **Skill Manager**
6. **Tool Manager**
7. **Execution Engine**
8. **Production Infrastructure**
9. 上层可选：**Agent Host / Multi-Session Manager**

---

## 二、总体架构

```text
                  Application / Host
                         │
             Multi-Session Agent Shell
                         │
                  Agent Runtime
                         │
                   Orchestrator
                         │
        ┌────────────────┼────────────────┐
        │                │                │
   Model Runtime   Context Manager   Execution Engine
        │                │                │
        │          State / Memory    Execution Patterns
        │                │
        ├────────────────┼───────────────┐
        │                │               │
 Skill Manager      Tool Manager     Persistence
        │                │
      Skills          Tools / MCP
```

外围还包括：

- Logging / Tracing
- Security
- Guardrails
- Retry / Timeout
- Evaluation
- Observability

---

## 三、Orchestrator：总调度器

Orchestrator 是 Runtime 最上层的协调模块，主要负责：

- 接受任务
- 创建或恢复任务 State
- 调用 Context Manager
- 调用 Model
- 选择 Execution Strategy
- 调用 Tool / Skill
- 驱动任务向前推进
- 判断完成、失败或等待
- Cancellation / Timeout
- 保存执行状态

它解决的是：

> **Agent 整体怎么跑起来？**

不要把 Orchestrator 理解成“负责所有思考”。真正的语义判断经常交给 LLM，Orchestrator 更像 Runtime 的控制中心。

---

## 四、Model Runtime：LLM 接入层

Model Runtime 负责与模型交互：

```text
Context
   ↓
Model Client
   ↓
LLM
   ↓
Response
```

主要负责：

- Messages
- System instructions
- Structured output
- Tool definitions
- Model invocation
- Streaming
- Token usage
- Model selection
- Retry

例如：

```text
SendAsync(
    messages,
    tools,
    responseSchema
)
```

返回结果可能是普通文本，也可能是 Tool Call。

一个重要概念是：

> **LLM 本身不会真正调用 Tool。**

LLM 只是产生“我要调用 `QueryKusto`，参数是 xxx”之类的结构化请求，Runtime 才真正执行工具。

---

## 五、Context Manager：当前这一轮让 LLM 看到什么

Context Manager 的核心职责不是简单保存聊天历史，而是：

> **为当前一次 Model Invocation 构造最合适的 Context。**

可能组合：

```text
System Instructions
+
Current Goal
+
Current Task State
+
Relevant Skill Instructions
+
Relevant Memory
+
Recent Conversation
+
Tool Observations
+
Relevant Retrieved Knowledge
+
Current User Input
```

### Context Engineering 和 Context Manager 的关系

> **Context Manager 是 Runtime 模块。**
>
> **Context Engineering 是设计 Context Manager 策略的方法论。**

例如：

- 最近保留多少轮？
- 哪些 Observation 删除？
- 哪些内容压缩？
- 哪些 Memory 需要召回？
- Skill 的哪些部分进入 Context？
- 5000 行 Kusto 数据保留原文还是 Summary？

因此：

- **Context Management = 机制**
- **Context Engineering = 如何把这个机制设计好**

---

## 六、State、Memory 和 Persistence

### 1. State

State 描述：

> **这一个任务现在执行到哪里？**

例如：

```text
Goal
CurrentStep
Messages
Observations
CurrentPlan
SelectedSkill
StepCount
ExecutionStatus
```

状态示例：

```text
Status = WaitingForTool
CurrentTool = QueryKusto
StepCount = 7
```

### 2. Memory

Memory 解决：

> **过去有哪些信息值得未来使用？**

例如：

- 跨 Session 的用户偏好
- 历史经验
- 已知事实
- 长期知识

简单理解：

> **State = 现在进行到哪。**
>
> **Memory = 过去有什么值得记住。**

### 3. Persistence

Persistence 解决：

> **State 和 Memory 怎么持久化？**

第一版可以使用：

- JSON
- SQLite

后续再考虑：

- SQL
- Redis
- Vector Database
- Distributed Storage

Persistence 还使下面的流程成为可能：

```text
Pause → Save → Restart → Resume
```

---

## 七、Skill Manager：Agent 知道怎么做某类事情

Skill 可以理解为：

> **给 LLM 阅读和遵循的一套可复用 SOP、Instructions 和 Knowledge Package。**

例如，`Incident Investigation Skill` 可能告诉 LLM：

- 怎么调查 Incident
- 优先关注什么
- 哪些证据才能支持 Root Cause
- 什么情况下不能下结论
- 输出格式应该是什么
- 可以参考哪些文档
- 可以使用哪些脚本

### Skill Manager 的职责

- Skill Discovery
- Skill Matching
- Skill Selection
- Skill Loading
- Reference Loading
- Script / Resource Discovery
- Metadata Management

典型流程：

```text
User: 调查 production incident
       ↓
Skill Manager
       ↓
Incident Investigation Skill
       ↓
Load SKILL.md
       ↓
Context Manager
       ↓
Relevant Skill Instructions
       ↓
LLM
```

Skill 本身不是 Prompt Chaining，也不是 ReAct。

Skill 解决的是：

> **这类事情应该怎么做。**

Execution Pattern 解决的是：

> **整个执行过程如何推进。**

---

## 八、Tool Manager：Agent 能做什么

Tool Manager 主要管理：

- Tool Registration
- Tool Schema
- Tool Discovery
- Tool Invocation
- Permission
- Input Validation
- Result Normalization
- Tool Errors

工具示例：

```text
QueryKusto
ReadLog
ReadFile
WriteFile
GitCommit
GetDeploymentHistory
```

Skill Manager 和 Tool Manager 形成一组清晰的对应关系：

> **Skill = Know-how**
>
> **Tool = Capability**

例如：

- `Incident Investigation Skill` 告诉 LLM 如何调查生产事故。
- `QueryKusto` 提供实际查询数据的能力。

Skill 可以指导 LLM 如何组合使用多个 Tools。

---

## 九、MCP 属于哪里

MCP 最适合放在：

> **Tool / Capability Integration**

这一层。

它不是 Agent 的思考模式。

Tool Manager 可以同时接入：

```text
Native Tool
REST API
Local Function
MCP Tool
Script
```

因此：

> **MCP 是 Agent Runtime 接入外部能力的一种协议或机制。**

---

## 十、Execution Engine：任务怎么执行

Execution Engine 可以支持多个 Execution Strategies / Execution Patterns，而不是“一个 Agent 只能使用一种模式”。

例如：

```text
IExecutionStrategy

├── DirectExecutor
├── ChainExecutor
├── RouterExecutor
├── ParallelExecutor
├── ReactExecutor
└── PlanExecuteExecutor
```

---

## 十一、Direct

最简单的执行方式：

```text
User
 ↓
LLM
 ↓
Answer
```

适合简单问题，例如：

> 什么是 Kusto？

没有必要使用 Chain，更没有必要进入 ReAct 循环。

---

## 十二、Prompt Chaining

Prompt Chaining 的核心是：

> **把复杂任务拆成多个相互连接的步骤，每一步只承担相对明确的任务，前一步结果作为后续步骤输入。**

例如：

```text
Extract Facts
      ↓
Build Timeline
      ↓
Analyze Root Cause
      ↓
Generate Report
      ↓
Validate
```

最重要的特征是：

> **Workflow 在执行之前已经基本确定。**

Workflow 可以由程序员预定义，也可以在开始时让 LLM 生成一次 Plan。关键是，开始执行以后，“下一步做什么”基本已经知道。

### Prompt Chaining 不一定只是顺序执行

顺序：

```text
A → B → C
```

分支：

```text
    → B
A →
    → C
```

循环：

```text
Generate → Validate
    ↑          │
    └── fail ──┘
```

所以 Prompt Chaining 的本质不是线性，而是：

> **控制结构预先确定。**

---

## 十三、Routing

Router 解决：

> **当前输入应该走哪条路径？**

例如：

```text
Request
   ↓
 Router
 ┌─┼────┐
 ↓ ↓    ↓
 A B    C
```

Router 可以是：

- Rule Based
- Classifier
- LLM Router

需要区分两类 Router：

### Execution Router

决定：

> Direct、Chain 还是 ReAct？

### Session Router

决定：

> s1、s2 还是 s3？

两者属于不同层次。

---

## 十四、Parallelization

Parallelization 适合并行执行相互独立的任务：

```text
        ┌→ Search A ─┐
Input ──┼→ Search B ─┼→ Aggregate
        └→ Search C ─┘
```

适用场景：

- 多数据源检索
- 多个独立代码检查
- 多个候选方案生成
- 多个独立分析

在 .NET 中，第一版可以从 `Task.WhenAll()` 开始实现。

---

## 十五、ReAct

ReAct 的核心循环：

```text
Reason
  ↓
Action
  ↓
Observation
  ↓
Reason
  ↓
Action
  ↓
...
```

最后生成 Final Answer。

与 Prompt Chaining 最重要的区别是：

> **下一步在当前 Observation 返回之前可能根本不知道。**

例如生产 Incident 调查：

1. 先 Query Kusto。
2. 结果显示故障只存在于 SU03。
3. LLM 决定检查 SU03 Deployment。
4. 结果显示 10:25 有部署。
5. LLM 决定读取部署前后的日志。

因此：

> **Prompt Chaining：先画路线，再走。**
>
> **ReAct：走一步，看一步，然后决定下一步。**

通常：

- SOP / 固定流程 → Prompt Chaining
- Troubleshooting / Investigation → ReAct

---

## 十六、Planning / Plan-and-Execute

Planning 解决：

> **一个复杂 Goal 应该分解成哪些任务？**

例如：

```text
Goal
 ↓
Planner
 ↓
Plan
├── Task 1
├── Task 2
├── Task 3
└── Task 4
 ↓
Executor
```

它与 ReAct 的核心差异仍然是决策时间点：

- **Plan-and-Execute**：先形成计划，再执行。
- **ReAct**：每次获得新 Observation 后，再决定下一步。

实际 Agent 常结合两者：

```text
Plan
 ↓
Execute
 ↓
Observe
 ↓
Replan
 ↓
Execute
```

---

## 十七、Reflection

Reflection 更准确地属于：

> **Reasoning / Quality Enhancement Mechanism**

典型流程：

```text
Generate
   ↓
Critique
   ↓
Refine
   ↓
Evaluate
```

直到满足质量条件。

Reflection 可以嵌入：

- Prompt Chain
- ReAct
- Planning

因此 Reflection 更像一个质量反馈回路，而不是与所有 Execution Pattern 完全平级的独立模式。

---

## 十八、Tool Use / Function Calling

Tool Use 不属于 Execution Pattern，而属于：

> **Capability Layer**

ReAct 可以使用 Tool：

```text
Reason
 ↓
QueryKusto
 ↓
Observation
 ↓
Reason
```

Prompt Chain 也可以使用 Tool：

```text
LLM → Kusto → LLM → Log → LLM
```

因此：

> **Tool Calling 决定 Agent 可以与外部世界做什么。**
>
> **ReAct / Chain 决定这些能力如何被组织起来使用。**

---

## 十九、Multi-Agent

Multi-Agent 已经进入另一个层次：

> **System Architecture Pattern**

例如：

```text
Research Agent
      ↓
Analysis Agent
      ↓
Writer Agent
```

也可以多个 Agent 并行，最后交给 Aggregator Agent 综合。

Multi-Agent 带来的新问题包括：

- Agent 如何通信
- 怎么 Handoff
- 如何共享 State
- 如何拆任务
- 各 Agent 使用什么 Tool
- 谁判断任务完成
- Context 是否共享

所以 Multi-Agent 不是单 Agent 的普通 Execution Pattern，而是多个 Agent Runtime 或 Agent Instance 如何协作的问题。

---

## 二十、Multi-Session Agent Host 属于哪里

多 Session 设计不是一种 Agent Execution Pattern，它位于 Agent Runtime 的上层：

> **Agent Host / Session Orchestration Layer**

例如：

```text
Agent Shell
     ↓
Session Manager
 ┌────┼────┐
 ↓    ↓    ↓
s1    s2   s3
 │    │    │
Agent Agent Agent
```

每个 Session 拥有自己的：

- Context
- State
- History
- Execution Strategy
- Task Queue
- Output Stream

不同 Session 甚至可以使用不同执行模式：

```text
s1 = ReAct
s2 = Prompt Chaining
s3 = Direct
```

---

## 二十一、Shell 命令属于 Application / Host

### `sls`

列出：

- Session
- Status
- Current Task

所属模块：**Session Manager**。

### `select s1`

设置：

```text
ActiveSessionId = s1
```

之后普通输入默认进入 s1。

所属模块：**Agent Shell / Session Control**。

### `tail -f s1`

订阅 s1 的 Session Event Stream，持续显示：

```text
TaskStarted
ToolCallStarted
ToolCallCompleted
TextDelta
WaitingForUser
Completed
```

所属模块：**Event Stream / Observer**。

### `quit`

离开当前 Session。

### `exit`

退出整个 Agent Host。

这些都属于：

> **Agent Host / Application UX**

而不是 LLM Reasoning。

---

## 二十二、概念和所属模块总表

| 概念 | 所属层或模块 |
|---|---|
| LLM | Model Runtime |
| Prompt | Model / Context |
| Context Manager | Runtime Infrastructure |
| Context Engineering | Context 设计方法论 |
| State | Runtime State |
| Memory | Runtime Knowledge / State |
| Persistence | Runtime Infrastructure |
| Skill | Knowledge / SOP |
| Skill Manager | Runtime Capability Infrastructure |
| Tool | Capability |
| Function Calling | Tool Mechanism |
| MCP | Tool Integration Protocol |
| Prompt Chaining | Execution Pattern |
| Routing | Execution Pattern / Decision Mechanism |
| Parallelization | Execution Pattern |
| ReAct | Dynamic Execution Pattern |
| Planning | Planning / Execution Pattern |
| Reflection | Reasoning / Quality Pattern |
| Multi-Agent | System Architecture Pattern |
| Session | Runtime Instance / State Boundary |
| Session Manager | Host / Runtime Infrastructure |
| Multi-Session | Host / Orchestration |
| `sls` / `select` / `tail` / `quit` | Application / Agent Shell |
| LangGraph 等框架 | Agent Framework |

---

## 二十三、四层核心心智模型

### Layer 1：Host

解决：

> **用户怎么使用和管理 Agent？**

包括：

- UI
- CLI / Agent Shell
- Multi-Session
- Session Manager
- User Interaction

`sls`、`select`、`tail -f` 等命令位于这一层。

### Layer 2：Runtime

解决：

> **Agent 怎么持续运行？**

包括：

- Orchestrator
- Model Runtime
- Context Manager
- State
- Memory
- Persistence
- Skill Manager
- Tool Manager
- Observability

这是从零实现 Agent Runtime 时的核心范围。

### Layer 3：Execution / Reasoning

解决：

> **Agent 怎么解决问题？**

包括：

- Direct
- Prompt Chaining
- Routing
- Parallelization
- ReAct
- Planning
- Reflection

### Layer 4：Capability / Knowledge

解决：

> **Agent 知道什么，又能做什么？**

#### Knowledge / SOP

- Prompt
- Skill
- References
- RAG Knowledge

#### Capability

- Tool
- Function Calling
- MCP
- API
- Script

---

## 二十四、最终定义

> **Agent = LLM 作为不确定性决策核心，在 Runtime 提供的 Context、State、Memory、Skill 和 Tool 基础设施之上，通过 Chain、Routing、ReAct、Planning、Reflection 等执行或推理模式持续推进任务，直到达到目标或满足停止条件。**

对于多 Session 系统：

> **Agent Host = 管理多个独立 Agent Session 的运行环境，为用户提供类似操作系统 Shell 的任务控制体验。**

最终可以用两个维度理解整个体系：

### 纵向架构

```text
Host → Runtime → Execution → Capability
```

### 横向执行模式

```text
Direct / Chain / Route / Parallel / ReAct / Plan / Reflect
```

这个框架可以作为后续从零实现 Mini Agent Runtime 的总体架构。

# Agent Runtime：从本地模块到独立 Service 的演进设计

## 1. 文档目的

本文档补充 `Mini Agent Runtime` 的模块化单体设计，重点回答两个问题：

1. 当前所有组件都作为同一个 .NET Project 中的模块实现时，如何保留未来拆分为独立 Service 的清晰边界？
2. 如何让调用方在不修改业务逻辑的情况下，在本地模块实现和远程 Service 实现之间切换？

设计目标不是在第一版直接建设分布式 Agent Runtime，而是：

> **运行时先采用单进程 Modular Monolith，但模块契约按照未来可能远程化的边界设计。**

这样可以同时学习：

- Agent Runtime 的核心机制
- 模块职责和交互契约
- 模块服务化需要的技术支撑
- 本地调用与远程调用的差异
- 分布式系统引入的延迟、失败、一致性和可观测性问题

---

## 2. 核心设计结论

### 2.1 当前形态

```text
Single Process

Agent Runtime
├── Context Manager
├── Working Memory Manager
├── Long-term Memory Manager
├── State Manager
├── Tool Manager
├── Skill Manager
├── Model Runtime
├── Execution Engine
└── Orchestrator
```

各模块通过进程内接口调用：

```csharp
await _workingMemory.ListRecentAsync(...);
await _longTermMemory.SearchAsync(...);
```

### 2.2 未来形态

某些模块可以演进为独立 Service：

```text
Agent Runtime
     │
     ▼
ILongTermMemoryManager
     │
RemoteLongTermMemoryManager
     │
HTTP / gRPC
     │
Memory Service
     │
Database / Search Index
```

调用方仍然只依赖：

```csharp
ILongTermMemoryManager
```

### 2.3 无缝切换的真实含义

“无缝切换”应该理解为：

- 调用方代码不需要修改
- 调用方依赖的 Domain Contract 不变
- 通过 Dependency Injection 切换具体 Adapter

但不能理解为：

- 本地调用和远程调用具有完全相同的性能
- 远程调用不会失败
- 本地和远程具有相同的一致性语义
- 所有本地接口都天然可以映射为 RPC

更准确的表述是：

> **API 使用方式可以保持稳定，但远程实现必须显式处理网络和分布式系统语义。**

---

## 3. 推荐的 Contract + Adapter 架构

对于可能远程化的模块，建议采用：

```text
                Domain Contract
                  IXXXManager
                       │
             ┌─────────┴─────────┐
             │                   │
      In-Process Adapter     Remote Adapter
             │                   │
       Local Component      Service Client
                                 │
                         HTTP / gRPC / MCP
                                 │
                          Remote Service
```

记忆分为两种访问能力：`IWorkingMemoryManager` 按 Owner 直接读取，
`ILongTermMemoryManager` 按 Owner 和 Query 进行相关性检索。
Task / Session 与 User / Global 分别是默认归属策略，不是存储或期限的硬绑定。
工作记忆可持久化；长期搜索的索引可异步、离线或增量构建。

以下以更适合迁移检索基础设施的长期记忆为例，工作记忆优先保留在 Runtime 本地：

```text
                    ILongTermMemoryManager
                    /             \
                   /               \
       LocalLongTermMemoryManager     RemoteLongTermMemoryManager
               │                       │
       LongTermMemoryStore       ILongTermMemoryServiceClient
                                       │
                                 HTTP / gRPC
```

---

## 4. Domain Contract 与 Transport Contract

不建议直接把 Runtime 的 Domain Interface 等同于远程 API。

应该区分两类契约。

## 4.1 Domain Contract

Domain Contract 表达 Runtime 所需要的业务能力：

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

public interface ILongTermMemoryManager : IMemoryWriter
{
    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        MemorySearchRequest query,
        CancellationToken cancellationToken);
}
```

调用者只依赖此接口。

IWorkingMemoryManager 同样继承 IMemoryWriter，仅增加自己的直接读取操作。
共享接口不统一两种实现的写后可见性，也不要求共享存储；只依赖 IMemoryWriter 的写入流程，
仍须在组合入口显式指定目标 Manager，不配置含糊的默认写入目标。

工作记忆的独立接口使用 `ListRecentAsync(MemoryReadRequest)` 返回 `MemoryItem` 列表，
不接受检索 Query，也不返回相关性 Score。两种能力的完整契约见设计初稿第 13 节。
契约目录分为 Models 和 Operations，请求与响应嵌套在 Operations 下，命名空间分别为
`HanyingAgent.Contracts.Memory.Models`、`HanyingAgent.Contracts.Memory.Operations`、
`HanyingAgent.Contracts.Memory.Operations.Requests` 和 `HanyingAgent.Contracts.Memory.Operations.Responses`；
本文示例省略对应 using，适配器实现按实际使用的类型导入。
短期到长期的整合是提取、去重后创建新记忆，不再用修改 Scope 的 Promote 操作表达。

## 4.2 Transport Contract

Transport Contract 表达远程 Service 的序列化请求和响应：

```csharp
public interface ILongTermMemoryServiceClient
{
    Task<SearchMemoryResponse> SearchAsync(
        SearchMemoryRequest request,
        CancellationToken cancellationToken);

    Task<WriteMemoryResponse> WriteAsync(
        WriteMemoryRequest request,
        CancellationToken cancellationToken);
}
```

Remote Adapter 负责在二者之间转换：

```text
MemorySearchRequest
    ↓ map
SearchMemoryRequest
    ↓ RPC
SearchMemoryResponse
    ↓ map
MemorySearchResult
```

---

## 5. 为什么 Domain Interface 不能直接等同 RPC API

进程内接口可以使用很多不适合跨网络的抽象。

例如：

```csharp
Task<AgentState> UpdateAsync(
    TaskId taskId,
    Func<AgentState, AgentState> updater);
```

这个接口适合本地对象调用，但 `Func<AgentState, AgentState>` 无法通过 HTTP 或 gRPC 发送。

如果该模块未来可能服务化，更合适的契约是：

```csharp
public sealed record UpdateStateRequest(
    TaskId TaskId,
    long ExpectedVersion,
    IReadOnlyList<StateMutation> Mutations,
    string OperationId);
```

```csharp
public sealed record UpdateStateResponse(
    AgentState State,
    long NewVersion);
```

这类 Request / Response：

- 可以 JSON 序列化
- 可以转换成 Protocol Buffers
- 可以记录日志
- 可以重放
- 可以进行版本管理
- 可以实现幂等处理

---

## 6. 可远程化 Contract 的设计规则

可能远程化的模块，从第一版开始应遵守以下规则。

### 6.1 全部使用异步接口

推荐：

```csharp
Task<AgentState> GetAsync(
    GetStateRequest request,
    CancellationToken cancellationToken);
```

不推荐：

```csharp
AgentState GetState(TaskId taskId);
```

原因：远程实现一定涉及 I/O、等待、超时和取消。

### 6.2 接受 `CancellationToken`

所有可能执行 I/O 的调用都应支持取消：

```csharp
Task<T> ExecuteAsync(..., CancellationToken cancellationToken);
```

### 6.3 使用可序列化 Request / Response

推荐：

```csharp
SearchMemoryRequest
SearchMemoryResponse
GetStateRequest
GetStateResponse
InvokeToolRequest
InvokeToolResponse
```

避免在边界上暴露：

- `Func<>`
- Delegate
- Callback
- `IQueryable`
- 可变共享对象
- 进程内对象引用
- 任意 `object`
- Provider 特有的返回对象

### 6.4 返回不可变对象

优先使用：

```csharp
record
readonly record struct
IReadOnlyList<T>
```

避免把模块内部的可变集合直接暴露给调用方。

### 6.5 显式设计版本与并发控制

例如 State 更新：

```csharp
public sealed record UpdateStateRequest(
    TaskId TaskId,
    long ExpectedVersion,
    IReadOnlyList<StateMutation> Mutations,
    string OperationId);
```

`ExpectedVersion` 可用于：

- Optimistic Concurrency
- Compare-And-Swap
- ETag
- 防止 Lost Update

### 6.6 显式设计幂等性

对于以下操作应考虑 `OperationId` 或 `IdempotencyKey`：

- Write Memory
- Update State
- Create Checkpoint
- Execute Side-effect Tool
- Create Session

示例：

```csharp
public sealed record WriteMemoryRequest(
    string OperationId,
    SessionId SessionId,
    string Content,
    MemoryType Type);
```

### 6.7 不隐藏远程失败语义

远程 Adapter 需要区分：

- Timeout
- Authentication Failure
- Authorization Failure
- Conflict
- Rate Limit
- Service Unavailable
- Invalid Request
- Serialization Failure

不要把所有错误都转成：

```text
Exception: operation failed
```

---

## 7. 本地与远程实现示例

### 7.1 长期记忆本地实现

以下仅展示写入与检索调用，省略构造函数、Update / Forget 和输入校验，均为后续实现示意。
Write 保存权威记录；索引构建由独立流程负责，不在每次 Agent 调用里同步重建索引。

```csharp
public sealed class LocalLongTermMemoryManager : ILongTermMemoryManager
{
    private readonly ILongTermMemoryStore _store;
    private readonly ILongTermMemoryRetriever _retriever;

    public Task<MemoryItem> WriteAsync(
        MemoryWriteRequest request,
        CancellationToken cancellationToken)
    {
        return _store.WriteAsync(request, cancellationToken);
    }

    public Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        MemorySearchRequest query,
        CancellationToken cancellationToken)
    {
        return _retriever.SearchAsync(query, cancellationToken);
    }
}
```

### 7.2 长期记忆远程实现

```csharp
public sealed class RemoteLongTermMemoryManager : ILongTermMemoryManager
{
    private readonly ILongTermMemoryServiceClient _client;

    public async Task<MemoryItem> WriteAsync(
        MemoryWriteRequest request,
        CancellationToken cancellationToken)
    {
        var remoteRequest = MemoryMapper.ToRemoteRequest(request);

        var response = await _client.WriteAsync(
            remoteRequest,
            cancellationToken);

        return MemoryMapper.ToDomain(response.Memory);
    }

    public async Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        MemorySearchRequest query,
        CancellationToken cancellationToken)
    {
        var remoteRequest = MemoryMapper.ToRemoteRequest(query);

        var response = await _client.SearchAsync(
            remoteRequest,
            cancellationToken);

        return response.Results
            .Select(MemoryMapper.ToDomain)
            .ToArray();
    }
}
```

### 7.3 Dependency Injection 切换

```csharp
services.AddSingleton<IWorkingMemoryManager, LocalWorkingMemoryManager>();

if (configuration.LongTermMemory.Mode == "Local")
{
    services.AddSingleton<ILongTermMemoryManager, LocalLongTermMemoryManager>();
}
else
{
    services.AddHttpClient<ILongTermMemoryServiceClient, LongTermMemoryServiceClient>();
    services.AddSingleton<ILongTermMemoryManager, RemoteLongTermMemoryManager>();
}
```

调用方不需要修改：

```csharp
var workingMemories = await _workingMemory.ListRecentAsync(
    readRequest,
    cancellationToken);

var longTermMemories = await _longTermMemory.SearchAsync(
    query,
    cancellationToken);
```

---

## 8. 各模块的远程化潜力

| 模块 | 独立 Service 潜力 | 建议 |
|---|---:|---|
| State Manager | ★★★★★ | 非常适合服务化 |
| Working Memory | ★★☆☆☆ | 高频直接读取，优先保留本地；需要共享或恢复时持久化 |
| Long-term Memory | ★★★★★ | 适合服务化，复用存储与检索基础设施 |
| Model Runtime / Model Gateway | ★★★★★ | 天然适合远程调用 |
| Tool Provider | ★★★★★ | 天然支持 Local / Remote |
| Observability Backend | ★★★★★ | 通常本身就是外部平台 |
| Skill Repository | ★★★★☆ | 适合集中注册和版本管理 |
| Persistence | ★★★☆☆ | 更适合通过 Storage Adapter 演进 |
| Session Manager | ★★☆☆☆ | Scale-out 后再考虑 |
| Context Manager | ★★☆☆☆ | 优先保留在 Runtime 内 |
| Execution Engine | ★☆☆☆☆ | 属于 Runtime 核心 |
| Orchestrator | ★☆☆☆☆ | 属于 Runtime 核心 |

---

## 9. 建议服务化的模块

## 9.1 State Manager

### 本地形态

```text
Execution Engine
      ↓
IStateManager
      ↓
LocalStateManager
      ↓
In-memory Dictionary / SQLite
```

### 远程形态

```text
Execution Engine
      ↓
IStateManager
      ↓
RemoteStateManager
      ↓
HTTP / gRPC
      ↓
State Service
      ↓
Redis / PostgreSQL / Cosmos DB
```

### 需要的技术

- ASP.NET Core Minimal API 或 gRPC
- Redis、PostgreSQL 或 Cosmos DB
- ETag / Optimistic Concurrency
- Compare-And-Swap
- Durable Checkpoint
- High Availability
- Backup / Restore
- Application-level Idempotency
- Microsoft Entra ID
- Managed Identity
- OpenTelemetry
- Retry / Timeout / Circuit Breaker

### 新增的分布式问题

- 并发更新冲突
- Lost Update
- State Version Skew
- 网络分区
- 重复提交
- Checkpoint 写入成功但响应丢失
- Runtime 恢复时读取旧版本
- 跨 Worker 的 ownership 和 lease

### 典型并发场景

```text
State version = 7

Worker A reads version 7
Worker B reads version 7

Worker A writes version 8
Worker B attempts to write based on version 7
```

State Service 应返回：

```text
409 Conflict
```

或者 gRPC 对应的 conflict status，由 Worker 重新读取并决定下一步。

---

## 9.2 工作记忆与长期记忆

### 工作记忆：按归属直接读取

```text
Context Manager
      ↓
IWorkingMemoryManager.ListRecentAsync(owner, limit)
      ↓
Working Memory Store（KV / 内存 / 数据库）
      ↓
最近有效记忆，无关键词匹配和相关性分数
```

上图参数为逻辑简写，实际接口接收 MemoryReadRequest。
Write / Update / Forget 成功后，后续读取应反映变更；默认用于 Task / Session，
不依赖长期搜索索引。数据持久化与否由实现决定，不由 Scope 决定。

### 长期记忆：本地形态

```text
Context Manager
      ↓
ILongTermMemoryManager
      ↓
LocalLongTermMemoryManager
      ↓
Long-term Store + Retrieval Adapter
```

### 长期记忆：远程形态

```text
Context Manager
      ↓
ILongTermMemoryManager
      ↓
RemoteLongTermMemoryManager
      ↓
Memory Service
      ├── Metadata Store
      ├── Raw Content Store
      ├── Keyword Index
      └── Vector Index
```

### 需要的技术

- ASP.NET Core 或 gRPC
- PostgreSQL / Cosmos DB
- Azure AI Search 或其他 Search Engine
- Keyword / BM25 Retrieval
- Embedding Service
- Vector Search
- Hybrid Retrieval
- Reranking
- Indexing Pipeline
- Cache
- TTL / Expiration
- Deduplication
- Memory Versioning
- OpenTelemetry
- Microsoft Entra ID / Managed Identity

### Write Path

```text
Memory Candidate
      ↓
Normalize
      ↓
Validate / Ground
      ↓
Deduplicate
      ↓
Persist Metadata and Raw Content
      ↓
WriteAsync returns: authoritative data saved
      ↓
Reliable Change Capture / Offline Scan
      ↓
Tokenize / Optional Embedding
      ↓
Build or Update Search Index
```

索引是可重建的派生数据；离线批处理或实时增量更新均可。
词项和业务标签分开建模，索引不承诺比原始数据占用更少空间。
更新、删除、过期也必须反映到查询结果中；可靠传播可使用 Outbox / CDC 或可恢复的批处理。
这些是未来基础设施选择，不是本次要实现的服务。

### Read Path

```text
Memory Query
      ↓
Metadata Filter
      ↓
BM25 Recall + Vector Recall
      ↓
Hybrid Merge
      ↓
Rank / Rerank
      ↓
Read and Validate Authoritative Records
      ↓
Top K
```

BM25 / 向量 / 混合检索是后续可选后端，不要求同时实现。
搜索返回当前权威数据，并验证归属、访问权限、状态和有效期，剔除删除项与失效项。
Write 成功不承诺索引已刷新，刚产生的执行信息通过工作记忆或 State 直接进入 Context。
Context 将工作记忆与长期检索结果合并、去重并按 Token 预算组装，不比较两种来源的原始分数。
用户固定偏好也可按归属直接读取；需要立即生效的变更不能只等待离线索引。

### 新增的分布式问题

- Index Freshness
- Write Store 成功但索引尚未更新
- Embedding Model Version
- Index Rebuild
- Duplicate Memory
- Stale Memory
- Retrieval Quality Regression
- Cache Invalidation
- 数据隔离和用户权限

---

## 9.3 Model Runtime / Model Gateway

Model Provider 通常已经是远程 Service：

```text
Model Runtime
      ↓
Model Client
      ↓
Azure OpenAI / OpenAI / Other Provider
```

### 需要的技术

- `HttpClientFactory`
- Streaming HTTP / SSE
- Provider SDK
- Managed Identity 或 API Key
- Retry
- Timeout
- Circuit Breaker
- Rate Limiting
- Token Quota
- Model Routing
- Fallback Model
- Prompt / Response Logging Policy
- Content Safety
- OpenTelemetry

### 可选的独立 Model Gateway

```text
Agent Runtime
      ↓
Model Gateway
      ├── Azure OpenAI Deployment A
      ├── Azure OpenAI Deployment B
      └── Other Model Provider
```

Model Gateway 可统一处理：

- Model Routing
- Provider Authentication
- Rate Limiting
- Token and Cost Accounting
- Response Normalization
- Retry and Fallback
- Audit and Safety Policy

---

## 9.4 Tool Provider

Tool Manager 本身可以保留在 Runtime 内，但具体 Tool Provider 可以本地或远程。
当前公共工具契约已定义在 Contracts/Tools：按任务列举允许使用的定义，
InvokeAsync 等待本次调用并返回 ToolObservation，不使用提交后轮询协议。
ToolResult 与 ToolObservation 共享 Succeeded、Failed、Cancelled、OutcomeUnknown 状态，
避免将响应丢失误判为未执行。本地注册与具体工具实现协议不放入远程公共接口。

```text
Tool Manager
    ├── Local Function Tool
    ├── REST Tool Adapter
    ├── gRPC Tool Adapter
    └── MCP Tool Adapter
```

### 需要的技术

- JSON Schema
- REST / gRPC
- MCP Client
- Authentication
- Permission Check
- Tool Timeout
- Retry Policy
- Idempotency Key
- Side-effect Classification
- Result Size Limit
- Artifact Store
- Distributed Trace Propagation

### Side-effect Tool 的特殊要求

例如：

```text
Create Work Item
Send Email
Delete Resource
Deploy Service
```

必须考虑：

- 重复调用
- 响应丢失
- Retry 是否安全
- 幂等键
- 人工审批
- Compensation / Rollback

---

## 9.5 Skill Repository

### 本地形态

```text
skills/
  incident-investigation/
    SKILL.md
    references/
```

### 远程形态

```text
Skill Manager
      ↓
Skill Registry / Repository Service
```

### 需要的技术

- Skill Metadata Index
- Package Versioning
- Artifact Storage
- Integrity Validation
- Digital Signature，可选
- Cache
- Rollback
- Dependency Declaration
- Access Control
- Compatibility Version

第一版仍建议使用本地文件目录。

---

## 9.6 Observability Backend

Runtime 内保留 Event Hub 和 Telemetry Adapter：

```text
Runtime Modules
      ↓
IAgentEventHub / OpenTelemetry
      ↓
Remote Observability Backend
```

可使用：

- OpenTelemetry
- Azure Monitor
- Application Insights
- Prometheus
- Grafana
- Jaeger
- OpenTelemetry Collector

必须传播的标识：

```text
TraceId
SpanId
SessionId
TaskId
ToolCallId
ModelInvocationId
CheckpointId
```

使用 W3C Trace Context：

```text
traceparent
tracestate
```

---

## 10. 不建议优先服务化的模块

## 10.1 Context Manager

Context Manager 位于每一轮 Model Invocation 的 Hot Path：

```text
State
Memory
Skill
Observations
      ↓
Context Manager
      ↓
Model Runtime
```

优先留在 Agent Runtime 内的原因：

- 与当前 Execution State 紧密耦合
- 每轮调用都需要执行
- 对延迟敏感
- 需要整合多个本地运行时对象
- 远程化容易形成多层级联调用

不推荐：

```text
Agent Runtime
      ↓ HTTP
Context Service
      ↓ HTTP
Memory Service
      ↓ HTTP
Skill Service
```

这会增加：

- Tail Latency
- Partial Failure
- Distributed Trace 复杂度
- Context Consistency 问题
- Distributed Spaghetti 风险

Context Manager 可以调用远程 Memory / State / Skill 接口，但自身优先保持在 Runtime 内。

---

## 10.2 Execution Engine

Execution Engine 包含：

- Direct Executor
- Prompt Chain Executor
- ReAct Executor
- Future Plan-and-Execute Executor

它是 Agent Runtime 的核心控制机制。

如果将其远程化，实际上已经开始拆分整个 Runtime，而不仅是拆分普通数据能力模块。

第一阶段不建议单独部署。

---

## 10.3 Orchestrator

Orchestrator 管理：

- Task 生命周期
- Skill Selection
- Strategy Selection
- Execution Engine 调度
- Checkpoint 时机
- Completion / Failure

它应与 Runtime 保持在同一进程或同一部署单元中。

---

## 11. 本地调用切换为远程调用时需要的基础技术

## 11.1 Transport

可选方案：

- HTTP REST
- gRPC
- Server-Sent Events
- WebSocket
- MCP，用于 Tool 能力接入

推荐：

- 普通 CRUD / 查询：HTTP REST
- 强类型、低延迟内部 API：gRPC
- 增量模型输出和事件：SSE、WebSocket 或 gRPC Streaming
- Tool 标准化接入：MCP

---

## 11.2 Serialization

- JSON
- Protocol Buffers

JSON 优点：

- 容易调试
- 易于抓包
- 与 HTTP REST 配合自然

Protocol Buffers 优点：

- 强类型
- Payload 较小
- 适合 gRPC
- Contract 演进规则更清晰

---

## 11.3 Authentication 与 Authorization

建议考虑：

- Microsoft Entra ID
- OAuth 2.0
- Managed Identity
- Service-to-Service Access Token
- Role / Scope
- Tenant Isolation

例如：

```text
Agent Runtime Managed Identity
      ↓ access token
Memory Service
```

Memory 和 State 特别需要按以下维度隔离：

```text
TenantId
UserId
AgentId
SessionId
TaskId
```

---

## 11.4 Resilience

.NET 技术：

- `IHttpClientFactory`
- `Microsoft.Extensions.Http.Resilience`
- Polly-compatible resilience pipeline

需要的策略：

- Timeout
- Retry
- Exponential Backoff
- Jitter
- Circuit Breaker
- Rate Limit
- Bulkhead / Concurrency Limit

注意：

> 并非所有操作都可以自动 Retry。

安全 Retry 的典型操作：

- Get State
- Search Memory
- List Skills

需要幂等键后才能安全 Retry：

- Write Memory
- Update State
- Create Checkpoint
- Side-effect Tool

---

## 11.5 Service Discovery 与配置

本地开发：

```text
http://localhost:5001
```

生产环境可能使用：

- DNS
- Kubernetes Service
- Azure Container Apps Environment
- Azure API Management
- Azure Service Fabric Naming
- Configuration Service

配置示例：

```json
{
  "Memory": {
    "Mode": "Remote",
    "Endpoint": "https://memory.internal",
    "TimeoutSeconds": 2
  },
  "State": {
    "Mode": "Local"
  }
}
```

---

## 11.6 Contract Versioning

远程 Service 必须考虑：

- Client 和 Server 版本不一致
- 新字段增加
- 字段废弃
- Enum 新值
- 数据迁移

建议：

- Request / Response 显式版本
- 字段只增加不重用
- Unknown Enum Value 可容忍
- API Compatibility Tests
- Consumer-driven Contract Tests，可选

---

## 11.7 Observability

每个远程调用需要记录：

- Endpoint
- Operation Name
- Request Duration
- Status Code
- Retry Count
- Timeout
- Payload Size
- TraceId
- SessionId
- TaskId

不要记录未脱敏的：

- 完整 Prompt
- 敏感 Memory
- Access Token
- 用户隐私数据
- 大型 Tool Raw Result

---

## 12. 本地和远程实现的测试策略

## 12.1 Contract Test Suite

为每个 Domain Contract 建立共享测试：

```csharp
public abstract class LongTermMemoryManagerContractTests
{
    protected abstract ILongTermMemoryManager CreateManager();

    [Fact]
    public async Task PersistedMemoryBecomesSearchableAfterIndexing()
    {
        // Same test for Local and Remote implementations.
    }
}
```

运行两套实现：

```text
LocalLongTermMemoryManagerContractTests
RemoteLongTermMemoryManagerContractTests
```

长期检索测试需显式推进索引构建或等待带超时的索引就绪条件，不能假设写后立即搜到。
工作记忆单独测试按 Owner 直接读取、有效项过滤、更新时间排序，以及写入 / 更新 / 删除后的立即可见性。
两种能力共用的测试包括归属隔离、默认 ID 拒绝、非法字段拒绝和错误传播。

目标：

> Local Adapter 和 Remote Adapter 在业务语义上满足同一个 Contract。

## 12.2 Failure Injection

远程实现需要测试：

- Timeout
- 503 Service Unavailable
- 429 Too Many Requests
- Partial Response
- Serialization Failure
- 409 State Conflict
- Duplicate Request
- Client Cancellation

## 12.3 Latency Injection

模拟：

```text
P50 = 20 ms
P95 = 200 ms
P99 = 1 s
```

观察 Agent Runtime：

- 总耗时
- Timeout 行为
- Retry 放大
- Context 构建延迟
- ReAct Loop 延迟

---

## 13. 建议的 Project 结构调整

以 Contracts + 适配器思想保留远程替换能力，目录按五种角色分组：

以下是演进阶段的逻辑组织示例。当前 `HanyingAgent` 项目骨架采用设计初稿第 4 节的目录结构，
将具体适配器统一放在 `Infrastructure` 下，暂不单独创建 `Adapters` 目录或远程实现。
应用项目位于工作区的 `HanyingAgent`，模块目录位于其 `SRC` 子目录；
同在项目根目录下的 `TEST` 预留给后续单元测试项目，应用项目显式排除 TEST 子树。
项目文件、Program.cs、bin / obj 不移入 SRC。SRC 顶层只保留 Contracts、Host、Runtime、Capabilities 和 Infrastructure。
下图展示模块分组及演进方向，不代表已创建相应实现或测试框架。

当前 Task 公共入口为 ITaskManager：SubmitAsync 接受后返回 TaskInfo，不等待整个执行完成；
后续通过 GetAsync、GetResultAsync 查询，通过 ProvideInputAsync 回答等待，通过 CancelAsync 请求取消。
同一 Session 最多一个未结束任务，忙时拒绝而不排队。远程适配器必须保留这些语义，
明确处理调用取消与业务取消的区别，以及超时后是否已经接收任务的不确定性；
当前没有提交幂等键，不把网络重试包装成无条件安全的操作。

当前 IUserManager 只管理独立用户资料，提供创建、读取、列举和显示名称更新，不是认证服务。
远程适配器需保留未知查询返回 null、未知更新报错的区别；UserId 不作为调用者权限凭证。
SessionInfo 与创建请求已要求 UserId，形成一个用户多个会话、一个会话一个用户的归属；
远程创建、恢复与访问需校验用户及权限，不能用当前调用者替代缺失的持久化归属。
旧请求或会话 JSON 缺少 UserId 时显式报错，需明确迁移；尚未实现用户存储，不添加多租户或外部身份绑定。

当前 ILanguageModelRuntime 定义一次非流式生成，使用类型化消息与可用工具定义，
返回文本、完整工具调用、明确结束原因和可空用量；不执行工具，也不决定 Task 是否完成。
ModelToolMessage 复用 ToolObservation，远程适配需保留工具角色、调用 ID、状态、错误和必要输出引用，
不能将工具交互压平成普通文本。Provider SDK、密钥和实际网络调用仍留在 Infrastructure/LanguageModels。

IConversationManager 已定义稳定 MessageId 的追加、按 ID 读取和按 Session 分页读取，
远程响应丢失后复用原 MessageId 重试；同 ID 不同载荷报冲突，不生成重复历史。
服务端分配 Sequence，首次读取最近 20 条、页内升序，游标读取更早记录；
历史记录独立于 ModelMessage，保留工具关联、失败和结束信息。去重、权限及存储仍待实现。

```text
SRC
│
├── Contracts
│   ├── Sessions
│   ├── Tasks
│   │   ├── Models
│   │   │   ├── TaskId.cs
│   │   │   ├── TaskInfo.cs
│   │   │   └── TaskStatus.cs
│   │   └── Operations
│   │       ├── ITaskManager.cs
│   │       ├── Requests
│   │       │   ├── SubmitTaskRequest.cs
│   │       │   ├── ProvideTaskInputRequest.cs
│   │       │   └── ListSessionTasksRequest.cs
│   │       └── Responses
│   │           ├── TaskListResponse.cs
│   │           └── TaskResult.cs
│   ├── Users
│   │   ├── Models
│   │   │   ├── UserId.cs
│   │   │   └── UserInfo.cs
│   │   └── Operations
│   │       ├── IUserManager.cs
│   │       └── Requests
│   │           ├── CreateUserRequest.cs
│   │           └── UpdateUserRequest.cs
│   ├── Conversation
│   │   ├── Models
│   │   └── Operations
│   │       ├── Requests
│   │       └── Responses
│   ├── Memory
│   │   ├── Models
│   │   │   ├── MemoryItem.cs
│   │   │   ├── MemoryId.cs
│   │   │   ├── MemoryOwner.cs
│   │   │   ├── MemoryScope.cs
│   │   │   ├── MemoryType.cs
│   │   │   └── MemoryStatus.cs
│   │   └── Operations
│   │       ├── IMemoryWriter.cs
│   │       ├── IWorkingMemoryManager.cs
│   │       ├── ILongTermMemoryManager.cs
│   │       ├── Requests
│   │       │   ├── MemoryWriteRequest.cs
│   │       │   ├── MemoryUpdateRequest.cs
│   │       │   ├── MemoryReadRequest.cs
│   │       │   └── MemorySearchRequest.cs
│   │       └── Responses
│   │           └── MemorySearchResult.cs
│   ├── Tools
│   │   ├── Models
│   │   └── Operations
│   │       ├── Requests
│   │       └── Responses
│   └── LanguageModels
│       ├── Models
│       └── Operations
│           ├── Requests
│           └── Responses
│
├── Host
│   ├── AgentShell
│   └── CommandParser
│
├── Runtime
│   ├── Orchestration
│   ├── Execution
│   ├── Context
│   ├── Conversation
│   └── State
│
├── Capabilities
│   ├── Memory
│   │   ├── LocalWorkingMemoryManager.cs
│   │   ├── LocalLongTermMemoryManager.cs
│   │   ├── IWorkingMemoryStore.cs
│   │   ├── ILongTermMemoryStore.cs
│   │   └── ILongTermMemoryRetriever.cs
│   ├── Skills
│   └── Tools
│
└── Infrastructure
    ├── LanguageModels
    ├── Persistence
    ├── Observability
    ├── Memory                    后续需要时创建
    │   ├── RemoteLongTermMemoryManager.cs
    │   └── LongTermMemoryServiceClient.cs
    └── State                     后续需要时创建
        ├── RemoteStateManager.cs
        └── StateServiceClient.cs
```

Runtime 依赖能力契约，Capabilities 实现业务规则并定义内部依赖端口，
Infrastructure 实现具体存储、检索与远程调用，不能反过来让业务模块依赖某个数据库或客户端。
Host 只适配用户交互，Session 生命周期协调放在 Runtime/Orchestration。
这些是逻辑分组，不是微服务边界；仍保持单 Project、单进程，由项目根入口完成组合。

对于学习项目，不必一开始实现所有 Remote Adapter。

建议先挑选两个模块：

1. Long-term Memory Manager（工作记忆先保持本地）
2. State Manager

为这两个模块同时实现 Local 和 Remote 版本，可以充分体验：

- Search Service 类型的系统
- Strong State / Checkpoint 类型的系统
- 两种服务完全不同的工程关注点

---

## 14. 推荐的学习演进路径

### Phase 1：全部本地模块

```text
100% In-Process
```

目标：理解 Agent Runtime。

### Phase 2：统一契约和 Adapter

为以下模块建立可序列化 Contract：

- Memory
- State
- Tool
- Model

目标：理解 Service Boundary。

### Phase 3：Remote Memory Service

技术组合示例：

```text
ASP.NET Core Minimal API
HTTP JSON
SQLite / PostgreSQL
Keyword Retrieval
OpenTelemetry
Microsoft Entra ID，可后置
```

目标：体验 Retrieval Service。

### Phase 4：Remote State Service

技术组合示例：

```text
ASP.NET Core / gRPC
Redis / PostgreSQL
Version / ETag
Optimistic Concurrency
Checkpoint
OpenTelemetry
```

目标：体验 Stateful Service。

### Phase 5：Local / Remote 混合部署

例如：

```text
Context Manager: Local
Execution Engine: Local
Orchestrator: Local
Working Memory Manager: Local
Long-term Memory Manager: Remote
State Manager: Remote
Tool Providers: Local + Remote
Model Provider: Remote
```

目标：观察调用链、延迟和失败传播。

### Phase 6：故障和弹性实验

主动注入：

- 500 ms Memory latency
- State 409 conflict
- Model 429
- Tool timeout
- Memory Service unavailable

验证：

- State Failure 是否暂停 Agent
- Long-term Memory Failure 是否按明确策略降级为工作记忆 Context，并发布可观察的错误或降级事件
- Tool Retry 是否满足幂等性
- Trace 是否能串联完整调用链

---

## 15. 每个模块的 Service Evolution 说明模板

建议在设计文档中为每个模块都增加以下小节：

```text
Module: Long-term Memory Manager

Current Deployment:
- In-process module

Remote Service Candidate:
- Yes

Domain Contract:
- ILongTermMemoryManager

Remote Operations:
- SearchMemory
- WriteMemory
- UpdateMemory
- ForgetMemory

Potential Technologies:
- ASP.NET Core
- gRPC / HTTP
- PostgreSQL / Cosmos DB
- Azure AI Search
- OpenTelemetry
- Microsoft Entra ID

Distributed Concerns:
- Retrieval latency
- Index freshness
- Duplicate memory
- Embedding version
- Cache invalidation
- Tenant isolation

Failure Policy:
- Retry read once
- Fallback to short-term context
- Do not block Agent indefinitely
```

State Manager 示例：

```text
Module: State Manager

Current Deployment:
- In-process module

Remote Service Candidate:
- Yes

Domain Contract:
- IStateManager

Remote Operations:
- GetState
- UpdateState
- CreateCheckpoint
- RestoreCheckpoint

Potential Technologies:
- ASP.NET Core / gRPC
- Redis / PostgreSQL / Cosmos DB
- ETag / Optimistic Concurrency
- OpenTelemetry
- Microsoft Entra ID

Distributed Concerns:
- Consistency
- Concurrent update
- Lost update
- Checkpoint durability
- Recovery
- Idempotency

Failure Policy:
- Retry safe reads
- Pause Agent if State cannot be safely read or updated
- Never continue from unknown State
```

---

## 16. 关键架构原则

### 原则一：接口稳定，部署可变

```text
Consumer → Domain Contract → Adapter → Local or Remote
```

### 原则二：Domain Contract 不依赖 Transport

Runtime 不应该知道：

- HTTP 状态码
- gRPC Stub
- Provider SDK 类型
- 数据库 Entity

这些由 Adapter 消化。

### 原则三：不要为了未来服务化而过度抽象

只有明确可能远程化的模块才需要：

- Request / Response Contract
- Local / Remote Adapter
- Client Abstraction

不要为每个普通 Helper 都设计 Remote Adapter。

### 原则四：远程调用不是本地函数调用

必须从设计上接受：

- Latency
- Timeout
- Partial Failure
- Retry
- Duplicate Request
- Version Skew
- Network Partition

### 原则五：不同模块的 Service 化重点不同

State Service 优化：

```text
Reliability
Consistency
Recoverability
Low latency
```

Memory Service 优化：

```text
Retrieval quality
Freshness
Lifecycle
Scalability
```

Model Gateway 优化：

```text
Quota
Routing
Fallback
Cost
Safety
```

Tool Provider 优化：

```text
Permission
Idempotency
Side-effect control
Result normalization
```

---

## 17. 最终推荐架构

第一阶段：

```text
Single Process Modular Monolith

Agent Runtime
├── Orchestrator
├── Execution Engine
├── Context Manager
├── Local State Manager
├── Local Working Memory Manager
├── Local Long-term Memory Manager
├── Local Skill Manager
├── Tool Manager
├── Model Runtime
└── Observability
```

第二阶段：

```text
Hybrid Runtime

Agent Runtime
├── Orchestrator                Local
├── Execution Engine            Local
├── Context Manager             Local
├── State Manager Adapter       Local / Remote
├── Working Memory Adapter      Local first
├── Long-term Memory Adapter    Local / Remote
├── Skill Repository Adapter    Local / Remote
├── Tool Manager                Local
│   └── Tool Providers          Local / REST / gRPC / MCP
├── Model Runtime
│   └── Model Provider          Remote
└── Observability Adapter
    └── Telemetry Backend       Remote
```

第三阶段，只有当规模和需求真实出现时，再考虑：

- Runtime Scale-out
- Distributed Session Ownership
- Remote Session Manager
- Event Bus
- Worker Pool
- Lease / Scheduler
- Multi-Agent Runtime

---

## 18. 总结

对于这个学习项目，最佳策略是：

> **代码实现采用单 Project 的 Modular Monolith，模块边界采用未来可服务化的 Contract + Adapter 设计。**

调用方依赖稳定的 Domain Contract：

```text
IStateManager
IMemoryWriter
IWorkingMemoryManager
ILongTermMemoryManager
ILanguageModelRuntime
IToolManager
ISkillRepository
```

具体实现通过 Dependency Injection 切换：

```text
Local Adapter
Remote Adapter
```

但必须明确：

> **Local / Remote 可以在调用代码层面无缝切换，不能在延迟、失败、一致性和幂等语义层面假装完全相同。**

建议优先选择 `Long-term Memory Manager` 和 `State Manager` 做 Remote Service 实验：

- 长期记忆可以迁移搜索、推荐、召回和排序经验；工作记忆优先使用直接读取路径。
- State Manager 可以迁移流处理、Checkpoint、恢复和一致性经验。
- Context Manager、Execution Engine 和 Orchestrator 优先保留在 Runtime 内。

这样可以形成两个互补的学习维度：

```text
代码层面：理解 Agent Runtime
设计层面：理解 Runtime Module 如何演进成 Distributed Service
```

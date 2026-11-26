# Infrastructure

| 分组 | 职责 |
|---|---|
| [LanguageModels](./LanguageModels/LanguageModels.md) | Ollama HTTP 请求、响应、取消、超时和协议边界。 |
| [Mcp](./Mcp/McpToolClientTests.cs) | stdio 配置、惰性连接、工具访问、取消与进程清理。 |
| [Persistence](./Persistence/InMemoryWorkingMemoryStoreTests.cs) | 工作记忆内存 Store 的集合、快照与原子操作规则。 |

测试使用手写 HTTP Handler、独立内存状态和通过官方 SDK 创建的
[本地 MCP 测试 Server](../../HanyingAgent.McpTestServer/Program.cs)，不连接真实外部服务或数据库。
跨模块持久化一致性用例仍在 Management/Tasks 和 Agent 的相应组合测试中。

```powershell
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --no-restore --configuration Release --filter "FullyQualifiedName~HanyingAgent.Tests.Infrastructure."
```

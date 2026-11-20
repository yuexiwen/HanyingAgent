# Infrastructure

| 分组 | 职责 |
|---|---|
| [LanguageModels](./LanguageModels/LanguageModels.md) | Ollama HTTP 请求、响应、取消、超时和协议边界。 |
| [Persistence](./Persistence/InMemoryWorkingMemoryStoreTests.cs) | 工作记忆内存 Store 的集合、快照与原子操作规则。 |

测试使用手写 HTTP Handler 和独立内存状态，不连接真实模型服务或数据库。
跨模块持久化一致性用例仍在 Management/Tasks 和 Agent 的相应组合测试中。

```powershell
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --no-restore --configuration Release --filter "FullyQualifiedName~HanyingAgent.Tests.Infrastructure."
```

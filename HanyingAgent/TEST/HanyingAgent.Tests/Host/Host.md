# Host

| 测试 | 职责 |
|---|---|
| [HostExecutionTests](./HostExecutionTests.cs) | 启动配置、DI、真实 HTTP 协议映射、日志、取消与 Console 端到端链路。 |
| [ConsolePresentationTests](./Console/ConsolePresentationTests.cs) | 终端帮助及纯文本、富终端展示。 |
| [ConsoleLogControlTests](./Console/ConsoleLogControlTests.cs) | 控制台日志开关和交互边界。 |

原 Execution 测试分组中的入口测试归入本目录，执行策略测试留在 Agent/Execution。
所有用例保持原有行为，不需要真实 Ollama 服务。

```powershell
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --no-restore --configuration Release --filter "FullyQualifiedName~HanyingAgent.Tests.Host."
```

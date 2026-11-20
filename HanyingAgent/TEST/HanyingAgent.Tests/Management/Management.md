# Management

[TaskTests](./Tasks/TaskTests.cs) 验证任务生命周期、会话占用、状态转换、取消与收尾，
包含跨 Store 原子性及不同 Host 的数据隔离。
现有用户、会话组合用例仍随 Agent/Orchestration 的 DialogueTests 保留，不为目录分组复制测试。

在应用项目根目录运行：

```powershell
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --no-restore --configuration Release --filter "FullyQualifiedName~HanyingAgent.Tests.Management."
```

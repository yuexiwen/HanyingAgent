# HanyingAgent.Tests

## 功能与定位

正式的 .NET 10 xUnit 测试项目，通过 ProjectReference 引用应用，不复制生产源码。
使用 SDK 模板提供的 xUnit v2、Microsoft.NET.Test.Sdk 和 VSTest 适配器，不额外引入 Mock 库或覆盖率工具。

当前覆盖 WorkingMemory 的契约、测试时钟、真实内存 Store 与隔离 Store 依赖的 Manager 单元测试；
第四步已补齐真实 Manager + Store 的跨层边界与并发测试，第五步统一验收与范围核对已完成。

WorkingMemory 阶段的历史统一运行结果：252 项通过、0 失败、0 跳过；解决方案构建为 0 警告、0 错误。
其中 ID / Owner 基础测试共 41 项、测试时钟 6 项、Store 46 项、Manager 109 项、跨层组合 50 项。
完整要求映射与限制见 [WorkingMemory 验收记录](<../../../.docs/disign/WorkingMemory 最小实现与演进计划.md#61-第五步统一验收结果>)。

Context 五步最小版共 77 项测试通过，其中 57 项使用手写工作记忆替身进行独立验证，
20 项直接组合真实 Builder、Manager 与 Store，使用可控时间，不调用真实 LLM。

2026-11-09 Context 阶段的历史全项目统一验收：329 项通过、0 失败、0 跳过；解决方案构建为 0 警告、0 错误。

| 测试分组 | 通过用例数 |
|---|---:|
| MemoryId / MemoryOwner | 41 |
| ManualTimeProvider | 6 |
| InMemoryWorkingMemoryStore | 46 |
| LocalWorkingMemoryManager | 109 |
| WorkingMemoryBoundary | 50 |
| LocalContextBuilder | 57 |
| ContextBoundary | 20 |
| 合计 | 329 |

2026-11-10 新增 Ollama 适配器 89 项离线测试，使用手写 HttpMessageHandler，不依赖模型服务。
与 77 项 Context 用例合并回归共 166 项通过、0 失败、0 跳过，解决方案构建 0 警告、0 错误；
其余 252 项历史用例本轮未重跑，不能把 329 + 89 表述为本轮全套运行结果。
另以实际适配器及 Context 调用本机模型验证合成文本，手工联调不计入离线测试数量。

当前入口已接入 Generic Host、DI 和后台多轮 Console；统一预算使用明确标记的保守 Token 估算，
精确 tokenizer 计数仍未实现。
新增 [HostExecutionTests](./Host/HostExecutionTests.cs) 覆盖启动配置校验、配置到真实 HTTP 协议的映射、
单轮隔离、并发日志 Scope、字符超限、失败与非完整回复展示、超时恢复及取消输入和模型等待。
测试使用真实 DI / Context / 模型适配器与手写 HTTP Handler，不依赖真实 Ollama。
另有中文 Console 端到端用例：启动真实应用子进程，以 UTF-8 写入中文，通过临时回环 HTTP 接收器
检查实际请求文本逐字一致，并检查中文和非 BMP 字符回复没有损坏；不连接外部服务。
新增 [DirectExecutorTests](./Agent/Execution/DirectExecutorTests.cs) 的 25 项策略测试：正常交付、截断、
未知、拒绝、过滤、工具请求优先级、非法响应、元数据保留、依赖错误、取消及 Console 结论展示。
2026-11-12 本阶段针对性验收：Execution 分组共 47 项通过、0 失败、0 跳过，
构建使用 `-warnaserror` 通过；本阶段未重跑其余分组或真实 Ollama 联调。
日志事件编号测试复用上述用例：核对并发执行中的事件编号、名称、顺序及 ExecutionId 隔离，
并检查非正常交付仍使用 1002，以及失败、取消、超时分别使用 1003、1004、1005。

2026-11-12 会话阶段新增 [DialogueTests](./Agent/Orchestration/DialogueTests.cs) 的 20 项组合用例，
覆盖同会话历史、跨会话隔离、10 轮窗口、精确字符边界、分页、归属、幂等并发、快照、
失败 / 取消 / 保存失败、执行 lease、200 条扫描上限与 Console 命令。
与 Execution 47 项合并共 67 项通过、0 失败、0 跳过；Release 构建以 `-warnaserror` 验证。
Debug 中旧应用仍运行，因此本轮不覆盖其输出、不终止该用户进程。
真实 Ollama 中文两轮姓名问答与新建空会话联调通过，不计入自动化测试数；其余分组本轮未重跑。

```powershell
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --no-restore --configuration Release --filter "FullyQualifiedName~HanyingAgent.Tests.Agent.Orchestration|FullyQualifiedName~HanyingAgent.Tests.Agent.Execution|FullyQualifiedName~HanyingAgent.Tests.Host" --verbosity minimal -warnaserror
```
2026-11-11 全量回归：439 项通过、0 失败、0 跳过（原 418 项加 Host / Execution 21 项），
使用 `dotnet test .\HanyingAgent.slnx --no-restore --verbosity minimal -warnaserror` 同时验收构建。
另外通过真实 Console 验证本机模型回答与跨模块相同 ExecutionId，不计入自动化测试数。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Management](./Management/Management.md) | 验证任务生命周期、会话占用、取消、结果保存及收尾。 |
| [Agent](./Agent/Agent.md) | 验证编排、执行策略、Context 和记忆能力。 |
| [Host](./Host/Host.md) | 验证入口配置、Console 交互和端到端调用链。 |
| [Infrastructure](./Infrastructure/Infrastructure.md) | 验证模型 Provider 适配与内存 Store。 |
| [Support](./Support/Support.md) | 提供可控 UTC 时钟并测试其行为。 |

2026-11-20 按 Management / Agent 分层调整实现、Contracts 和测试目录，命名空间同步迁移。
Release 全套 571 项通过、0 失败、0 跳过，编译启用 `-warnaserror`。
这次只迁移组织结构，不增加或删除测试；上文各阶段数字仍为历史验收结果。
筛选命令需使用新命名空间，分组文档给出当前运行示例。

## 运行

在 HanyingAgent 项目根目录执行：

```powershell
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj
```

当前使用 VSTest，未启用 Microsoft.Testing.Platform；上面是 VSTest 的项目路径语法。
首次运行会还原测试项目依赖；后续依赖未变时可以加 `--no-restore`。
也可以在 VS Code 中打开 [HanyingAgent.slnx](../../HanyingAgent.slnx)，使用 C# Dev Kit 的测试资源管理器。

复现第五步统一验收（依赖已还原）：

```powershell
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --no-restore --nologo --verbosity minimal
dotnet build .\HanyingAgent.slnx --no-restore --nologo -warnaserror
dotnet run --project .\HanyingAgent.csproj --no-build
```

最后一条现会启动单轮 Console，需要本地 Ollama 才能回答；输入 `/exit` 或 EOF 可退出。
上面的测试与构建命令不依赖真实模型服务。

仅运行内存 Store 测试：

```powershell
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --no-restore --filter "FullyQualifiedName~InMemoryWorkingMemoryStoreTests"
```

仅运行 Manager 单元测试：

```powershell
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --no-restore --filter "FullyQualifiedName~LocalWorkingMemoryManagerTests"
```

仅运行第四步跨层边界与并发测试：

```powershell
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --no-restore --filter "FullyQualifiedName~WorkingMemoryBoundaryTests"
```

仅运行 Context 独立测试：

```powershell
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --no-restore --filter "FullyQualifiedName~HanyingAgent.Tests.Agent.Context.LocalContextBuilderTests"
```

运行全部 Context 测试（独立与真实组合）：

```powershell
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --no-restore --filter "FullyQualifiedName~HanyingAgent.Tests.Agent.Context."
```

仅运行 Context 第五步真实组合测试：

```powershell
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --no-restore --filter "FullyQualifiedName~HanyingAgent.Tests.Agent.Context.ContextBoundaryTests"
```

仅运行 Ollama 适配器离线测试：

```powershell
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --no-restore --filter "FullyQualifiedName~HanyingAgent.Tests.Infrastructure.LanguageModels."
```

仅运行 Host 与执行链路测试：

```powershell
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --no-restore --filter "FullyQualifiedName~HostExecutionTests"
```

## 设计边界

- 应用排除 TEST 子树，测试依赖不进入应用项目。
- 测试不依赖真实模型、外部网络服务或数据库；中文 Console 用例临时监听本机回环地址，
  Host 的 HTTP 超时用例等待配置的一秒超时，其余时间边界主要由可控时钟验证。
- 每个测试创建独立状态和时钟，不依靠测试执行顺序。
- 组合边界也在本项目中验证，不另起一套临时验证项目；Agent 接入不属于这些测试的前提。

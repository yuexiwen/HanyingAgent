# HanyingAgent.Tests

## 功能与定位

正式的 .NET 10 xUnit 测试项目，通过 ProjectReference 引用应用，不复制生产源码。
使用 SDK 模板提供的 xUnit v2、Microsoft.NET.Test.Sdk 和 VSTest 适配器，不额外引入 Mock 库或覆盖率工具。

当前覆盖 WorkingMemory 的契约、测试时钟、真实内存 Store 与隔离 Store 依赖的 Manager 单元测试；
第四步已补齐真实 Manager + Store 的跨层边界与并发测试，第五步统一验收与范围核对已完成。

WorkingMemory 阶段的历史统一运行结果：252 项通过、0 失败、0 跳过；解决方案构建为 0 警告、0 错误。
其中 ID / Owner 基础测试共 41 项、测试时钟 6 项、Store 46 项、Manager 109 项、跨层组合 50 项。
完整要求映射与限制见 [WorkingMemory 验收记录](<../../../.docs/WorkingMemory 最小实现与演进计划.md#61-第五步统一验收结果>)。

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

入口仍仅输出 HanyingAgent；应用 71 个 Compile 项不包含 TEST 源码。精确 Token 预算与 Agent 接入仍未实现。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [WorkingMemory](./WorkingMemory/WorkingMemory.md) | 验证记忆契约、Store、Manager 和真实组合的边界与并发行为。 |
| [Context](./Context/Context.md) | 验证构建请求、字符上限、工作记忆参考数据及真实 Builder + Manager + Store 组合。 |
| [LanguageModels](./LanguageModels/LanguageModels.md) | 验证 Ollama 原生 HTTP 请求、响应、错误、取消和超时协议。 |
| [Support](./Support/Support.md) | 提供可控 UTC 时钟并测试其行为。 |

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

最后一条只验证入口仍输出 HanyingAgent；它不会启动完整 Agent 或示例记忆流程。

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
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --no-restore --filter "FullyQualifiedName~HanyingAgent.Tests.Context.LocalContextBuilderTests"
```

运行全部 Context 测试（独立与真实组合）：

```powershell
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --no-restore --filter "FullyQualifiedName~HanyingAgent.Tests.Context."
```

仅运行 Context 第五步真实组合测试：

```powershell
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --no-restore --filter "FullyQualifiedName~HanyingAgent.Tests.Context.ContextBoundaryTests"
```

仅运行 Ollama 适配器离线测试：

```powershell
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --no-restore --filter "FullyQualifiedName~HanyingAgent.Tests.LanguageModels."
```

## 设计边界

- 应用排除 TEST 子树，测试依赖不进入应用项目。
- 测试不依赖真实模型、网络服务、数据库或真实时间等待。
- 每个测试创建独立状态和时钟，不依靠测试执行顺序。
- 组合边界也在本项目中验证，不另起一套临时验证项目；Agent 接入不属于这些测试的前提。

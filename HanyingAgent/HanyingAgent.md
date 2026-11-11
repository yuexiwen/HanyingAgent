# HanyingAgent

## 功能与定位

面向学习的 Agent Runtime，采用单个 .NET 10 Console Project、单进程的模块化单体结构，通过明确契约为后续远程适配保留边界。

当前已定义 Session、Task、User、Conversation、Memory、Tools、LanguageModels 与 Context 契约；
已实现工作记忆管理器、Context Builder、Ollama 模型适配器，以及 Generic Host / DI 驱动的单轮 Console 执行链路。
WorkingMemory 已建立内部存储端口、正式 xUnit 测试项目、测试用可控时钟，并实现进程内存 Store 和工作记忆 Manager。
当前已分别验证 Store 与 Manager 的独立行为，并增加真实组合的跨层边界与并发测试。
五步现已完成，内存版 WorkingMemory MVP 此前已通过统一验收：252 个正式测试全部通过，构建 0 警告、0 错误。
模块可供后续调用方通过公共接口使用，但未在程序入口接入记忆链路。
数据不持久化，自动提取与评分、身份授权、容量治理和长期记忆仍未实现。

Context 五步最小版已完成：接口、纯文本请求组装、明确归属的工作记忆接入、总输入字符上限和真实组件组合验收，
共 57 项独立测试与 20 项真实组合测试通过。字符上限默认 32,000，可配置；精确 Token 预算仍未实现。
Context 阶段的历史统一验收：全项目 329 项测试通过、0 失败、0 跳过，解决方案构建 0 警告、0 错误。

Ollama 适配器实现 ILanguageModelRuntime，使用 HttpClient 调用原生 /api/chat，支持纯文本非流式请求，
映射结束原因与 Token 用量，工具及非法协议明确拒绝，不自动重试、下载或启动模型服务。
2026-11-10 针对性验收：89 项模型适配测试与 77 项 Context 回归共 166 项通过，
构建 0 警告、0 错误，并通过实际适配器调用本机 qwen2.5:1.5b 验证合成问题；其余 252 项历史测试本轮未重跑。
模型调用能力现已通过 DirectExecutor 接入 Console 用户输入；尚无 Agent 循环、会话历史或工具执行。
使用方式与限制见 [模型适配器](./SRC/Infrastructure/LanguageModels/LanguageModels.md)。

## 本地运行

先准备本地 Ollama 服务及模型，再从仓库根目录运行：

```powershell
ollama pull qwen2.5:1.5b
dotnet run --project .\HanyingAgent\HanyingAgent.csproj
```

连续输入独立问题，`/exit` 或输入结束退出；Ctrl+C 取消当前等待并退出应用，不继续原执行。
入口统一使用 UTF-8 输入与输出，支持中文；通过管道输入时，发送端也需要使用 UTF-8。
Windows PowerShell 5.1 的管道默认编码可能丢失中文，可先执行 `$OutputEncoding = [System.Text.UTF8Encoding]::new($false)`。
每轮只包含配置的系统指令与本轮用户输入，不保留历史，也不自动选择任何 MemoryOwner。
模型返回 Length、过滤、拒绝、工具调用或未知结束原因时明确显示状态，不伪装成完整回答。
连接、协议或输入错误会显示失败并允许继续输入；超时与用户主动取消分别处理。

[appsettings.json](./appsettings.json) 随构建与发布复制到输出目录，Host 从应用输出目录读取配置，
不依赖启动终端的当前目录。支持 Generic Host 标准环境变量与命令行覆盖，例如：

```powershell
$env:Ollama__ModelName = "qwen2.5:1.5b"
dotnet run --project .\HanyingAgent\HanyingAgent.csproj -- --Ollama:TimeoutSeconds=180 --Agent:MaxOutputTokens=128
```

Ollama 配置包括 BaseAddress、ModelName、ContextWindowTokens、TimeoutSeconds；
Agent 配置包括 SystemInstruction、MaxInputCharacters、MaxOutputTokens、Temperature。
非法配置在启动时失败。地址必须为末尾带 `/` 的 HTTP(S) URL，不含凭据、查询或片段。
字符上限与模型 Token 窗口独立，不自动估算输入 Token、裁剪消息或预留输出空间。

日志写入标准错误，交互与回答写入标准输出；每轮的执行、Context 与模型日志共享 ExecutionId。
默认不记录输入正文、系统指令、记忆正文或完整回复。日志级别可在 Logging:LogLevel 中设置。
详细边界见 [Host](./SRC/Host/Host.md)、[Execution](./SRC/Runtime/Execution/Execution.md)
和[日志说明](./SRC/Infrastructure/Observability/Observability.md)。

2026-11-11 本轮验收：全套 439 项测试通过、0 失败、0 跳过，构建警告按错误处理并通过。
真实 Console 经 Context 与 Ollama 调用本机 qwen2.5:1.5b，`2 + 3` 返回 `5`、结束原因 Stop；
已检查实际执行、Context 与模型日志使用同一 ExecutionId。真实服务联调不计入离线测试数。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [SRC](./SRC/SRC.md) | 按契约、交互入口、运行控制、业务能力与基础设施组织应用模块。 |
| [TEST](./TEST/TEST.md) | 正式 xUnit 测试项目，验证 WorkingMemory、Context 和 Ollama 模型适配器。 |

## 根目录与设计边界

- [HanyingAgent.csproj](./HanyingAgent.csproj) 定义项目构建，并排除 TEST 子树，防止测试源码混入应用。
- [HanyingAgent.slnx](./HanyingAgent.slnx) 组织应用与测试项目，便于编辑器发现和统一构建。
- [Program.cs](./Program.cs) 负责 Host 启停；[ApplicationHost](./SRC/Host/ApplicationHost.cs) 集中配置 DI、Options 与日志，不承载具体业务规则。
- bin、obj 是生成目录，不编写模块文档，也不作为业务模块维护。
- 目录分组不等于微服务划分；业务模块通过契约协作，具体实现由入口组合。

整体设计背景见[设计初稿](<../.docs/Mini Agent Runtime 设计初稿.md>)。
模块边界与端到端执行过程见[模块职责与运行流程](<../.docs/HanyingAgent 模块职责与运行流程.md>)。
工作记忆的本期范围、验收标准和扩展方向见[WorkingMemory 最小实现与演进计划](<../.docs/WorkingMemory 最小实现与演进计划.md>)。

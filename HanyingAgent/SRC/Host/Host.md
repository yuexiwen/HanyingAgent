# Host

## 功能与定位

把用户交互适配成 Runtime 的调用，负责输入解析、会话视图和输出展示。

当前已实现 Generic Host / DI 组合与连续单轮 Console，没有正式会话管理或多轮历史。

## 当前实现

- [ApplicationHost](./ApplicationHost.cs) 统一注册配置、日志、记忆、Context、类型化 HTTP 模型客户端和执行器。
- [ConsoleApplication](./ConsoleApplication.cs) 读取输入并调用 DirectExecutor，依据 ExecutionOutcome.Conclusion 展示文本与提示，
  不再判断 ModelFinishReason 或工具请求是否合法。无有效正文时不打印空 Assistant 行；部分文本保留展示并附非正常交付提示。
- 程序入口在 Host / DI 捕获 Console.In / Console.Out 之前，将输入与输出统一为无 BOM 的 UTF-8，
  避免 Windows 默认代码页使中文变成问号或乱码。重定向输入也必须由调用方提供 UTF-8 字节。
- [Configuration](./Configuration) 定义 Ollama 与 Agent 选项，启动时校验；配置来源和命令见[应用文档](../../HanyingAgent.md)。
- 每次非空输入创建独立 DI Scope，解析瞬态执行器与模型客户端；内存 Store、Manager 和无状态 Context Builder 为 Singleton。
  Console 不捕获类型化客户端为单例，Provider HTTP Handler 由 IHttpClientFactory 管理。
- 不持有对话历史，每轮只传系统指令、本轮输入和空 MemoryOwners；不读取 User / Session / Task。
- `/exit`（忽略大小写）、EOF 或 Ctrl+C 退出，空白输入跳过。
- Generic Host 的 ConsoleLifetime 将 Ctrl+C 转为 ApplicationStopping，信号同时取消输入等待或模型调用。
  标准输入的同步 ReadLine 不能强制中断，退出时只停止等待该后台读取，不再发起下一次读取。
- 已知调用失败不自动重试，显示失败后接受下一问题；用户取消退出，超时允许继续输入。
- 业务错误由执行器在关联 Scope 内记录一次，Console 只显示提示；未预期异常不吞掉。

## 后续组件规划

| 组件 | 一句话概述 |
|---|---|
| AgentShell | 读取输入、维护当前会话视图并展示结果和增量事件。 |
| CommandParser | 用确定性代码识别 create、select、cancel 等显式命令。 |
| 输出渲染 | 将 Runtime 结果与事件转换成用户可理解的展示。 |

## 设计边界

显式命令不调用模型；Host 不拼接模型 Context、不执行记忆检索算法，也不推进 ReAct。
Session 生命周期的实际协调位于 Runtime/Orchestration，Host 只发起相关操作。

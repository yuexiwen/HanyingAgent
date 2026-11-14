# Host

## 功能与定位

把用户交互适配成 Runtime 的调用，负责输入解析、会话视图和输出展示。

当前已实现 Generic Host / DI、本地会话命令、有界多轮历史和后台 Direct 任务；所有数据仅保存于当前进程。

## 当前实现

- [ApplicationHost](./ApplicationHost.cs) 统一注册配置、日志、记忆、Context、类型化 HTTP 模型客户端和执行器。
- [ConsoleApplication](./ConsoleApplication.cs) 提交任务后立即显示 TaskId；`/task` 查询、`/wait` 等待并展示 TaskResult。
  不判断模型协议或推进任务状态。无有效正文时不打印空 Assistant 行；部分文本保留展示并附失败原因。
- 程序入口在 Host / DI 捕获 Console.In / Console.Out 之前，将输入与输出统一为无 BOM 的 UTF-8，
  避免 Windows 默认代码页使中文变成问号或乱码。重定向输入也必须由调用方提供 UTF-8 字节。
- [Configuration](./Configuration) 定义 Ollama 与 Agent 选项，启动时校验；配置来源和命令见[应用文档](../../HanyingAgent.md)。
- 每个后台任务创建独立 DI Scope，解析瞬态执行器与模型客户端；内存 Store、Manager 和无状态 Context Builder 为 Singleton。
  Console 不捕获类型化客户端为单例，Provider HTTP Handler 由 IHttpClientFactory 管理。
- 启动时创建可信本地用户和 Default 会话；Host 只保存当前 SessionId，历史由 Conversation 管理，
  历史选择由 ConversationWindow 完成，不自动选择 MemoryOwner；正常输入通过 LocalTaskManager 创建正式 Task。
- `/new NAME` 创建并选择会话，`/sessions` 列出可见会话，`/use ID` 切换（完整 ID），
  `/history [CURSOR]` 查看最近 20 条或更早一页，`/close` 关闭当前会话并取消选择。
  Closed 会话可以选择查看，但不能再提问；无当前会话时须新建或选择。
  命令忽略大小写，未知命令或非法参数明确报错，不发送给模型，不记为聊天消息。
- `/tasks [CURSOR]` 查看当前会话任务；`/task [ID]` 查询状态及已有结果；
  `/wait [ID]` 等待结果；`/cancel [ID]` 请求取消。后三个命令省略 ID 时使用最近提交的任务，
  切换会话不会改变这个最近任务 ID。取消返回快照不代表业务已经停止。
- 后台期间可以继续输入命令或切换会话；同会话再次提交和关闭明确拒绝。
  `/wait` 等待期间不读取新命令；取消等待本身不会请求业务取消，Ctrl+C 退出则由 Host 取消全部任务。
- `/exit`（忽略大小写）、EOF 或 Ctrl+C 退出，空白输入跳过。
- Generic Host 的 ConsoleLifetime 将 Ctrl+C 转为 ApplicationStopping，取消 Console 等待；
  Program 随后调用 Host.StopAsync，任务服务停止接受、取消运行任务并等待收尾，退出期限为 10 秒。
  `/exit` 和 EOF 同样进入 Host 收尾，不承诺退出后继续运行；超出期限显式失败，不提前确认任务取消。
  标准输入的同步 ReadLine 不能强制中断，退出时只停止等待该后台读取，不再发起下一次读取。
- 已知调用失败不自动重试，显示失败后接受下一问题；用户取消退出，超时允许继续输入。
- 执行异常由原执行边界记录，任务层记录失败摘要；后台未预期异常记录 Error 并保存 Failed。
  Console 操作错误记录 Warning，返回提示；不展示原始服务端响应或异常堆栈。

## 后续组件规划

| 组件 | 一句话概述 |
|---|---|
| AgentShell | 读取输入、维护当前会话视图并展示结果和增量事件。 |
| CommandParser | 用确定性代码识别 create、select、cancel 等显式命令。 |
| 输出渲染 | 将 Runtime 结果与事件转换成用户可理解的展示。 |

## 设计边界

显式命令不调用模型；Host 不拼接模型 Context、不执行记忆检索算法，也不推进 ReAct。
Task 接受、会话占用、后台执行与收尾位于 Orchestration；Host 只发起操作和管理进程生命周期。

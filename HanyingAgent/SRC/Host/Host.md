# Host

## 功能与定位

把用户交互适配成 Runtime 的调用，负责输入解析、会话视图和输出展示。

当前已实现 Generic Host / DI、本地会话命令、有界多轮历史和后台 Direct 任务；所有数据仅保存于当前进程。

## 当前实现

- [ApplicationHost](./ApplicationHost.cs) 统一注册配置、日志、记忆、Context、类型化 HTTP 模型客户端和执行器。
- [ConsoleApplication](./ConsoleApplication.cs) 提交任务后立即显示 TaskId，并异步等待该任务的结果。
  当前会话完成时自动展示回答、失败或取消；其他会话完成时显示带 SessionId / TaskId 的通知，
  不混入当前会话回答，切换回该会话后展示尚未阅读的更新。
  `/task` 保留显式查询，`/wait` 保留等待；已经展示的结果再次 `/wait` 只显示状态，重新查看正文用 `/task`。
  不判断模型协议或推进任务状态。无有效正文时不打印空 Assistant 行；部分文本保留展示并附失败原因。
- 程序入口在 Host / DI 捕获 Console.In / Console.Out 之前，将输入与输出统一为无 BOM 的 UTF-8，
  避免 Windows 默认代码页使中文变成问号或乱码。重定向输入也必须由调用方提供 UTF-8 字节。
- [Configuration](./Configuration) 定义 Ollama 与 Agent 选项，启动时校验；配置来源和命令见[应用文档](../../HanyingAgent.md)。
- 每个后台任务创建独立 DI Scope，解析瞬态执行器与模型客户端；内存 Store、Manager 和无状态 Context Builder 为 Singleton。
  Console 不捕获类型化客户端为单例，Provider HTTP Handler 由 IHttpClientFactory 管理。
- 启动时创建可信本地用户和 Default 会话；Host 只保存当前 SessionId，历史由 Conversation 管理，
  历史选择由 ConversationWindow 完成，不自动选择 MemoryOwner；正常输入通过 LocalTaskManager 创建正式 Task。
- `/new NAME` 创建并选择会话，`/session [ID]` 查询指定或当前会话详情，不改变选择；
  `/sessions` 列出可见会话并用 `*` 标记当前选择，`/use ID` 切换（完整 ID）并显示详情。
  详情包含是否选中、创建及活动时间；详情和列表均显示当前任务（若有），否则显示最近提交的任务。
  从未提交任务的会话明确显示 Task: none，不把它当作已完成任务。
  Finished: yes 表示任务处于 Completed、Failed 或 Cancelled；其余状态（包括 Cancelling）为 no。
  Session.Idle 只代表空闲，不代表最近任务成功；任务成功与否应看具体终态。
  这些读取是查询时快照，后台任务可以继续变化，会话和任务查询不是同一冻结事务。
- `/history [CURSOR]` 查看最近 20 条或更早一页，`/close` 关闭当前会话并取消选择。
  Closed 会话可以选择查看，但不能再提问；无当前会话时须新建或选择。
  命令忽略大小写，未知命令或非法参数明确报错，不发送给模型，不记为聊天消息。
- `/tasks [CURSOR]` 查看当前会话任务；`/task [ID]` 查询状态及已有结果；
  `/wait [ID]` 等待结果；`/cancel [ID]` 请求取消。后三个命令省略 ID 时使用最近提交的任务，
  切换会话不会改变这个最近任务 ID。取消返回快照不代表业务已经停止。
- 后台期间可以继续输入命令或切换会话；同会话再次提交和关闭明确拒绝。
  `/wait` 等待期间不读取新命令；取消等待本身不会请求业务取消，Ctrl+C 退出则由 Host 取消全部任务。
- Console 用同一输出循环协调命令和结果，不由后台任务直接写 stdout。
  始终只保留一个同步 ReadLine；等待输入时也能被结果完成唤醒，无轮询。
  自动通知另起一行并重显 `You>`，不丢弃正在等待的输入。
  标准 ReadLine 不提供完整输入行重绘，已输入文字可能视觉上留在旧行；stderr 日志仍可能与终端交错。
  本实现不是完整终端编辑器，也不是面向多个客户端的任务事件订阅服务。
- `/exit`（忽略大小写）、EOF 或 Ctrl+C 退出，空白输入跳过。
- Generic Host 的 ConsoleLifetime 将 Ctrl+C 转为 ApplicationStopping，取消 Console 等待；
  Program 随后调用 Host.StopAsync，任务服务停止接受、取消运行任务并等待收尾，退出期限为 10 秒。
  `/exit` 和 EOF 同样进入 Host 收尾，不承诺退出后继续运行；超出期限显式失败，不提前确认任务取消。
  标准输入的同步 ReadLine 不能强制中断，退出时只停止等待该后台读取，不再发起下一次读取。
  Console 退出会取消并观察结果通知等待，不再展示回复；业务任务仍由 Host 负责取消及收尾。
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

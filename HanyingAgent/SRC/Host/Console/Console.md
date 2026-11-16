# Console

Console 专属交互组件，不承担 Session / Task 的权威存储、业务状态转换或模型执行。

| 组件 | 职责 |
|---|---|
| [ConsoleCommandHandler](./ConsoleCommandHandler.cs) | 解析命令及参数，解析会话编号，调用已有管理器，普通输入提交任务。 |
| [ConsoleRenderer](./ConsoleRenderer.cs) | 展示提示、会话和任务快照、历史、回答与错误，不访问业务服务。 |
| [ConsolePresentation](./ConsolePresentation.cs) | 使用 Spectre.Console 绘制彩色状态、帮助及会话表格、回答与历史面板，集中处理终端展示。 |
| [ConsoleLogControl](./ConsoleLogControl.cs) | 保存线程安全的进程级日志显示开关，通过标准日志筛选控制 Console Provider，不替代 ILogger。 |
| [ConsoleState](./ConsoleState.cs) | 维护当前会话、最近任务、待展示结果、未读结果与去重标记。 |

[ConsoleApplication](../ConsoleApplication.cs) 每次运行创建一组组件，负责输入与任务结果的单循环协调，
初始化和退出收尾。状态只属于该次 Console 运行，查询或选择会话不创建另一套任务状态机。
命令处理器通过管理器读取数据后交给 Renderer；所有渲染由主循环或其等待的命令处理调用完成，
后台任务不直接写终端。

历史读取与轮数选择由 Runtime 的
[ConversationHistoryReader](../../Runtime/Conversation/ConversationHistoryReader.cs) 完成。
`/history` 的数量解析属于命令适配，显示格式与未记录回复的提示属于渲染。

## 终端美化

使用 Spectre.Console 0.57.2。ConsoleRenderer 保留输出入口与纯文本分支，
ConsolePresentation 只处理富终端绘制，不依赖任务或会话服务。

- 仅标准终端且输入、输出均未重定向时自动启用；自定义 TextWriter、管道、文件重定向或 TERM=dumb 保持原纯文本格式。
- 启动帮助按命令分行展示；会话列表使用表格，宽度不足 80 列时改用分行详情，避免密集表格截断 ID。
- 输入 `/help` 可随时再次显示所有命令，复用启动帮助；不要求选中会话，不提交模型任务，不接受额外参数。
- `/logs` 查看日志开关，`/logs off` 关闭全部标准 ILogger 控制台日志（包括 Warning / Error / Critical），
  `/logs on` 恢复按原日志级别及分类配置输出。默认开启，命令忽略大小写，无须当前会话，不调用模型。
  开关作用于本进程所有会话和后台任务，不修改 appsettings.json，重启恢复开启，不影响其他日志 Provider。
  任务结果、失败提示、帮助和其他 stdout 交互保持可见；关闭前已进入控制台队列的日志仍可能随后输出。
- 任务状态使用颜色区分；回答、错误及历史消息使用面板，保留原始文字，不将模型输出当作 Spectre Markup 执行。
- 不使用清屏、光标重定位、动态进度或额外输入循环。原有异步结果通知及提示符协调保持不变。
- 日志仍由 ILogger 写入 stderr，不在此类中改动日志级别或路由；终端同时显示日志时仍可能打断提示行。
- 不改变命令、会话连续编号、隐藏语义、自动通知、重复结果抑制和取消行为。

验证：Release 配置下 Execution 相关 74 项测试通过（含 8 项展示测试、4 项帮助命令测试和 8 项日志开关测试），警告按错误处理。
测试覆盖重定向纯文本、面板字面量、显示开关、完整帮助，以及 32 / 120 列会话输出；
既有 Host / Console 交互回归保持通过，不需要运行 Ollama。

在仓库根目录执行：

```powershell
dotnet test .\HanyingAgent\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~Execution" -warnaserror
dotnet run --project .\HanyingAgent\HanyingAgent.csproj --configuration Release --no-build
```

使用真实交互终端查看颜色与边框；重定向运行会按设计显示纯文本。

不引入每命令一个类、通用命令框架或完整终端编辑器。

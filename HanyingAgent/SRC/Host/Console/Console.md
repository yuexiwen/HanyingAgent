# Console

Console 专属交互组件，不承担 Session / Task 的权威存储、业务状态转换或模型执行。

| 组件 | 职责 |
|---|---|
| [ConsoleCommandHandler](./ConsoleCommandHandler.cs) | 解析命令及参数，解析会话编号，调用已有管理器，普通输入提交任务。 |
| [ConsoleRenderer](./ConsoleRenderer.cs) | 展示提示、会话和任务快照、历史、回答与错误，不访问业务服务。 |
| [ConsoleState](./ConsoleState.cs) | 维护当前会话、最近任务、待展示结果、未读结果与去重标记。 |

[ConsoleApplication](../ConsoleApplication.cs) 每次运行创建一组组件，负责输入与任务结果的单循环协调，
初始化和退出收尾。状态只属于该次 Console 运行，查询或选择会话不创建另一套任务状态机。
命令处理器通过管理器读取数据后交给 Renderer；所有渲染由主循环或其等待的命令处理调用完成，
后台任务不直接写终端。

历史读取与轮数选择由 Runtime 的
[ConversationHistoryReader](../../Runtime/Conversation/ConversationHistoryReader.cs) 完成。
`/history` 的数量解析属于命令适配，显示格式与未记录回复的提示属于渲染。

本次为职责重构，保留命令、会话连续编号、隐藏语义、自动通知和取消行为。
不引入每命令一个类、通用命令框架或完整终端编辑器。

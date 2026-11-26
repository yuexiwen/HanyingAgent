# Agent

按 Agent 工作职责组织已有测试，不改变测试用例和断言。

| 分组 | 内容 |
|---|---|
| [Orchestration](./Orchestration/DialogueTests.cs) | 对话编排、历史、用户记忆、预算、取消及跨模块集成。 |
| [Execution](./Execution/DirectExecutorTests.cs) | DirectExecutor 的模型调用、响应解释和异常语义。 |
| [Context](./Context/Context.md) | 上下文校验、组装、统一预算与份额策略。 |
| [Capabilities/Memory](./Capabilities/Memory/Memory.md) | 记忆契约、Manager 规则与真实 Manager + Store 组合。 |
| [Capabilities/Tools](./Capabilities/Tools/LocalToolManagerTests.cs) | 时间工具、扩展注册、归属、调用去重、取消、未知结果及输出限制。 |
| [Capabilities/Skills](./Capabilities/Skills/LocalSkillManagerTests.cs) | Skill 发现与加载、格式校验、快照、大小与数量边界、链接拒绝和 Host 初始化。 |
| [Capabilities/Prompts](./Capabilities/Prompts/LocalPromptManagerTests.cs) | 模板发现、参数校验与字面值替换、快照、文件及输出边界和 Host 初始化。 |

内存 Store 单元测试移至 Infrastructure/Persistence，Console 与 Host 入口测试移至 Host。
已有 DialogueTests 包含归属、历史和交互集成测试，本次不为机械镜像目录而拆分测试方法。

```powershell
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --no-restore --configuration Release --filter "FullyQualifiedName~HanyingAgent.Tests.Agent."
```

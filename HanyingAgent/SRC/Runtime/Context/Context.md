# Context

## 功能与定位

决定当前一次模型调用需要看到哪些信息，组装类型化 ModelInvocation。
完整目标包括预算与工具协议；当前五步最小版已验收，支持纯文本消息、工作记忆与总输入字符上限，不保证 Token 预算。

当前没有子目录，已有 [LocalContextBuilder](./LocalContextBuilder.cs)，实现
[IContextBuilder](../../Contracts/Context/Operations/IContextBuilder.cs)。

## 当前最小实现

构造注入 IWorkingMemoryManager，调用方通过
[BuildContextRequest](../../Contracts/Context/Operations/Requests/BuildContextRequest.cs) 提供：
独立系统指令、按时间排列且包含当前 User 输入的消息序列、明确的 MemoryOwners，以及可选 ModelOptions。

```text
校验与快照化输入
  → 原始系统指令与消息字符数预检
  → 按值去重 Owner
  → 每个 Owner 读取最近 20 条工作记忆
  → 按 MemoryId 去重
  → UpdatedAt 降序、ID Ordinal 升序
  → System + 可选参考记忆 User 消息 + 原始消息
  → 最终全部消息正文字符数校验
  → ModelInvocation（Tools 为空）
```

- 原始序列只支持非空白 User 与纯文本 Assistant，并以当前 User 结尾；不再追加 Task.Input。
- 明确拒绝原始序列内的 System、Tool、Assistant 工具调用或未知消息类型，不静默丢弃。
- 不传记忆归属时不查记忆；不自动包含 User / Global，也不扩大 Session 到其下 Task。
- 输入 Owner 与消息集合在第一次异步读取前物化；Assistant 的空工具列表独立快照化。
- 依赖工作记忆管理器提供有效状态、到期过滤及精确归属结果，不重复实现其业务规则。
- 检查依赖返回的基本归属与 ID 一致性；重复 ID 内容冲突时报错，不默默选取某份。
- 参考记忆单独作为 User 参考数据消息，正文为带标题的 JSON，保留 ID、Owner、Type、Content、Source。
- 有记忆时向系统指令追加固定的参考资料使用规则，记忆正文不进入系统指令；无记忆时不加空资料块。
- JSON 编码明确数据结构，但不保证模型绝不会服从参考文本中的恶意指令；身份授权与提示注入防护不在本阶段完成。
- Options 原样传递，模型专属参数解释与 Provider JSON 适配仍属于模型模块。
- 依赖异常或取消显式传播，无自动重试、记忆读取失败时的空结果回退或部分上下文成功返回。
- 不读取 Conversation、不写记忆、不修改任务、不执行工具、不调用 LLM。

本地 IWorkingMemoryManager 没有独立只读接口，因此构造时注入完整接口，但构建器只调用 ListRecentAsync；
不为这一阶段另造与其重复的接口。独立测试与真实组合验证见 [Context 测试](../../../TEST/HanyingAgent.Tests/Context/Context.md)。

真实依赖的最小组合如下；request 由调用方提供，包含当前用户消息和明确归属：

```csharp
var store = new InMemoryWorkingMemoryStore();
IWorkingMemoryManager memory = new LocalWorkingMemoryManager(store, TimeProvider.System);
IContextBuilder builder = new LocalContextBuilder(memory);
var invocation = await builder.BuildAsync(request, cancellationToken);
```

需要记忆时由调用方先通过 memory.WriteAsync 写入；Builder 自身只读。
这只是组件组合方式，尚未写入程序入口，也不会调用模型；返回值是 ModelInvocation，不是模型回复。

## 输入字符上限

构造参数 maxInputCharacters 默认 32,000，必须大于 0；入口可以按需要调整，无须修改公共请求协议：

```csharp
var defaultBuilder = new LocalContextBuilder(memory);
var customBuilder = new LocalContextBuilder(memory, maxInputCharacters: 16_000);
```

- 按最终所有消息正文的 string.Length 累计，即 UTF-16 代码单元；不按 Unicode 字符、Token 或 UTF-8 字节计量。
- 包括原始系统指令、追加的换行和参考资料规则、参考记忆标题、完整序列化 JSON（含字段、元数据与转义），以及所有历史与当前 User / Assistant 正文。
- 无记忆时不增加参考消息或规则，因此没有这部分开销；记忆按既有规则去重后再计量，所有归属共用一次总上限。
- 不计 role 等传输结构、空 Tools 或 Options；MaxOutputTokens 仍只是输出选项，不参与输入预算。
- 刚好等于上限允许；超过时抛 InvalidOperationException，说明实际数值和配置上限，不包含请求正文。
- 不自动裁剪、丢弃记忆或截断当前请求，不返回部分成功。非正上限在构造时抛 ArgumentOutOfRangeException。
- 原始系统指令与消息已超限时，在读取记忆前失败；记忆读取后再检查完整输出，累计使用 long 避免 int 溢出。

此限制约束可返回的消息正文总长度，不保证模型 Token 窗口、HTTP 请求大小或构建过程的内存峰值。
Owner 数量、序列化前的记忆规模和输入集合物化仍无独立硬限制，不应把它当成处理任意不可控输入的完整资源治理。

## 五步计划与当前状态

| 步骤 | 内容 | 状态 |
|---|---|---|
| 第一步 | 确认组装规则，定义公共接口与请求。 | 已完成。 |
| 第二步 | 实现纯文本组装、合法性校验与消息快照。 | 已完成。 |
| 第三步 | 接入明确归属的工作记忆、去重与参考资料格式。 | 已完成，使用手写记忆替身独立测试。 |
| 第四步 | 总输入字符上限、精确阈值及边界验证；不把字符数当成 Token 数。 | 已完成，默认 32,000，可在构造时调整。 |
| 第五步 | 真实 Context + WorkingMemory Manager + Store 组合验收与统一文档收尾。 | 已完成，20 项真实组合测试及全套统一验收通过。 |

当前独立测试 57 项通过，覆盖原有 40 项与新增 17 项字符边界用例；20 条仍只是每 Owner 的记忆数量限制，
字符上限约束最终正文总长度，并非精确 Token 预算或完整传输请求大小限制。
另有 20 项真实组合测试通过，验证写入、更新、遗忘、状态及到期过滤、精确归属、每归属召回上限、跨归属排序和真实 JSON 字符边界。
2026-11-09 统一验收：全项目 329 项测试通过、0 失败、0 跳过，其中 Context 共 77 项；
解决方案构建 0 警告、0 错误，入口仍仅输出 HanyingAgent，应用 70 个编译项均不包含 TEST 源码。
测试分组与复现命令见 [测试项目](../../../TEST/HanyingAgent.Tests/HanyingAgent.Tests.md)。
未实现摘要、压缩、裁剪、相关性评分、长期记忆、Skills、工具历史、自动对话加载或 Agent 接入。

## 后续组件规划（非本期已实现）

| 组件 | 一句话概述 |
|---|---|
| ContextManager | 协调收集状态、对话、记忆、Skill 与工具 Observation。 |
| ContextRanker | 根据当前任务选择、去重并排列候选信息。 |
| ContextBudget | 约束输入预算并决定压缩或舍弃策略。 |
| ContextAssembler | 将选定信息组成模型消息与工具协议。 |
| ContextPreview | 在不调用模型的情况下解释最终选择与舍弃原因。 |

最小版仅用一个 Builder，不预先拆分 Ranker、Budget 与 Assembler。

## 两种记忆来源（完整目标）

工作记忆通过 ListRecentAsync 按归属直接读取，长期记忆通过 SearchAsync 按任务需求召回。
两路结果合并、去重后参与 Context 选择；不把工作记忆优先级与长期搜索分数直接比较。

## 设计边界

只选择和组装数据，不管理记忆生命周期或实现分词、索引构建。
工具调用消息必须保留 Call ID 关联；模型生成的检索文本不能改变可信执行上下文中的访问归属。
模型调用协议已定义于 [Contracts/LanguageModels](../../Contracts/LanguageModels/LanguageModels.md)：
组装类型化 ModelMessage、工具定义和选项形成 ModelInvocation，不将内部预览与选择元数据直接作为调用协议。
ModelToolMessage 复用 ToolObservation，压缩时仍须保留调用关联、失败或未知状态、错误与必要输出引用。
历史读取协议见 [Contracts/Conversation](../../Contracts/Conversation/Conversation.md)，分页从最近向更早读取，每页内部按 Sequence 升序。
ConversationContent 需要选择和转换，不直接当作 ModelMessage；分页边界切断工具关联时，补齐所需记录或舍弃完整片段。
只有结束信息而无文本或调用的 Assistant 历史可以保留展示，但不能直接转换为非法空 ModelAssistantMessage。

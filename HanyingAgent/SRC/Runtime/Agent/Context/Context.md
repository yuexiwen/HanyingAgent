# Context

## 功能与定位

决定当前一次模型调用需要看到哪些信息，组装类型化 ModelInvocation。
支持纯文本消息、工作记忆、统一的模型输入预算和总字符保护上限。
当前 Token 计数为明确标识的保守估算，不是匹配模型 tokenizer 的精确计数。

当前没有子目录，已有 [LocalContextBuilder](./LocalContextBuilder.cs)，实现
[IContextBuilder](../../../Contracts/Agent/Context/Operations/IContextBuilder.cs)。

## 会话历史窗口

[ConversationWindow](./ConversationWindow.cs) 在 Builder 之前读取并转换候选历史，不分配输入预算。
由 DialogueOrchestrator 提供已经记录的当前 User 消息及 Sequence；窗口不写记录、不调用模型。

- 当前用户输入仅加入一次；只选择 Sequence 更早且属于该会话的消息，最终按时间顺序发送。
- 正常片段要求相邻 User + 非空白、Stop、无工具调用的 Assistant；跨分页边界也能组成片段。
- 截断、未知、拒绝、过滤、没有回答的旧输入不纳入；本期没有工具片段、摘要或语义召回。
- 默认最多读取 10 个候选片段，可配置为 1–100；按时间正序交给 Builder。
- 每页 20 条，最多扫描 200 条旧记录，防止连续失败导致无限翻页；达到任一上限就停止。
- 对话编排器显式读取当前可信用户记忆，再将两路候选一起传入 Builder，统一决定最终带入哪些内容。
- 候选筛选或预算淘汰不删除历史；离开上下文的事实可能忘记。
- 事件 2003 记录候选片段数 CandidateTurns、扫描数、扫描中未选消息数和停止原因；
  最终选中和因预算省略的数量由 Builder 的事件 2002 记录。
  不是未扫描的全部会话消息计数，不能把它解释为删除数量。

## 当前最小实现

Builder 不依赖 Memory 或 Conversation 服务，调用方通过
[BuildContextRequest](../../../Contracts/Agent/Context/Operations/Requests/BuildContextRequest.cs) 提供：
独立系统指令、按时间排列的完整候选问答轮次和最后的当前 User 输入、已读取的 Memories，
以及可选 ModelOptions、请求级字符上限。

```text
校验与快照化输入
  → 校验已提供的记忆数据
  → 按 MemoryId 去重
  → UpdatedAt 降序、ID Ordinal 升序
  → 解析目标模型预算，预留输出和安全余量
  → 检查必须保留的 System 与当前输入
  → 为 History / Memory 分配 75% / 25% 的可选预算
  → 两路各自选取后借用未使用份额
  → System + 可选参考记忆 User 消息 + 选中的完整历史轮次 + 当前输入
  → 校验最终 Token 估算和字符总量，记录省略数量
  → ModelInvocation（Tools 为空）
```

- 原始序列只支持完整的非空白 User + 纯文本 Assistant 问答轮次，并以当前 User 结尾；不再追加 Task.Input。
- 明确拒绝原始序列内的 System、Tool、Assistant 工具调用或未知消息类型，不静默丢弃。
- 不访问记忆服务；传空 Memories 时不增加参考消息或规则。
- 输入记忆与消息集合快照化；Assistant 的空工具列表独立快照化。
- 编排层负责选择可信 Owner，Memory Manager 负责有效状态和到期过滤；
  编排器校验读取结果的归属，再把数据交给 Builder。
- 检查记忆的 Owner、ID、Content、Source 基本有效性；重复 ID 内容冲突时报错，不默默选取某份。
- 参考记忆单独作为 User 参考数据消息，正文为带标题的 JSON，保留 ID、Owner、Type、Content、Source。
- 有记忆时向系统指令追加固定的参考资料使用规则，记忆正文不进入系统指令；无记忆时不加空资料块。
- JSON 编码明确数据结构，但不保证模型绝不会服从参考文本中的恶意指令；身份授权与提示注入防护不在本阶段完成。
- 显式 MaxOutputTokens 原样保留；未指定时补上预算所使用的默认输出上限，确保实际生成也受同一输出预留约束。
  Provider JSON 适配仍属于模型模块。
- 取消和校验失败显式传播，不返回部分上下文；构建无外部读取，可同步完成，保留 Task 返回契约。
- 不读取 Conversation、不写记忆、不修改任务、不执行工具、不调用 LLM。

记忆读取集中在 Orchestrator，ContextBuilder 只接受数据；
独立测试不再需要 Memory 替身，通过可替换 Token Counter 验证预算。真实组合验证由测试调用方先读取记忆再调用 Builder，
见 [Context 测试](../../../../TEST/HanyingAgent.Tests/Agent/Context/Context.md)。

最小组合如下；调用方先完成可信 Owner 选择、授权和记忆读取：

```csharp
var store = new InMemoryWorkingMemoryStore();
IWorkingMemoryManager memory = new LocalWorkingMemoryManager(store, TimeProvider.System);
var memories = await memory.ListRecentAsync(new MemoryReadRequest(owner, Limit: 20), cancellationToken);
IModelTokenCounter tokens = new ConservativeModelTokenCounter("qwen2.5:1.5b", 4096, 256);
IContextBuilder builder = new LocalContextBuilder(tokens);
var request = new BuildContextRequest(systemInstruction, messages, memories, options);
var invocation = await builder.BuildAsync(request, cancellationToken);
```

需要记忆时由调用方先通过 memory.WriteAsync 写入；Builder 不读写记忆服务。
该示例本身只组合 Context，不会调用模型；返回值是 ModelInvocation，不是模型回复。
当前 Console 通过 DialogueOrchestrator 接入此构建器：编排器调用 ConversationWindow、
Memory Manager、ContextBuilder 和 DirectExecutor，显式读取当前用户最近最多 20 条有效工作记忆，
传入上下文后取得 ModelInvocation，再交给执行器。
不跨用户、不自动读取 Session / Task / Global 记忆；没有相关性检索或自动记忆提取。

## 统一预算与份额

输入 Token 额度 = 模型 ContextWindowTokens − MaxOutputTokens − TokenSafetyMargin。
默认配置为 4096 − 256 − 128 = 3712；系统与当前输入先占用，剩余才是可选上下文额度。

- History 默认保留 75%，Memory 默认保留 25%；字符保护额度也按相同比例分配。
  HistoryBudgetPercent 可配置为 1–99；份额不会随候选轮数增加而改变。
- 先在各自份额内选择，再把闲置额度借给另一方；没有记忆时历史可使用全部剩余额度，反之亦然。
  双方都有候选时也允许借用。借用阶段先尝试补充记忆，再扩展历史。
- 历史从近到远取完整问答轮次，遇到放不下的轮次停止，不拆分或跳过它拼接更早历史。
- 记忆按既定顺序逐条尝试；放不下的整条省略，可继续尝试后面的较小条目。
  份额是预算保留，不保证任意大小的条目都能放入；极大记忆可能一条也选不中。
- 每次试选计入完整消息、参考规则、标题、JSON 元数据及转义，并在最终组装后再次校验。
- 省略只影响本次上下文，不删除 Store 数据，不截断用户输入或记忆正文。
  日志记录选中历史轮数、记忆数、省略数、预算、输出预留和 TokenCountIsEstimate，不记录正文。
- 必须保留的 System 与当前输入本身超限才明确失败；无效数据、协议、计数器错误仍明确传播，不通过省略隐藏。

Token 能力通过 [IModelTokenCounter](../../../Contracts/Agent/LanguageModels/Operations/IModelTokenCounter.cs) 提供，
由 Host 配置与模型适配器相同的模型名称和窗口大小。
[ConservativeModelTokenCounter](../../../Infrastructure/LanguageModels/ConservativeModelTokenCounter.cs)
按每 UTF-8 字节估算一个 Token，另加每消息 8、整个请求 8 的协议余量，并预留默认 128 Token 安全空间。
它不是精确 tokenizer，也不保证任意聊天模板都不会超限；日志明确标记估算。
请求指定其他未配置模型时拒绝，不把默认模型窗口悄悄用于其他模型。
后续可替换为匹配模型 tokenizer / 聊天模板的计数实现，无须修改候选选择流程。

maxInputCharacters 仍为额外的 UTF-16 正文保护上限，默认 32,000，必须大于 0。
请求 MaxInputCharacters 可进一步收紧，不能放宽构造配置。它与 Token 额度同时约束选择，
并非 Token 换算公式；计数使用 long。正文长度不包含传输 JSON 外围。
预算不保证 HTTP 请求大小或构建过程的内存峰值。
序列化前的记忆规模和输入集合物化仍无独立硬限制，不应把它当成处理任意不可控输入的完整资源治理。

## 五步最小版验收记录与后续调整

| 步骤 | 内容 | 状态 |
|---|---|---|
| 第一步 | 确认组装规则，定义公共接口与请求。 | 已完成。 |
| 第二步 | 实现纯文本组装、合法性校验与消息快照。 | 已完成。 |
| 第三步 | 接入明确归属的工作记忆、去重与参考资料格式。 | 已完成，使用手写记忆替身独立测试。 |
| 第四步 | 总输入字符上限、精确阈值及边界验证；不把字符数当成 Token 数。 | 已完成，默认 32,000，可在构造时调整。 |
| 第五步 | 真实 Context + WorkingMemory Manager + Store 组合验收与统一文档收尾。 | 已完成，20 项真实组合测试及全套统一验收通过。 |

原五步最小版独立测试 57 项通过，覆盖原有 40 项与新增 17 项字符边界用例；20 条仍只是每 Owner 的记忆数量限制，
字符上限约束最终正文总长度，并非精确 Token 预算或完整传输请求大小限制。
另有 20 项真实组合测试通过，验证写入、更新、遗忘、状态及到期过滤、精确归属、每归属召回上限、跨归属排序和真实 JSON 字符边界。
2026-11-09 统一验收：全项目 329 项测试通过、0 失败、0 跳过，其中 Context 共 77 项；
解决方案构建 0 警告、0 错误，入口仍仅输出 HanyingAgent，应用 70 个编译项均不包含 TEST 源码。
测试分组与复现命令见 [测试项目](../../../../TEST/HanyingAgent.Tests/HanyingAgent.Tests.md)。
2026-11-19 扁平化编排后，读取、归属校验、依赖错误及取消的测试移至编排边界，
Builder 测试聚焦已提供数据的组装与预算；上述数量是历史验收记录，不是当前测试数量。
2026-11-19 统一预算调整后，ConversationWindow 只读取候选历史，Builder 统一选择历史与记忆，
并通过可替换计数器执行模型级估算预算与输出预留。
未实现摘要、压缩、相关性评分、长期记忆、Skills 或工具历史。

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
模型调用协议已定义于 [Contracts/Agent/LanguageModels](../../../Contracts/Agent/LanguageModels/LanguageModels.md)：
组装类型化 ModelMessage、工具定义和选项形成 ModelInvocation，不将内部预览与选择元数据直接作为调用协议。
ModelToolMessage 复用 ToolObservation，压缩时仍须保留调用关联、失败或未知状态、错误与必要输出引用。
历史读取协议见 [Contracts/Agent/Capabilities/Conversation](../../../Contracts/Agent/Capabilities/Conversation/Conversation.md)，分页从最近向更早读取，每页内部按 Sequence 升序。
ConversationContent 需要选择和转换，不直接当作 ModelMessage；分页边界切断工具关联时，补齐所需记录或舍弃完整片段。
只有结束信息而无文本或调用的 Assistant 历史可以保留展示，但不能直接转换为非法空 ModelAssistantMessage。

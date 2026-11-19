# Context

## 功能与定位

承载 Context 五步最小版的接口、独立行为与真实组件组合测试，不依赖真实模型、对话服务或数据库。

## 当前测试

[LocalContextBuilderTests](./LocalContextBuilderTests.cs) 直接提供消息与 MemoryItem，
Builder 不再注入 Memory 服务，不需要记忆替身。

- 纯文本组装、历史顺序、当前用户输入不重复、空 Tools 和 Options 传递。
- 空请求、空字段、空白文本、空消息与 null 项、最后一条非 User 等非法输入。
- 明确拒绝嵌入 System、工具结果、Assistant 工具调用与未知消息类型。
- 只组装明确提供的数据，不主动访问其他服务；读取范围和 Limit 由编排器测试验证。
- 没有记忆时不插入空参考消息，也不额外修改系统指令。
- 重复 MemoryId 去重，跨 Owner 统一按时间与 Ordinal ID 排列。
- 参考记忆使用带标签的普通 User 消息，以 JSON 编码 ID、Owner、Type、Content 和 Source。
- 记忆文本不进入高优先级系统指令，换行、引号等数据可以从 JSON 恢复；不声称完成提示注入防护。
- 提供的记忆有默认 ID、null 项、空白内容或来源、冲突的重复 ID 时明确失败。
- 快照化消息、Memories 与 Assistant 空工具列表；不修改请求或调用方原始集合。
- ModelInvocation 和 BuildContextRequest 的 JSON 协议、必需字段及 role 判别符。
- 预取消明确传播，不返回部分上下文；记忆读取期间取消与依赖失败由编排器测试覆盖。
- 构造拒绝非正字符上限；默认 32,000 与自定义上限均验证上限前一字符、刚好等于及超出一字符。
- 所有系统、历史和当前正文共用一个预算，Options、输出 Token 选项及传输 JSON 外围不计入。
- 最终记忆计量包括追加规则、标题、元数据、完整 JSON 转义；多归属共用总上限，重复记忆只计一次。
- 使用 UTF-16 长度，验证代理项、换行和累计超过 int.MaxValue 不溢出；大累计测试复用同一字符串，不分配数 GB 文本。
- 原始输入或记忆加入后超限均明确失败，不修改请求或自动裁剪，失败后可重复调用。

## 真实组件组合测试

[ContextBoundaryTests](./ContextBoundaryTests.cs) 直接组合 LocalContextBuilder、
LocalWorkingMemoryManager 与 InMemoryWorkingMemoryStore；只替换时间源为已有 ManualTimeProvider，
不以替身替代这三个被验收组件。测试调用方显式从 Manager 读取记忆后，将数据交给 Builder，
不再由 Builder 嵌套读取。每个测试独立构造状态，不依赖真实时间等待或执行顺序。

| 验收点 | 用例数 | 证明的行为 |
|---|---:|---|
| 记忆生命周期 | 1 | 写入、更新、物理遗忘对新上下文可见，旧调用结果不被后续更新改写。 |
| 五种状态 | 5 | 只有 Active / Confirmed 被引用，其他合法状态仍保存在 Store 中但不进入上下文。 |
| 到期与恢复 | 5 | 当前时间严格早于到期时间才可引用；时间前进移除最后一条参考及系统附加规则；重新激活不能绕过到期，清除有效期可恢复。 |
| 精确归属 | 2 | Task / Session / User / Global、不同及大小写不同 ID 不串归属；等值重复归属不重复引用，空归属不自动包含 Global。 |
| 召回与排序 | 2 | 两个归属各有 25 条有效记忆时各取最近 20 条，更新更近的无效记忆不占名额；跨归属统一排序，同时间按 ID Ordinal 排列，更新后重新排序。 |
| 最终字符边界 | 4 | 真实记忆 JSON、转义、系统规则、历史及当前正文参与计数；等于及少于上限允许，超出一字符失败；默认上限拒绝过大记忆且不自动裁剪，更新后可复用 Builder。 |
| 取消 | 1 | 预取消明确传播且不修改存储，之后仍可正常构建。 |

成功、超限及过滤操作同时检查存储记录，防止 Builder 在组装过程中修改或删除记忆；
Builder 的构造依赖中已没有记忆接口。

调用顺序、当前用户归属、读取失败、不合作依赖返回后的取消检查和模型调用隔离，
见 [DialogueTests](../Conversation/DialogueTests.cs)。执行器测试验证直接接收 ModelInvocation。

## 统一验收与未完成范围

历史记录：2026-11-09 Context 共 77 项通过，包括 57 项独立测试和 20 项真实组合测试，五步最小版验收完成。
全项目 329 项测试通过，失败和跳过均为 0；解决方案构建为 0 警告、0 错误。
上述是旧边界下的验收数量。2026-11-19 扁平化后已同步调整测试职责，不将历史数量当作当前结果。
Context 测试不调用真实 LLM，不以字符数冒充模型 Token 窗口保证。
未实现精确 Token 预算、摘要、工具历史、长期记忆和自动对话加载，也不代表远程适配或生产级资源治理已完成。

在 HanyingAgent 项目根目录运行全部 Context 测试：

```powershell
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --no-restore --filter "FullyQualifiedName~HanyingAgent.Tests.Context."
```

仅运行真实组合测试：

```powershell
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --no-restore --filter "FullyQualifiedName~HanyingAgent.Tests.Context.ContextBoundaryTests"
```

# HanyingAgent

## 功能与定位

面向学习的 Agent Runtime，采用单个 .NET 10 Console Project、单进程的模块化单体结构，通过明确契约为后续远程适配保留边界。

当前已定义 Session、Task、User、Conversation、Memory、Tools、LanguageModels 与 Context 契约；除工作记忆管理器和 Context Builder 外，其余业务模块主要仍为目录骨架，尚无模型客户端，程序入口仅输出项目名称。
WorkingMemory 已建立内部存储端口、正式 xUnit 测试项目、测试用可控时钟，并实现进程内存 Store 和工作记忆 Manager。
当前已分别验证 Store 与 Manager 的独立行为，并增加真实组合的跨层边界与并发测试。
五步现已完成，内存版 WorkingMemory MVP 此前已通过统一验收：252 个正式测试全部通过，构建 0 警告、0 错误。
模块可供后续调用方通过公共接口使用，但未在程序入口接入记忆链路。
数据不持久化，自动提取与评分、身份授权、容量治理和长期记忆仍未实现。

Context 五步最小版已完成：接口、纯文本请求组装、明确归属的工作记忆接入、总输入字符上限和真实组件组合验收，
共 57 项独立测试与 20 项真实组合测试通过。字符上限默认 32,000，可构造配置；精确 Token 预算与 Agent 接入仍未实现。
最新统一验收：全项目 329 项测试通过、0 失败、0 跳过，解决方案构建 0 警告、0 错误；程序入口行为不变。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [SRC](./SRC/SRC.md) | 按契约、交互入口、运行控制、业务能力与基础设施组织应用模块。 |
| [TEST](./TEST/TEST.md) | 正式 xUnit 测试项目，验证 WorkingMemory 及 Context 最小版的独立与真实组合行为。 |

## 根目录与设计边界

- [HanyingAgent.csproj](./HanyingAgent.csproj) 定义项目构建，并排除 TEST 子树，防止测试源码混入应用。
- [HanyingAgent.slnx](./HanyingAgent.slnx) 组织应用与测试项目，便于编辑器发现和统一构建。
- [Program.cs](./Program.cs) 是入口，后续负责依赖注入与启动组合，不承载具体业务规则。
- bin、obj 是生成目录，不编写模块文档，也不作为业务模块维护。
- 目录分组不等于微服务划分；业务模块通过契约协作，具体实现由入口组合。

整体设计背景见[设计初稿](<../.docs/Mini Agent Runtime 设计初稿.md>)。
模块边界与端到端执行过程见[模块职责与运行流程](<../.docs/HanyingAgent 模块职责与运行流程.md>)。
工作记忆的本期范围、验收标准和扩展方向见[WorkingMemory 最小实现与演进计划](<../.docs/WorkingMemory 最小实现与演进计划.md>)。

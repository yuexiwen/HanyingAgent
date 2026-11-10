# TEST

## 功能与定位

承载 UT 与契约测试，验证模块规则、边界数据和本地 / 远程适配器的行为约定。

已建立正式 xUnit 测试项目，当前覆盖 WorkingMemory 的 ID、Owner、可控时钟、内存 Store 与 Manager 单元测试。
真实 Manager + Store 的跨层边界和并发测试已完成，第五步统一验收共 252 个用例全部通过，失败和跳过均为 0。
这是内存版 WorkingMemory 的验收，不包含 Agent 接入或长期记忆实现。
Context 五步最小版也已完成，包含 57 项独立测试和 20 项真实 Builder + Manager + Store 组合测试，
验证纯文本请求组装、工作记忆接入、总输入字符上限及跨组件边界。
最新全项目统一验收共 329 项全部通过、0 失败、0 跳过，解决方案构建 0 警告、0 错误。
252 是 WorkingMemory 的历史验收数量；精确 Token 预算、真实模型调用和 Agent 接入仍未实现。

## 设计边界

- 测试项目通过 ProjectReference 引用应用，不复制生产源码。
- 应用项目已排除 TEST 子树；后续测试源码及测试生成文件不应进入应用编译。
- 优先覆盖 ID 与 MemoryOwner 校验、DTO 序列化、接口行为和状态转换。
- 工作记忆与长期记忆分别测试写后读取可见性和索引延迟，不能假设长期写入立即可搜索。
- 模型与工具测试优先使用可预测替身，不依赖真实付费服务或执行真实副作用。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [HanyingAgent.Tests](./HanyingAgent.Tests/HanyingAgent.Tests.md) | 独立引用应用的 .NET 10 xUnit 项目，按被测模块组织。 |

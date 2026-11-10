# LanguageModels

## 功能与定位

验证模型适配器的协议转换和失败边界，默认不依赖 Ollama、网络、GPU 或模型回答质量。

当前没有子目录，已有 [OllamaLanguageModelRuntimeTests](./OllamaLanguageModelRuntimeTests.cs)。
测试使用真实 HttpClient 与手写 HttpMessageHandler，捕获实际序列化请求并返回可控响应，
不引入 Mock 库或 Provider SDK。

## 测试范围

- 构造依赖、默认模型、正数上下文窗口、HTTP(S) BaseAddress、路径结尾及 query / fragment 校验。
- 原生 POST api/chat、路径前缀、消息顺序、中文及转义、角色转换和 stream = false。
- 默认模型与逐次覆盖、num_ctx / temperature / num_predict 映射，未指定选项不写入 JSON。
- 空请求、集合、消息和工具项，以及非法模型名、文本、非有限或负温度、非正输出上限，在发送前失败。
- 工具定义、工具历史和未知消息类型明确拒绝，不静默省略。
- Stop / Length / Unknown 与原始结束原因；缺失原因不默认 Stop，截断不伪装成完整回答。
- 用量未知、已知 0、单项未知、负数及超出 int 范围的响应；不把未知用量记为 0。
- 不完整结果、空正常回复、错误角色、服务 error、非法 JSON 及意外 NDJSON 均明确失败。
- 不接受服务擅自返回的工具调用和图像，不把 thinking 当成最终回复。
- HTTP 400 / 404 / 429 / 500 / 502 保留状态码且不自动重试，不在异常中回显原始错误正文；网络异常原样传播。
- 预取消、请求等待中取消，以及响应头到达后响应体仍阻塞时 HttpClient.Timeout 的覆盖。
- 成功或解析失败时释放响应内容，但不释放调用方注入的 HttpClient。

## 已验证结果

2026-11-10：89 项离线测试全部通过；与 77 项 Context 测试合并回归共 166 项通过、0 失败、0 跳过。
解决方案构建为 0 警告、0 错误。本轮未重跑其余 252 项 WorkingMemory / Support 历史用例。

另行使用临时验证程序组合真实 Context 与适配器，通过回环地址调用本机 qwen2.5:1.5b，
只发送合成算术问题，返回 5、Stop 和服务报告的输入 33 / 输出 2 Tokens。
临时程序不进入项目；可参考 [适配器使用示例](../../../SRC/Infrastructure/LanguageModels/LanguageModels.md) 独立复现。
此手工联调不属于上述 89 项离线用例，不要求所有开发者安装模型。

## 运行

在 HanyingAgent 项目根目录运行适配器测试：

```powershell
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --no-restore --filter "FullyQualifiedName~HanyingAgent.Tests.LanguageModels."
```

合并 Context 回归：

```powershell
dotnet test .\TEST\HanyingAgent.Tests\HanyingAgent.Tests.csproj --no-restore --filter "FullyQualifiedName~HanyingAgent.Tests.LanguageModels.|FullyQualifiedName~HanyingAgent.Tests.Context."
```

## 未完成范围

流式、工具调用、其他 Provider、认证框架和 Agent 接入仍未实现。
上下文 Token 计数、超长输入治理与模型能力质量评估不由这些协议测试保证。

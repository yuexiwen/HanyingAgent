# LanguageModels

## 功能与定位

将统一模型协议适配到具体 Provider，实现请求发送、响应解析及后续流式输出。

当前没有子目录，已有 [OllamaLanguageModelRuntime](./OllamaLanguageModelRuntime.cs)，实现
[ILanguageModelRuntime](../../Contracts/LanguageModels/Operations/ILanguageModelRuntime.cs)。
它使用 .NET 自带 HttpClient 与 JSON 序列化，通过 Ollama 原生 POST /api/chat 提供纯文本、非流式调用，
不引入 Provider SDK，也不改变公共契约。

## 最小使用方式

以下为独立调用示例；Ollama 服务和模型需由运行环境事先准备，不要求打开 ollama run 聊天客户端：

```csharp
using HanyingAgent.Contracts.LanguageModels.Models;
using HanyingAgent.Contracts.LanguageModels.Operations;
using HanyingAgent.Contracts.LanguageModels.Operations.Requests;
using HanyingAgent.Infrastructure.LanguageModels;

using var client = new HttpClient
{
    BaseAddress = new Uri("http://127.0.0.1:11434/"),
    Timeout = TimeSpan.FromMinutes(2)
};
ILanguageModelRuntime runtime = new OllamaLanguageModelRuntime(
    client, "qwen2.5:1.5b", contextWindowTokens: 2048);
var invocation = new ModelInvocation(
    [new ModelSystemMessage("You are a concise assistant."),
     new ModelUserMessage("What is 2 + 3? Reply with only the number.")],
    [],
    new ModelOptions(Temperature: 0, MaxOutputTokens: 32));
var response = await runtime.InvokeAsync(invocation, CancellationToken.None);
Console.WriteLine(response.Text);
```

实际调用方也可以直接传入 Context 返回的 ModelInvocation。当前入口已通过 Generic Host / DI、DirectExecutor 和 Console 接入，
详见[应用运行文档](../../../HanyingAgent.md)；上面仍是独立调用示例。
生产调用方应复用 HttpClient 或使用 HttpClientFactory 管理其生命周期，不要为每次请求重复创建客户端。

## 配置与请求映射

| 配置或输入 | 行为 |
|---|---|
| HttpClient.BaseAddress | 必须是绝对 HTTP(S) URL，路径以 / 结尾，无 query 或 fragment；保留反向代理路径前缀，再追加 api/chat。 |
| HttpClient.Timeout | 由调用方设置，覆盖等待响应及完整响应体；适配器不修改客户端配置。 |
| defaultModelName | 必需且非空白，只有 Options.ModelName 为 null 时才采用。 |
| contextWindowTokens | 构造时配置，默认 4096，必须大于 0；每次请求映射为 options.num_ctx，不修改模型文件。 |
| Messages | 快照并按顺序转换为 role / content，支持 System、User 和非空白纯文本 Assistant。 |
| Tools | 必须为空；工具定义、Assistant 工具调用、Tool 消息及未知消息类型明确拒绝，不降级成文本。 |
| Options.ModelName | 非 null 时必须非空白，映射为 model，不静默切换默认模型。 |
| Options.Temperature | 可空；显式值必须有限且非负，映射为 options.temperature。目标模型的进一步约束由服务明确返回。 |
| Options.MaxOutputTokens | 可空；显式值必须大于 0，映射为 options.num_predict，不传负数表示无限生成。 |
| 非流式 | 固定 stream = false；不指定的可空生成选项不写入 JSON，保留服务默认行为。 |

客户端由调用方持有并释放；适配器仅释放每次请求、响应与内容。不自动下载模型、启动服务、重试或回退到其他模型。
Ollama 原生服务不因填写模型名称而获得访问保护，远程部署仍需由可信网络或认证代理控制访问。

## 响应与失败

- 要求有效 JSON、done = true、assistant 角色和非 null 文本字段；缺失或不完整响应不伪造成成功。
- done_reason = stop 映射为 Stop，必须有非空白文本；length 映射为 Length，允许保留部分文本。
- 其他或缺失原因映射为 Unknown，保留 ProviderFinishReason 原值；未知原因不意味着任务完成。
- 输出 ToolCalls 恒为空。服务擅自返回工具调用、tool_calls 结束原因或图像时明确拒绝，不静默丢弃可执行指令。
- thinking 不拼入 Text；只使用最终 content。已知正常文本不 trim 或改写，Length / Unknown 的空白内容表达为 null。
- prompt_eval_count / eval_count 映射输入、输出用量；未知项为 null，实际报告的 0 保持为 0，两项都未知则 Usage 为 null。
- 负数用量、非法消息或成功状态中的 error 字段显式抛 InvalidOperationException；格式错误抛 JsonException。
- HTTP 非成功状态抛 HttpRequestException 并保留 StatusCode，不把原始响应正文附加到错误消息中。
- 连接异常、超时及取消显式传播，无成功形空回复或自动重试。取消本地等待不证明远端生成已立即停止。

## 验证与范围

2026-11-10：89 项离线适配器测试与 77 项 Context 回归测试合计 166 项通过，失败和跳过均为 0。
解决方案构建 0 警告、0 错误；入口仍仅输出 HanyingAgent，应用 71 个编译项不包含 TEST。
另通过实际 C# 适配器及真实 Context 调用本机 Ollama 0.40.16 / qwen2.5:1.5b：
合成问题 2 + 3 返回 5，结束原因为 Stop，服务报告输入 33、输出 2 Tokens。
真实联调不是默认测试依赖；离线测试不要求本机安装 Ollama。详见 [LanguageModels 测试](../../../TEST/HanyingAgent.Tests/LanguageModels/LanguageModels.md)。

当前不提供流式、工具调用、多模态、模型目录、自动拉取、认证配置框架或 Agent 循环。
可选 ILogger 构造参数用于记录调用边界和用量，未传时保留无日志的独立使用方式。
num_ctx 是运行配置，不是输入 Token 计数器；本适配器不估算 Token、不裁剪或压缩消息，
也不能保证 Ollama 不按自身策略处理超长输入。Context 的 32,000 字符上限不保证请求能放入此窗口，
仍需后续设计精确输入预算并预留输出空间。

## 设计边界

不自行检索记忆、构建业务 Context 或修改任务状态。
工具调用的 ID、参数、结果关联和结束信息必须正确保留，失败与取消不可转为成功回答。
密钥来自安全配置，不写入源码或原始日志；具体服务依赖仅在实际实现时引入。
ModelToolMessage 复用 ToolObservation，适配器将其映射为正确工具角色和调用 ID，并在内容中保留状态、错误和必要输出引用。
统一 ModelResponse 使用 Text 而非 FinalAnswer，保留截断、拒绝、未知原因及未知用量；流式能力仍待后续设计。

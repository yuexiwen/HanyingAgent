# MCP

## 功能与定位

独立的 stdio MCP 接入层。应用通过一个统一入口访问多个 Server，
内部每个 Server 独立维护一个 SDK 连接；本期不接入 Agent、LocalToolManager 或 Console 命令。
协议握手、传输和子进程管理使用官方 ModelContextProtocol.Core SDK，不自行实现 JSON-RPC。

## 组成

| 文件 | 职责 |
|---|---|
| [McpServerOptions](./McpServerOptions.cs) | 定义启动配置、超时、stdio 命令、参数、工作目录与环境。 |
| [McpConnectionManager](./McpConnectionManager.cs) | 校验并复制配置，按需建立和复用连接，协调并发与退出清理。 |
| [McpToolClient](./McpToolClient.cs) | 按 ServerName 发现工具，按 ServerName + ToolName 调用工具。 |

注册配置不是连接，也不是工具目录。Host 启动时校验全部注册项，包括禁用项；
重复名称、空命令、非法超时或工作目录明确拒绝。名称区分大小写。
首次发现或调用才启动 Server。每个 Server 的请求串行执行，不同 Server 可并发访问。
本期配置固定到进程结束，不支持运行中注册、修改或移除。

## 配置

[appsettings.json](../../../appsettings.json) 默认 `Mcp.Servers` 为空，不启动任何外部进程。
以下仅为配置示例，需替换为已安装且可信的 Server：

```json
{
  "Mcp": {
    "Servers": [
      {
        "Name": "example",
        "Enabled": true,
        "ConnectionTimeoutSeconds": 30,
        "RequestTimeoutSeconds": 60,
        "Stdio": {
          "Command": "dotnet",
          "Arguments": ["C:\\McpServers\\ExampleServer.dll"],
          "WorkingDirectory": "C:\\McpServers",
          "Environment": {}
        }
      }
    ]
  }
}
```

Command 由 SDK 作为进程启动，不拼接 shell 命令。WorkingDirectory 可省略；
指定时必须是已存在的绝对路径。Server 的 stdout 只能用于 MCP 协议，日志应写 stderr。
配置会执行宿主权限下的本地程序，不是安全沙箱；只注册经过批准的可信命令。
不要把秘密提交到配置文件。进程只继承 SDK 的基础环境白名单，再应用显式 Environment，
不整体复制宿主环境；显式配置仍可能将敏感值传给 Server。
SDK 与 Server 的 stderr 日志可能包含外部程序提供的信息，不保证其自动脱敏。

## 使用

从 DI 解析 McpToolClient，显式传递取消令牌：

```csharp
var tools = await client.ListToolsAsync("example", cancellationToken);
var result = await client.CallToolAsync(
    "example", "echo",
    new Dictionary<string, object?> { ["text"] = "hello" },
    cancellationToken);
```

ListToolsAsync 自动读取工具分页，保留原始 Tool 定义与 Schema；重复分页游标明确失败。
每次发现重新请求，不维护应用侧工具目录缓存。
CallToolAsync 返回原始 CallToolResult，保留 Content、StructuredContent 和 IsError。
工具层 IsError 由调用方判断，不自动转换为异常；未知工具、协议错误、连接失败和取消明确传播。
两个 Server 的同名工具互不覆盖。工具参数的顶层字典会复制，嵌套可变对象仍由调用方管理。

## 生命周期与限制

- ConnectionTimeoutSeconds 覆盖连接与握手；RequestTimeoutSeconds 从连接成功后开始，
  覆盖一次调用或完整分页发现，不包含排队时间。排队可由调用方或 Host 停止令牌取消。
- 失败的工具调用绝不自动重试。SDK 确认连接已结束后，后续新请求可以重新建立连接；
  连接初始化失败也可由调用方另行发起新请求。
- 取消和超时会停止本地等待，不保证 Server 已撤销副作用。
- Host 停止拒绝新请求，取消进行中的访问，等待其释放连接后由 SDK 清理启动的子进程。
  重复清理幂等；同时支持异步释放和现有 Host 的同步 Dispose。
- 公共入口暂时依赖 MCP SDK 协议类型，属于基础设施 API，不是 Agent 公共契约。
  后续接入业务层时再增加适配，不把 SDK 类型直接带入 Agent 契约。
- 本期没有 HTTP、认证、动态注册、用户身份隔离、sampling、elicitation、
  Agent 工具调用循环或 MCP 到 ToolObservation 的映射。

测试使用[本地测试 Server](../../../TEST/HanyingAgent.McpTestServer/Program.cs)
和[接入测试](../../../TEST/HanyingAgent.Tests/Infrastructure/Mcp/McpToolClientTests.cs)，不连接真实外部服务。

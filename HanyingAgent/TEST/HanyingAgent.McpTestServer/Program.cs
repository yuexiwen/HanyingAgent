using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

if (Environment.GetEnvironmentVariable("MCP_TEST_START_LOG") is { } startLog)
{
    await File.AppendAllTextAsync(startLog, $"{Environment.ProcessId}{Environment.NewLine}");
}

if (Environment.GetEnvironmentVariable("MCP_TEST_PID_FILE") is { } pidFile)
{
    var pending = pidFile + ".pending";
    await File.WriteAllTextAsync(pending, Environment.ProcessId.ToString());
    File.Move(pending, pidFile);
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<TestTools>();
await builder.Build().RunAsync();

[McpServerToolType]
public sealed class TestTools
{
    [McpServerTool(Name = "identity"), Description("Returns this test server's process identity.")]
    public static CallToolResult Identity() => new()
    {
        Content = [new TextContentBlock { Text = Environment.ProcessId.ToString() }],
        StructuredContent = JsonSerializer.SerializeToElement(new
        {
            processId = Environment.ProcessId,
            label = Environment.GetEnvironmentVariable("MCP_TEST_LABEL"),
            secretPresent = Environment.GetEnvironmentVariable("HANYING_MCP_TEST_SECRET") is not null
        })
    };

    [McpServerTool(Name = "echo"), Description("Echoes the supplied text.")]
    public static string Echo(string text) => text;

    [McpServerTool(Name = "failure"), Description("Returns a tool-level failure.")]
    public static CallToolResult Failure() => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = "Expected test failure." }]
    };

    [McpServerTool(Name = "wait"), Description("Waits for cancellation.")]
    public static async Task<string> Wait(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return "unreachable";
    }

    [McpServerTool(Name = "exit"), Description("Terminates this test server.")]
    public static string Exit()
    {
        Environment.Exit(7);
        return "unreachable";
    }
}

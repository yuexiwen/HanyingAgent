using System.Diagnostics;
using HanyingAgent.Host;
using HanyingAgent.Infrastructure.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace HanyingAgent.Tests.Infrastructure.Mcp;

public sealed class McpToolClientTests
{
    [Fact]
    public async Task Host_ValidatesConfigurationAtStartupWithoutStartingServer()
    {
        var builder = ApplicationHost.CreateBuilder([]);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mcp:Servers:0:Name"] = "lazy",
            ["Mcp:Servers:0:Stdio:Command"] = "nonexistent-hanying-mcp-server"
        });
        using var host = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token);
        Assert.NotNull(host.Services.GetRequiredService<McpToolClient>());
        await host.StopAsync(timeout.Token);

        var invalid = ApplicationHost.CreateBuilder([]);
        invalid.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mcp:Servers:0:Name"] = "invalid"
        });
        using var invalidHost = invalid.Build();
        await Assert.ThrowsAsync<ArgumentException>(() => invalidHost.StartAsync(timeout.Token));
    }

    [Fact]
    public async Task ListAndCall_PreserveSchemasStructuredContentAndServerRouting()
    {
        await using var connections = Manager(Server("a", "first"), Server("b", "second"));
        var client = new McpToolClient(connections);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var tools = await client.ListToolsAsync("a", timeout.Token);
        var echo = Assert.Single(tools, tool => tool.Name == "echo");
        Assert.True(echo.InputSchema.GetProperty("properties").TryGetProperty("text", out _));
        var response = await client.CallToolAsync("a", "echo", new Dictionary<string, object?>
        {
            ["text"] = "hello"
        }, timeout.Token);
        Assert.Contains(response.Content, block => block is TextContentBlock text && text.Text.Contains("hello"));

        var first = await IdentityAsync(client, "a", timeout.Token);
        var second = await IdentityAsync(client, "b", timeout.Token);
        Assert.NotEqual(first.ProcessId, second.ProcessId);
        Assert.Equal("first", first.Label);
        Assert.Equal("second", second.Label);
        Assert.False(first.SecretPresent);
    }

    [Fact]
    public async Task ConcurrentFirstAccess_ReusesOneProcessAndDisposalTerminatesIt()
    {
        var connections = Manager(Server("one"));
        var client = new McpToolClient(connections);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Process? process = null;
        try
        {
            var responses = await Task.WhenAll(Enumerable.Range(0, 6)
                .Select(_ => IdentityAsync(client, "one", timeout.Token)));
            process = Process.GetProcessById(responses[0].ProcessId);
            Assert.All(responses, response => Assert.Equal(process.Id, response.ProcessId));
        }
        finally
        {
            await connections.DisposeAsync();
        }
        Assert.NotNull(process);
        using (process)
        {
            Assert.True(process.HasExited);
        }
        await connections.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ListToolsAsync("one", timeout.Token));
    }

    [Fact]
    public async Task CancellationAndRequestTimeout_DoNotPreventLaterCalls()
    {
        var options = Server("one");
        options.RequestTimeoutSeconds = 1;
        await using var connections = Manager(options);
        var client = new McpToolClient(connections);
        using var overall = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var identity = await IdentityAsync(client, "one", overall.Token);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.CallToolAsync("one", "wait", null, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.CallToolAsync("one", "wait", null, overall.Token));
        var after = await IdentityAsync(client, "one", overall.Token);
        Assert.Equal(identity.ProcessId, after.ProcessId);
    }

    [Fact]
    public async Task ToolFailureIsPreservedAndConnectionLossIsNotRetried()
    {
        var startLog = Path.GetTempFileName();
        var options = Server("one");
        options.Stdio.Environment["MCP_TEST_START_LOG"] = startLog;
        try
        {
            await using var connections = Manager(options);
            var client = new McpToolClient(connections);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var before = await IdentityAsync(client, "one", timeout.Token);
            var result = await client.CallToolAsync("one", "failure", null, timeout.Token);
            Assert.True(result.IsError);
            Assert.Contains(result.Content, block => block is TextContentBlock text && text.Text == "Expected test failure.");
            await Assert.ThrowsAsync<McpProtocolException>(() =>
                client.CallToolAsync("one", "unknown-tool", null, timeout.Token));
            var error = await Record.ExceptionAsync(() => client.CallToolAsync("one", "exit", null, timeout.Token));
            Assert.NotNull(error);
            Assert.Single(await File.ReadAllLinesAsync(startLog, timeout.Token));
            var after = await IdentityAsync(client, "one", timeout.Token);
            Assert.NotEqual(before.ProcessId, after.ProcessId);
            Assert.Equal(2, (await File.ReadAllLinesAsync(startLog, timeout.Token)).Length);
        }
        finally
        {
            File.Delete(startLog);
        }
    }

    [Fact]
    public async Task UnknownDisabledAndUnreachableServers_FailExplicitly()
    {
        var disabled = Server("disabled");
        disabled.Enabled = false;
        var missing = Server("missing");
        missing.Stdio.Command = "nonexistent-hanying-mcp-server";
        await using var connections = Manager(disabled, missing);
        var client = new McpToolClient(connections);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => client.ListToolsAsync("unknown", timeout.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListToolsAsync("disabled", timeout.Token));
        Assert.NotNull(await Record.ExceptionAsync(() => client.ListToolsAsync("missing", timeout.Token)));
    }

    [Fact]
    public async Task Shutdown_CancelsInFlightOperationAndPreventsNewCalls()
    {
        var connections = Manager(Server("one"));
        var client = new McpToolClient(connections);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await IdentityAsync(client, "one", timeout.Token);
        var waiting = client.CallToolAsync("one", "wait", null, timeout.Token);
        await connections.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ListToolsAsync("one", timeout.Token));
    }

    [Fact]
    public async Task DifferentServers_CanBeAccessedWhileOneIsBusy()
    {
        await using var connections = Manager(Server("a"), Server("b"));
        var client = new McpToolClient(connections);
        using var overall = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await IdentityAsync(client, "a", overall.Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(overall.Token);
        var waiting = client.CallToolAsync("a", "wait", null, cancellation.Token);
        try
        {
            await IdentityAsync(client, "b", overall.Token);
            Assert.False(waiting.IsCompleted);
        }
        finally
        {
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnresponsiveHandshake_TimeoutOrCancellationTerminatesProcess(bool cancelByCaller)
    {
        var pidFile = Path.Combine(Path.GetTempPath(), $"hanying-mcp-{Guid.NewGuid():N}.pid");
        var options = Server("unresponsive");
        options.ConnectionTimeoutSeconds = cancelByCaller ? 10 : 3;
        options.Stdio.Environment["MCP_TEST_PID_FILE"] = pidFile;
        await using var connections = Manager(options);
        var client = new McpToolClient(connections);
        using var overall = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(overall.Token);
        var connecting = client.ListToolsAsync("unresponsive", caller.Token);
        try
        {
            while (!File.Exists(pidFile))
            {
                await Task.Delay(20, overall.Token);
            }
            var pid = await File.ReadAllTextAsync(pidFile, overall.Token);
            using var process = Process.GetProcessById(int.Parse(pid));
            if (cancelByCaller)
            {
                await caller.CancelAsync();
            }
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting);
            await process.WaitForExitAsync(overall.Token);
            Assert.True(process.HasExited);
        }
        finally
        {
            await caller.CancelAsync();
            _ = await Record.ExceptionAsync(() => connecting);
            File.Delete(pidFile);
            File.Delete(pidFile + ".pending");
        }
    }

    [Fact]
    public void DuplicateNamesAndInvalidTimeouts_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => Manager(Server("same"), Server("same")));
        var invalid = Server("invalid");
        invalid.RequestTimeoutSeconds = 0;
        Assert.Throws<ArgumentException>(() => Manager(invalid));
    }

    [Fact]
    public async Task Configuration_IsSnapshottedAndEnvironmentIsNotFullyInherited()
    {
        var options = Server("one", "original");
        await using var connections = Manager(options);
        options.Stdio.Command = "nonexistent-hanying-mcp-server";
        options.Stdio.Environment["MCP_TEST_LABEL"] = "modified";
        var client = new McpToolClient(connections);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var previous = Environment.GetEnvironmentVariable("HANYING_MCP_TEST_SECRET");
        try
        {
            Environment.SetEnvironmentVariable("HANYING_MCP_TEST_SECRET", "test-only-value");
            var response = await IdentityAsync(client, "one", timeout.Token);
            Assert.Equal("original", response.Label);
            Assert.False(response.SecretPresent);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HANYING_MCP_TEST_SECRET", previous);
        }
    }

    private static McpConnectionManager Manager(params McpServerOptions[] servers) =>
        new(Options.Create(new McpSettings { Servers = servers.ToList() }), NullLoggerFactory.Instance);

    private static McpServerOptions Server(string name, string label = "test") => new()
    {
        Name = name,
        ConnectionTimeoutSeconds = 10,
        Stdio = new McpStdioOptions
        {
            Command = "dotnet",
            Arguments = [Path.Combine(AppContext.BaseDirectory, "McpTestServer", "HanyingAgent.McpTestServer.dll")],
            Environment = new Dictionary<string, string> { ["MCP_TEST_LABEL"] = label }
        }
    };

    private static async Task<Identity> IdentityAsync(McpToolClient client, string serverName, CancellationToken token)
    {
        var result = await client.CallToolAsync(serverName, "identity", null, token);
        var body = result.StructuredContent!.Value;
        return new Identity(body.GetProperty("processId").GetInt32(),
            body.GetProperty("label").GetString(), body.GetProperty("secretPresent").GetBoolean());
    }

    private sealed record Identity(int ProcessId, string? Label, bool SecretPresent);
}

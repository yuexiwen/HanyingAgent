using System.Text.Json;
using HanyingAgent.Contracts.Agent.Capabilities.Tools.Models;
using HanyingAgent.Contracts.Agent.Capabilities.Tools.Operations;
using HanyingAgent.Contracts.Agent.Capabilities.Tools.Operations.Requests;
using HanyingAgent.Contracts.Agent.Capabilities.Tools.Operations.Responses;
using HanyingAgent.Contracts.Management.Sessions.Models;
using HanyingAgent.Contracts.Management.Sessions.Operations;
using HanyingAgent.Contracts.Management.Sessions.Operations.Requests;
using HanyingAgent.Contracts.Management.Tasks.Models;
using HanyingAgent.Contracts.Management.Tasks.Operations.Requests;
using HanyingAgent.Contracts.Management.Users.Operations;
using HanyingAgent.Contracts.Management.Users.Operations.Requests;
using HanyingAgent.Host;
using HanyingAgent.Runtime.Agent.Capabilities.Tools;
using HanyingAgent.Runtime.Agent.Capabilities.Tools.Implementations;
using HanyingAgent.Runtime.Management.Tasks;
using HanyingAgent.Runtime.Management.Sessions;
using HanyingAgent.Runtime.Management.Users;
using HanyingAgent.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HanyingAgent.Tests.Agent.Capabilities.Tools;

public sealed class LocalToolManagerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HostRegistration_TimeToolReturnsUtcAndCorrelatedObservation()
    {
        using var host = CreateHost();
        var (task, _) = await CreateTaskAsync(host.Services);
        var clock = new ManualTimeProvider(Now);
        var manager = CreateManager(host.Services, clock, new GetCurrentTimeTool(clock));
        var request = Request(task);

        var observation = await manager.InvokeAsync(request, CancellationToken.None);

        Assert.IsType<LocalToolManager>(host.Services.GetRequiredService<IToolManager>());
        Assert.Equal(request.Call.Id, observation.CallId);
        Assert.Equal("get_current_time", observation.ToolName);
        Assert.Equal(ToolCallStatus.Succeeded, observation.Status);
        using var body = JsonDocument.Parse(observation.Summary);
        Assert.Equal(Now, body.RootElement.GetProperty("utcTime").GetDateTimeOffset());
        Assert.Equal(Now, observation.Timestamp);
        Assert.Null(observation.Error);
        Assert.Null(observation.ArtifactReference);
    }

    [Fact]
    public async Task AdditionalTool_IsDiscoveredFilteredAndDispatchedWithoutManagerChanges()
    {
        using var host = CreateHost();
        var (task, _) = await CreateTaskAsync(host.Services);
        var clock = new ManualTimeProvider(Now);
        var extra = new RecordingTool("echo");
        var manager = CreateManager(host.Services, clock, new GetCurrentTimeTool(clock), extra);
        var query = new ListToolDefinitionsRequest(task.SessionId, task.Id);

        var definitions = await manager.ListDefinitionsAsync(query, CancellationToken.None);
        Assert.Equal(new[] { "echo", "get_current_time" }, definitions.Select(item => item.Name));
        var filtered = await manager.ListDefinitionsAsync(query with { AllowedTools = ["echo"] }, CancellationToken.None);
        Assert.Equal("echo", Assert.Single(filtered).Name);
        Assert.Empty(await manager.ListDefinitionsAsync(query with { AllowedTools = [] }, CancellationToken.None));

        var request = Request(task, "echo");
        var observation = await manager.InvokeAsync(request, CancellationToken.None);
        Assert.Same(request, extra.LastRequest);
        Assert.Equal("echo result", observation.Summary);
    }

    [Fact]
    public async Task HostStart_RejectsDuplicateToolNames()
    {
        var builder = ApplicationHost.CreateBuilder([]);
        builder.Services.AddSingleton<ILocalTool>(new RecordingTool("get_current_time"));
        using var host = builder.Build();

        await Assert.ThrowsAsync<ArgumentException>(() => host.StartAsync());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""{"timezone":"local"}""")]
    [InlineData("{")]
    public async Task TimeTool_RejectsArgumentsOutsideDeclaredSchema(string arguments)
    {
        var tool = new GetCurrentTimeTool(new ManualTimeProvider(Now));
        var request = new InvokeToolRequest(new SessionId("s"), new TaskId("t"),
            new ToolCallRequest(new ToolCallId("call"), tool.Definition.Name, arguments));

        var error = await Record.ExceptionAsync(() => tool.InvokeAsync(request, CancellationToken.None));

        Assert.True(error is ArgumentException or JsonException);
    }

    [Fact]
    public async Task Invoke_RejectsUnknownToolsWrongSessionAndInactiveTasksBeforeDispatch()
    {
        using var host = CreateHost();
        var (task, owner) = await CreateTaskAsync(host.Services);
        var tool = new RecordingTool("echo");
        var manager = CreateManager(host.Services, TimeProvider.System, tool);
        var request = Request(task, "echo");

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            manager.InvokeAsync(Request(task, "missing"), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            manager.InvokeAsync(request with { SessionId = new SessionId("other") }, CancellationToken.None));
        host.Services.GetRequiredService<ITaskStore>().FinishTask(task.Id, owner, "done", null, false, Now);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.InvokeAsync(request, CancellationToken.None));
        Assert.Equal(0, tool.Calls);
    }

    [Fact]
    public async Task Invoke_RejectsTasksOwnedByAnotherUser()
    {
        using var host = CreateHost();
        var services = host.Services;
        var other = await services.GetRequiredService<IUserManager>()
            .CreateAsync(new CreateUserRequest("Other"), CancellationToken.None);
        var session = services.GetRequiredService<ISessionStore>()
            .Add(other.Id, "Other", Now, CancellationToken.None);
        var store = services.GetRequiredService<ITaskStore>();
        var task = store.CreateTask(new SubmitTaskRequest(session.Id, "test"), other.Id, Now, CancellationToken.None);
        store.StartTask(task.Id, other.Id, Now);
        var tool = new RecordingTool("echo");
        var manager = CreateManager(services, TimeProvider.System, tool);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            manager.InvokeAsync(Request(task, "echo"), CancellationToken.None));
        Assert.Equal(0, tool.Calls);
    }

    [Fact]
    public async Task Invoke_ReservesCallIdBeforeExecutionAndRejectsDuplicateOrChangedRequests()
    {
        using var host = CreateHost();
        var (task, _) = await CreateTaskAsync(host.Services);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tool = new RecordingTool("echo")
        {
            Execute = async token =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(token);
                return new ToolResult(ToolCallStatus.Succeeded, "done");
            }
        };
        var manager = CreateManager(host.Services, TimeProvider.System, tool);
        var request = Request(task, "echo");
        var first = manager.InvokeAsync(request, CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.InvokeAsync(request, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.InvokeAsync(
                request with { Call = request.Call with { ArgumentsJson = """{"changed":true}""" } }, CancellationToken.None));
        }
        finally
        {
            release.TrySetResult();
            await first;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.InvokeAsync(request, CancellationToken.None));
        Assert.Equal(1, tool.Calls);
    }

    [Fact]
    public async Task Invoke_PropagatesCancellationAndDoesNotRetryCancelledCalls()
    {
        using var host = CreateHost();
        var (task, _) = await CreateTaskAsync(host.Services);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tool = new RecordingTool("echo")
        {
            Execute = async token =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new ToolResult(ToolCallStatus.Succeeded, "unreachable");
            }
        };
        var manager = CreateManager(host.Services, TimeProvider.System, tool);
        using var cancellation = new CancellationTokenSource();
        var request = Request(task, "echo");
        var execution = manager.InvokeAsync(request, cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await cancellation.CancelAsync();
        }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.InvokeAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task Invoke_RequestsCooperativeTimeout()
    {
        using var host = CreateHost();
        var (task, _) = await CreateTaskAsync(host.Services);
        var tool = new RecordingTool("echo")
        {
            Definition = new ToolDefinition("echo", "Waits for cancellation.", """{"type":"object"}""",
                Timeout: TimeSpan.FromMilliseconds(20)),
            Execute = async token =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new ToolResult(ToolCallStatus.Succeeded, "unreachable");
            }
        };
        var manager = CreateManager(host.Services, TimeProvider.System, tool);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            manager.InvokeAsync(Request(task, "echo"), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Invoke_PreservesUnknownOutcomeAndRejectsOversizedOutput()
    {
        using var host = CreateHost();
        var (task, _) = await CreateTaskAsync(host.Services);
        var tool = new RecordingTool("echo")
        {
            Execute = _ => Task.FromResult(new ToolResult(ToolCallStatus.OutcomeUnknown, null, "Response lost."))
        };
        var manager = CreateManager(host.Services, TimeProvider.System, tool);
        var observation = await manager.InvokeAsync(Request(task, "echo"), CancellationToken.None);
        Assert.Equal(ToolCallStatus.OutcomeUnknown, observation.Status);
        Assert.Equal("Response lost.", observation.Error);

        tool.Execute = _ => Task.FromResult(new ToolResult(ToolCallStatus.Succeeded, new string('x', 8193)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.InvokeAsync(Request(task, "echo"), CancellationToken.None));
    }

    private static IHost CreateHost() => ApplicationHost.CreateBuilder([]).Build();

    private static async Task<(TaskInfo Task, HanyingAgent.Contracts.Management.Users.Models.UserId Owner)>
        CreateTaskAsync(IServiceProvider services)
    {
        var owner = await services.GetRequiredService<LocalUserContext>().GetUserIdAsync(CancellationToken.None);
        var session = await services.GetRequiredService<ISessionManager>()
            .CreateAsync(new CreateSessionRequest(owner, "Tools"), CancellationToken.None);
        var store = services.GetRequiredService<ITaskStore>();
        var task = store.CreateTask(new SubmitTaskRequest(session.Id, "test"), owner, Now, CancellationToken.None);
        return (store.StartTask(task.Id, owner, Now), owner);
    }

    private static LocalToolManager CreateManager(
        IServiceProvider services, TimeProvider clock, params ILocalTool[] tools) =>
        new(tools, services.GetRequiredService<ITaskStore>(),
            services.GetRequiredService<LocalUserContext>(), clock);

    private static InvokeToolRequest Request(TaskInfo task, string name = "get_current_time") =>
        new(task.SessionId, task.Id, new ToolCallRequest(new ToolCallId(Guid.NewGuid().ToString("N")), name, "{}"));

    private sealed class RecordingTool(string name) : ILocalTool
    {
        public ToolDefinition Definition { get; init; } = new(name, "A test tool.", """{"type":"object"}""");
        public Func<CancellationToken, Task<ToolResult>> Execute { get; set; } =
            _ => Task.FromResult(new ToolResult(ToolCallStatus.Succeeded, $"{name} result"));
        public InvokeToolRequest? LastRequest { get; private set; }
        public int Calls { get; private set; }

        public Task<ToolResult> InvokeAsync(InvokeToolRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            return Execute(cancellationToken);
        }
    }
}

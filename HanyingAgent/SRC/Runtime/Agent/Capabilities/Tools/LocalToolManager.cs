using System.Diagnostics;
using System.Text.Json;
using HanyingAgent.Contracts.Agent.Capabilities.Tools.Models;
using HanyingAgent.Contracts.Agent.Capabilities.Tools.Operations;
using HanyingAgent.Contracts.Agent.Capabilities.Tools.Operations.Requests;
using HanyingAgent.Contracts.Agent.Capabilities.Tools.Operations.Responses;
using HanyingAgent.Contracts.Management.Sessions.Models;
using HanyingAgent.Contracts.Management.Tasks.Models;
using HanyingAgent.Runtime.Management.Tasks;
using HanyingAgent.Runtime.Management.Users;
using TaskStatus = HanyingAgent.Contracts.Management.Tasks.Models.TaskStatus;

namespace HanyingAgent.Runtime.Agent.Capabilities.Tools;

public sealed class LocalToolManager : IToolManager
{
    private const int MaxOutputCharacters = 8192;
    private readonly Dictionary<string, Registration> _tools = new(StringComparer.Ordinal);
    private readonly HashSet<ToolCallId> _calls = new();
    private readonly object _gate = new();
    private readonly ITaskStore _tasks;
    private readonly LocalUserContext _identity;
    private readonly TimeProvider _clock;

    public LocalToolManager(
        IEnumerable<ILocalTool> tools, ITaskStore tasks, LocalUserContext identity, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(clock);
        _tasks = tasks;
        _identity = identity;
        _clock = clock;
        foreach (var tool in tools)
        {
            ArgumentNullException.ThrowIfNull(tool);
            var definition = tool.Definition;
            ArgumentNullException.ThrowIfNull(definition);
            ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);
            ArgumentException.ThrowIfNullOrWhiteSpace(definition.Description);
            using var schema = JsonDocument.Parse(definition.InputJsonSchema);
            if (schema.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Tool input schemas must be JSON objects.", nameof(tools));
            }
            if (definition.Timeout is { } timeout &&
                (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1))
            {
                throw new ArgumentException("Tool timeout must fit a positive cancellation timer.", nameof(tools));
            }
            if (!_tools.TryAdd(definition.Name, new Registration(definition, tool)))
            {
                throw new ArgumentException($"Duplicate tool name: {definition.Name}.", nameof(tools));
            }
        }
    }

    public async Task<IReadOnlyList<ToolDefinition>> ListDefinitionsAsync(
        ListToolDefinitionsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await RequireActiveTaskAsync(request.SessionId, request.TaskId, cancellationToken);
        var allowed = request.AllowedTools?.ToArray();
        if (allowed is not null)
        {
            foreach (var name in allowed)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(name);
                if (!_tools.ContainsKey(name))
                {
                    throw new KeyNotFoundException($"Tool is not registered: {name}.");
                }
            }
        }
        return _tools.Values
            .Where(item => allowed is null || allowed.Contains(item.Definition.Name, StringComparer.Ordinal))
            .Select(item => item.Definition)
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<ToolObservation> InvokeAsync(
        InvokeToolRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Call);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Call.Id.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Call.ToolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Call.ArgumentsJson);
        await RequireActiveTaskAsync(request.SessionId, request.TaskId, cancellationToken);
        if (!_tools.TryGetValue(request.Call.ToolName, out var registration))
        {
            throw new KeyNotFoundException($"Tool is not registered: {request.Call.ToolName}.");
        }
        using (var arguments = JsonDocument.Parse(request.Call.ArgumentsJson))
        {
            if (arguments.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Tool arguments must be a JSON object.", nameof(request));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_calls.Add(request.Call.Id))
            {
                throw new InvalidOperationException("Tool call ID has already been used; automatic retries are not supported.");
            }
        }

        // Keep the ID reserved even on failure: an exception does not prove that no action occurred.
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (registration.Definition.Timeout is { } timeout)
        {
            cancellation.CancelAfter(timeout);
        }
        var started = Stopwatch.GetTimestamp();
        var result = await registration.Tool.InvokeAsync(request, cancellation.Token);
        if (result is null || !Enum.IsDefined(result.Status) ||
            (result.Status == ToolCallStatus.Succeeded &&
                (string.IsNullOrWhiteSpace(result.Content) || result.Error is not null)) ||
            (result.Status != ToolCallStatus.Succeeded && string.IsNullOrWhiteSpace(result.Error)))
        {
            throw new InvalidOperationException("The tool returned an invalid result.");
        }
        if ((long)(result.Content?.Length ?? 0) + (result.Error?.Length ?? 0) > MaxOutputCharacters)
        {
            throw new InvalidOperationException("Tool output exceeds the inline observation limit; artifact output is not implemented.");
        }
        return new ToolObservation(request.Call.Id, registration.Definition.Name, result.Status,
            result.Content ?? string.Empty, result.Error, null, _clock.GetUtcNow(), Stopwatch.GetElapsedTime(started));
    }

    private async Task RequireActiveTaskAsync(
        SessionId sessionId, TaskId taskId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId.Value);
        var owner = await _identity.GetUserIdAsync(cancellationToken);
        var task = _tasks.GetTask(taskId, owner, cancellationToken)
            ?? throw new KeyNotFoundException("Task does not exist.");
        if (task.SessionId != sessionId)
        {
            throw new ArgumentException("Task does not belong to the requested session.", nameof(sessionId));
        }
        if (task.Status is not (TaskStatus.Running or TaskStatus.WaitingForModel or TaskStatus.WaitingForTool))
        {
            throw new InvalidOperationException("Task is not active for tool actions.");
        }
    }

    private sealed record Registration(ToolDefinition Definition, ILocalTool Tool);
}

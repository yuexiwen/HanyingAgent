using System.Text.Json;
using HanyingAgent.Contracts.Execution.Models;
using HanyingAgent.Contracts.Tasks.Models;
using HanyingAgent.Contracts.Tasks.Operations;
using HanyingAgent.Contracts.Tasks.Operations.Requests;
using HanyingAgent.Contracts.Tasks.Operations.Responses;
using HanyingAgent.Contracts.Users.Models;
using HanyingAgent.Diagnostics;
using HanyingAgent.Runtime.Conversation;
using HanyingAgent.Runtime.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskStatus = HanyingAgent.Contracts.Tasks.Models.TaskStatus;

namespace HanyingAgent.Runtime.Orchestration;

public sealed class LocalTaskManager(
    ITaskStore store,
    LocalUserContext identity,
    IServiceScopeFactory scopeFactory,
    TaskExecutionOptions options,
    TimeProvider clock,
    ILogger<LocalTaskManager> logger) : ITaskManager, IHostedService
{
    private readonly object _gate = new();
    private readonly Dictionary<TaskId, ExecutionHandle> _running = new();
    private bool _accepting;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _accepting = true;
        }
        return Task.CompletedTask;
    }

    public async Task<TaskInfo> SubmitAsync(SubmitTaskRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Input);
        var owner = await identity.GetUserIdAsync(cancellationToken);
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_accepting)
            {
                throw new InvalidOperationException("Task service is not running or is stopping.");
            }
            var task = store.CreateTask(request, owner, clock.GetUtcNow(), cancellationToken);
            var handle = new ExecutionHandle(owner);
            _running.Add(task.Id, handle);
            logger.LogInformation(LogEvents.TaskAccepted,
                "Task accepted: {TaskId}, session {SessionId}, version {Version}",
                task.Id.Value, task.SessionId.Value, task.Version);
            handle.Work = Task.Run(() => RunAsync(task, handle), CancellationToken.None);
            return task;
        }
    }

    public async Task<TaskInfo?> GetAsync(TaskId taskId, CancellationToken cancellationToken)
    {
        ValidateId(taskId);
        return store.GetTask(taskId, await identity.GetUserIdAsync(cancellationToken), cancellationToken);
    }

    public async Task<TaskListResponse> ListBySessionAsync(
        ListSessionTasksRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId.Value);
        if (request.PageSize is < 1 or > 200 || (request.Status is { } status && !Enum.IsDefined(status)))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Invalid page size or task status.");
        }
        return store.ListTasks(request, await identity.GetUserIdAsync(cancellationToken), cancellationToken);
    }

    public async Task<TaskResult?> GetResultAsync(TaskId taskId, CancellationToken cancellationToken)
    {
        ValidateId(taskId);
        return store.GetTaskResult(taskId, await identity.GetUserIdAsync(cancellationToken), cancellationToken);
    }

    public async Task<TaskInfo> ProvideInputAsync(
        ProvideTaskInputRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = await GetAsync(request.TaskId, cancellationToken)
            ?? throw new KeyNotFoundException("Task does not exist.");
        throw new NotSupportedException("Waiting for user input is not supported by the Direct task strategy.");
    }

    public async Task<TaskInfo> CancelAsync(TaskId taskId, CancellationToken cancellationToken)
    {
        ValidateId(taskId);
        var owner = await identity.GetUserIdAsync(cancellationToken);
        TaskInfo snapshot;
        Task callbacks;
        lock (_gate)
        {
            snapshot = store.RequestCancellation(taskId, owner, clock.GetUtcNow(), cancellationToken);
            callbacks = _running.TryGetValue(taskId, out var handle) &&
                snapshot.Status == TaskStatus.Cancelling ? handle.CancelAsync() : Task.CompletedTask;
        }
        logger.LogInformation(LogEvents.TaskCancellationRequested,
            "Task cancellation requested: {TaskId}, session {SessionId}, status {Status}",
            snapshot.Id.Value, snapshot.SessionId.Value, snapshot.Status);
        await callbacks;
        return snapshot;
    }

    public async Task<TaskResult> WaitForResultAsync(TaskId taskId, CancellationToken cancellationToken)
    {
        ValidateId(taskId);
        var owner = await identity.GetUserIdAsync(cancellationToken);
        Task work;
        lock (_gate)
        {
            var result = store.GetTaskResult(taskId, owner, cancellationToken);
            if (result is not null)
            {
                return result;
            }
            work = _running.TryGetValue(taskId, out var handle) ? handle.Work
                : throw new InvalidOperationException("An unfinished task has no execution handle.");
        }
        await work.WaitAsync(cancellationToken);
        return store.GetTaskResult(taskId, owner, cancellationToken)
            ?? throw new InvalidOperationException("Task stopped without a terminal result.");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task[] work;
        lock (_gate)
        {
            _accepting = false;
            foreach (var (id, handle) in _running)
            {
                var snapshot = store.RequestCancellation(id, handle.Owner, clock.GetUtcNow(), CancellationToken.None);
                if (snapshot.Status == TaskStatus.Cancelling)
                {
                    _ = handle.CancelAsync();
                }
            }
            work = _running.Values.Select(handle => handle.Work).ToArray();
        }
        logger.LogInformation(LogEvents.TaskServiceStopping, "Task service stopping; waiting for {TaskCount} executions", work.Length);
        await Task.WhenAll(work).WaitAsync(cancellationToken);
    }

    private async Task RunAsync(TaskInfo accepted, ExecutionHandle handle)
    {
        using var taskScope = logger.BeginScope("TaskId:{TaskId}", accepted.Id.Value);
        using var sessionScope = logger.BeginScope("SessionId:{SessionId}", accepted.SessionId.Value);
        string? answer = null;
        string? error = null;
        var cancelled = false;
        try
        {
            LogTransition(store.StartTask(accepted.Id, handle.Owner, clock.GetUtcNow()));
            handle.Token.ThrowIfCancellationRequested();
            using var scope = scopeFactory.CreateScope();
            var outcome = await scope.ServiceProvider.GetRequiredService<DialogueCoordinator>().RespondTaskAsync(
                new DialogueRequest(accepted.SessionId, accepted.Input, options.SystemInstruction,
                    options.MaxInputCharacters, options.MaxHistoryTurns, options.ModelOptions),
                accepted.Id,
                () => LogTransition(store.WaitForModel(accepted.Id, handle.Owner, clock.GetUtcNow())),
                handle.Token);
            answer = outcome.Text;
            error = OutcomeError(outcome.Conclusion);
        }
        catch (OperationCanceledException) when (handle.Token.IsCancellationRequested)
        {
            cancelled = true;
            error = "Task cancelled after execution stopped.";
        }
        catch (OperationCanceledException)
        {
            error = "Request timed out or dependency cancelled. See logs.";
        }
        catch (Exception exception)
        {
            // This background boundary must turn even unexpected failures into an observable terminal task.
            error = "Request failed during task execution. See logs.";
            if (exception is not (HttpRequestException or JsonException or ArgumentException or
                InvalidOperationException or NotSupportedException or KeyNotFoundException or
                UnauthorizedAccessException or IOException))
            {
                logger.LogError(LogEvents.TaskUnexpectedFailure, exception, "Unexpected task execution failure");
            }
            else
            {
                logger.LogWarning(LogEvents.TaskExecutionFailed, exception, "Task execution failed; no automatic retry");
            }
        }
        try
        {
            var result = store.FinishTask(accepted.Id, handle.Owner, answer, error, cancelled, clock.GetUtcNow());
            logger.Log(result.Status == TaskStatus.Failed ? LogLevel.Warning : LogLevel.Information,
                LogEvents.TaskFinished, "Task finished with {Status}; result saved and session released", result.Status);
        }
        catch (Exception exception)
        {
            logger.LogCritical(LogEvents.TaskFinalizationFailed, exception,
                "Task finalization failed; session release and terminal result are not confirmed");
            throw;
        }
        finally
        {
            Task callbacks;
            lock (_gate)
            {
                callbacks = handle.CancellationCallbacks;
            }
            try
            {
                await callbacks;
            }
            catch (Exception exception)
            {
                logger.LogError(LogEvents.TaskCancellationFailed, exception, "Task cancellation callback failed");
                throw;
            }
            finally
            {
                lock (_gate)
                {
                    _running.Remove(accepted.Id);
                    handle.Dispose();
                }
            }
        }
    }

    private void LogTransition(TaskInfo task) =>
        logger.LogInformation(LogEvents.TaskStateChanged,
            "Task state changed: {Status}, version {Version}", task.Status, task.Version);

    private static void ValidateId(TaskId id) => ArgumentException.ThrowIfNullOrWhiteSpace(id.Value);

    private static string? OutcomeError(ExecutionConclusion conclusion) => conclusion switch
    {
        ExecutionConclusion.Delivered => null,
        ExecutionConclusion.Incomplete => "Output truncated; this is not a complete answer.",
        ExecutionConclusion.Unconfirmed => "Completion is unknown; do not treat this as a complete answer.",
        ExecutionConclusion.Refused => "The model refused the request.",
        ExecutionConclusion.Filtered => "Output was filtered.",
        ExecutionConclusion.UnsupportedAction => "Tool execution is not supported.",
        _ => throw new InvalidOperationException("Unsupported execution conclusion.")
    };

    private sealed class ExecutionHandle(UserId owner) : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        public UserId Owner { get; } = owner;
        public CancellationToken Token => _cancellation.Token;
        public Task Work { get; set; } = Task.CompletedTask;
        public Task CancellationCallbacks { get; private set; } = Task.CompletedTask;
        private bool _cancelRequested;

        public Task CancelAsync()
        {
            if (!_cancelRequested)
            {
                _cancelRequested = true;
                CancellationCallbacks = _cancellation.CancelAsync();
            }
            return CancellationCallbacks;
        }

        public void Dispose() => _cancellation.Dispose();
    }
}

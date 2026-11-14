using Microsoft.Extensions.Logging;

namespace HanyingAgent.Diagnostics;

public static class LogEvents
{
    public static readonly EventId ExecutionStarted = new(1001, nameof(ExecutionStarted));
    public static readonly EventId ExecutionReturned = new(1002, nameof(ExecutionReturned));
    public static readonly EventId ExecutionFailed = new(1003, nameof(ExecutionFailed));
    public static readonly EventId ExecutionCancelled = new(1004, nameof(ExecutionCancelled));
    public static readonly EventId ExecutionTimedOutOrDependencyCancelled = new(1005, nameof(ExecutionTimedOutOrDependencyCancelled));

    public static readonly EventId ContextBuildStarted = new(2001, nameof(ContextBuildStarted));
    public static readonly EventId ContextBuildCompleted = new(2002, nameof(ContextBuildCompleted));

    public static readonly EventId ModelInvocationStarted = new(3001, nameof(ModelInvocationStarted));
    public static readonly EventId ModelInvocationReturned = new(3002, nameof(ModelInvocationReturned));

    public static readonly EventId HistoryWindowSelected = new(2003, nameof(HistoryWindowSelected));
    public static readonly EventId DialogueInputRecorded = new(4001, nameof(DialogueInputRecorded));
    public static readonly EventId DialogueResponseRecorded = new(4002, nameof(DialogueResponseRecorded));
    public static readonly EventId DialogueFailed = new(4003, nameof(DialogueFailed));
    public static readonly EventId DialogueCancelled = new(4004, nameof(DialogueCancelled));
    public static readonly EventId SessionCommandFailed = new(4005, nameof(SessionCommandFailed));
    public static readonly EventId DialogueResponseNotRecorded = new(4006, nameof(DialogueResponseNotRecorded));
    public static readonly EventId TaskAccepted = new(5001, nameof(TaskAccepted));
    public static readonly EventId TaskStateChanged = new(5002, nameof(TaskStateChanged));
    public static readonly EventId TaskCancellationRequested = new(5003, nameof(TaskCancellationRequested));
    public static readonly EventId TaskFinished = new(5004, nameof(TaskFinished));
    public static readonly EventId TaskServiceStopping = new(5005, nameof(TaskServiceStopping));
    public static readonly EventId TaskUnexpectedFailure = new(5006, nameof(TaskUnexpectedFailure));
    public static readonly EventId TaskFinalizationFailed = new(5007, nameof(TaskFinalizationFailed));
    public static readonly EventId TaskCancellationFailed = new(5008, nameof(TaskCancellationFailed));
    public static readonly EventId TaskExecutionFailed = new(5009, nameof(TaskExecutionFailed));
}

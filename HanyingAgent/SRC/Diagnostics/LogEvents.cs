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
}

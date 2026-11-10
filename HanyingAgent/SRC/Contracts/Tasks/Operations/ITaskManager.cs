using HanyingAgent.Contracts.Tasks.Models;
using HanyingAgent.Contracts.Tasks.Operations.Requests;
using HanyingAgent.Contracts.Tasks.Operations.Responses;

namespace HanyingAgent.Contracts.Tasks.Operations;

public interface ITaskManager
{
    /// <summary>
    /// Returns an accepted task snapshot without waiting for completion.
    /// </summary>
    Task<TaskInfo> SubmitAsync(
        SubmitTaskRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns null when the task does not exist.
    /// </summary>
    Task<TaskInfo?> GetAsync(
        TaskId taskId,
        CancellationToken cancellationToken);

    Task<TaskListResponse> ListBySessionAsync(
        ListSessionTasksRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns null for an unfinished task and rejects an unknown task.
    /// </summary>
    Task<TaskResult?> GetResultAsync(
        TaskId taskId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Supplies input only to a task waiting for the user at the expected version.
    /// </summary>
    Task<TaskInfo> ProvideInputAsync(
        ProvideTaskInputRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Requests cancellation; the returned snapshot does not guarantee cancellation has completed.
    /// </summary>
    Task<TaskInfo> CancelAsync(
        TaskId taskId,
        CancellationToken cancellationToken);
}

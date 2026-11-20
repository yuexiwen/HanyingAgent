using HanyingAgent.Contracts.Management.Tasks.Models;
using HanyingAgent.Contracts.Management.Tasks.Operations.Requests;
using HanyingAgent.Contracts.Management.Tasks.Operations.Responses;

namespace HanyingAgent.Contracts.Management.Tasks.Operations;

/// <summary>
/// Provides caller-facing operations for submitting, querying, and controlling business tasks.
/// </summary>
/// <remarks>
/// Callers, such as a console application or an HTTP API, manage tasks through this contract
/// without directly invoking the internal agent execution flow.
/// Host lifecycle management and background execution entry points are outside this contract.
/// </remarks>
public interface ITaskManager
{
    /// <summary>
    /// Submits input as a new business task in the specified session.
    /// </summary>
    /// <remarks>
    /// Successful submission confirms acceptance, not completion of the agent execution.
    /// The session must belong to the caller and be available for a new task.
    /// </remarks>
    /// <param name="request">The target session and input to execute.</param>
    /// <param name="cancellationToken">
    /// Cancels the submission operation, not the lifetime of an accepted task.
    /// Use <see cref="CancelAsync"/> to request cancellation after acceptance.
    /// </param>
    /// <returns>A snapshot of the accepted task, including its identifier.</returns>
    Task<TaskInfo> SubmitAsync(
        SubmitTaskRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves the current snapshot of a business task accessible to the caller.
    /// </summary>
    /// <remarks>
    /// This operation does not wait for execution to finish. Use <see cref="GetResultAsync"/>
    /// to retrieve the terminal result.
    /// </remarks>
    /// <param name="taskId">The identifier of the task to query.</param>
    /// <param name="cancellationToken">Cancels this query operation.</param>
    /// <returns>The task snapshot, or null when the task does not exist.</returns>
    Task<TaskInfo?> GetAsync(
        TaskId taskId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists a page of business tasks in a session, from newest to oldest.
    /// </summary>
    /// <remarks>
    /// The session must belong to the caller. An optional status filter limits the results.
    /// Pass the returned continuation token unchanged with the same session and status filter
    /// to retrieve older tasks.
    /// </remarks>
    /// <param name="request">The session, optional status filter, page size, and continuation token.</param>
    /// <param name="cancellationToken">Cancels this query operation.</param>
    /// <returns>A page of task snapshots and a continuation token if more matching tasks remain.</returns>
    Task<TaskListResponse> ListBySessionAsync(
        ListSessionTasksRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves the saved terminal result of a business task accessible to the caller.
    /// </summary>
    /// <remarks>
    /// This operation does not wait for completion. An unknown task is rejected rather than
    /// treated as an unfinished task.
    /// </remarks>
    /// <param name="taskId">The identifier of the task whose result is requested.</param>
    /// <param name="cancellationToken">Cancels this query operation.</param>
    /// <returns>The saved result, or null while no terminal result has been saved.</returns>
    Task<TaskResult?> GetResultAsync(
        TaskId taskId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Supplies additional input to a task waiting for the user at the expected version.
    /// </summary>
    /// <remarks>
    /// The expected version guards against applying input to a task that has changed since it
    /// was observed. Implementations that do not support user-input continuation reject this
    /// operation with <see cref="System.NotSupportedException"/>.
    /// </remarks>
    /// <param name="request">The task identifier, expected version, and additional user input.</param>
    /// <param name="cancellationToken">Cancels this input-supply operation.</param>
    /// <returns>A task snapshot reflecting acceptance of the additional input.</returns>
    Task<TaskInfo> ProvideInputAsync(
        ProvideTaskInputRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Requests cooperative cancellation of a business task accessible to the caller.
    /// </summary>
    /// <remarks>
    /// This operation does not forcibly terminate execution or wait for all execution cleanup.
    /// The returned snapshot may still indicate cancellation in progress.
    /// Repeated requests do not restart cancellation, and terminal tasks remain unchanged.
    /// </remarks>
    /// <param name="taskId">The identifier of the task to cancel.</param>
    /// <param name="cancellationToken">
    /// Cancels this control operation; it is not the target task's execution token.
    /// </param>
    /// <returns>A task snapshot reflecting the cancellation request or the existing terminal state.</returns>
    Task<TaskInfo> CancelAsync(
        TaskId taskId,
        CancellationToken cancellationToken);
}

using HanyingAgent.Contracts.Management.Tasks.Models;
using HanyingAgent.Contracts.Management.Tasks.Operations.Requests;
using HanyingAgent.Contracts.Management.Tasks.Operations.Responses;
using HanyingAgent.Contracts.Management.Users.Models;

namespace HanyingAgent.Runtime.Management.Tasks;

public interface ITaskStore
{
    TaskInfo CreateTask(SubmitTaskRequest request, UserId owner, DateTimeOffset now, CancellationToken token);
    TaskInfo? GetTask(TaskId id, UserId owner, CancellationToken token);
    TaskListResponse ListTasks(ListSessionTasksRequest request, UserId owner, CancellationToken token);
    TaskResult? GetTaskResult(TaskId id, UserId owner, CancellationToken token);
    TaskInfo StartTask(TaskId id, UserId owner, DateTimeOffset now);
    TaskInfo WaitForModel(TaskId id, UserId owner, DateTimeOffset now);
    TaskInfo RequestCancellation(TaskId id, UserId owner, DateTimeOffset now, CancellationToken token);
    TaskResult FinishTask(TaskId id, UserId owner, string? answer, string? error,
        bool cancelled, DateTimeOffset now);
}

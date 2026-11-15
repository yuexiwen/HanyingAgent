using HanyingAgent.Contracts.Conversation.Models;
using HanyingAgent.Contracts.Sessions.Models;
using HanyingAgent.Contracts.Tasks.Models;
using HanyingAgent.Contracts.Tasks.Operations.Responses;
using TaskStatus = HanyingAgent.Contracts.Tasks.Models.TaskStatus;

namespace HanyingAgent.Host.Console;

internal sealed class ConsoleRenderer(TextWriter output)
{
    public Task WriteLineAsync(string text = "") => output.WriteLineAsync(text);

    public Task ShowHelpAsync() => WriteLineAsync(
        "HanyingAgent - local in-memory tasks. Replies appear automatically. /new NAME, /session [ID], /sessions, /use INDEX|ID, /delete INDEX, /history [COUNT|all], /tasks [CURSOR], /task [ID], /wait [ID], /cancel [ID], /close, /exit. Task commands default to the last submitted task. /delete hides without deleting data. Exit cancels running tasks.");

    public async Task ShowPromptAsync()
    {
        await output.WriteAsync("You> ");
        await output.FlushAsync();
    }

    public async Task RenderResultAsync(TaskResult result, bool includeStatus = true, bool includeBody = true)
    {
        if (includeStatus)
        {
            await WriteLineAsync($"Task: {result.TaskId.Value} [{result.Status}]");
        }
        if (!includeBody)
        {
            return;
        }
        if (!string.IsNullOrWhiteSpace(result.FinalAnswer))
        {
            await WriteLineAsync($"Assistant> {result.FinalAnswer}");
        }
        if (result.Error is { } error)
        {
            await WriteLineAsync($"[{error}]");
        }
    }

    public async Task RenderOtherSessionAsync(SessionId sessionId, TaskResult result)
    {
        await WriteLineAsync(
            $"[Session: {sessionId.Value}, Task: {result.TaskId.Value} [{result.Status}]. Use /use {sessionId.Value} to view the update.]");
        if (result.Error is { } error)
        {
            await WriteLineAsync($"[Task: {result.TaskId.Value}: {error}]");
        }
    }

    public async Task RenderSessionAsync(SessionInfo session, SessionId? current, TaskInfo? task)
    {
        await WriteLineAsync($"Session: {session.Id.Value} ({session.Name}) [{session.Status}]");
        await WriteLineAsync($"Selected: {(session.Id == current ? "yes" : "no")}");
        await WriteLineAsync($"Created: {session.CreatedAt:O}, last active: {session.LastActiveAt:O}");
        await WriteLineAsync(DescribeSessionTask(task));
        if (session.Status == SessionStatus.Active && task is null)
        {
            await WriteLineAsync("[A standalone dialogue is in progress without a TaskId.]");
        }
    }

    public Task RenderSessionRowAsync(int index, SessionInfo session, SessionId? current, TaskInfo? task) =>
        WriteLineAsync(
            $"{index} {(session.Id == current ? "*" : " ")} {session.Id.Value} {session.Name} [{session.Status}] | {DescribeSessionTask(task)}");

    public Task RenderTaskAsync(TaskInfo task) =>
        WriteLineAsync($"Task: {task.Id.Value} [{task.Status}] v{task.Version}");

    private static string DescribeSessionTask(TaskInfo? task)
    {
        if (task is null)
        {
            return "Task: none (no tasks submitted)";
        }
        var finished = task.Status is TaskStatus.Completed or TaskStatus.Failed or TaskStatus.Cancelled;
        return $"Task: {task.Id.Value} [{task.Status}] | Finished: {(finished ? "yes" : "no")}";
    }

    public async Task RenderHistoryAsync(IReadOnlyList<ConversationMessage> messages, CancellationToken token)
    {
        if (messages.Count == 0)
        {
            await WriteLineAsync("[No messages in this session.]");
        }
        for (var index = 0; index < messages.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var message = messages[index];
            var text = message.Content switch
            {
                UserConversationContent user => $"User: {user.Text}",
                AssistantConversationContent assistant => $"Assistant [{assistant.FinishReason}]: {assistant.Text}",
                _ => throw new NotSupportedException("Unsupported history content.")
            };
            await WriteLineAsync($"{message.Sequence} {text}");
            if (message.Content is UserConversationContent &&
                (index + 1 == messages.Count || messages[index + 1].Content is not AssistantConversationContent ||
                 messages[index + 1].TaskId != message.TaskId))
            {
                await WriteLineAsync("[No assistant response recorded for this turn.]");
            }
        }
    }
}
